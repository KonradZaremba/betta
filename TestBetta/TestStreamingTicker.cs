// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Betta.Interfaces;
using Betta.Services;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Headless tests for the streaming demo (SimpleService.Ticker) — the
    /// IObservable side of the 0.7.0 streaming feature that runs without
    /// Grasshopper. The BettaComponent subscribe/coalesce/dispose plumbing is
    /// GH-coupled and is exercised manually via the Ticker component on canvas.
    /// </summary>
    public class TestStreamingTicker
    {
        private sealed class Collector : IObserver<double>
        {
            private readonly List<double> _values = new();
            public void OnNext(double value) { lock (_values) _values.Add(value); }
            public void OnError(Exception error) { }
            public void OnCompleted() { }
            public int Count { get { lock (_values) return _values.Count; } }
            public List<double> Snapshot() { lock (_values) return _values.ToList(); }
        }

        /// <summary>Poll until the collector has at least <paramref name="count"/> values.</summary>
        private static bool WaitForCount(Collector c, int count, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (c.Count >= count) return true;
                Thread.Sleep(25);
            }
            return c.Count >= count;
        }

        [Fact]
        public void Ticker_EmitsIncreasingElapsedSeconds()
        {
            // Also the "emits repeatedly" guard: the monotonicity assertions
            // below only run once >=3 emissions arrived.
            var obs = new SimpleService().Ticker(0.1);
            var c = new Collector();

            using (obs.Subscribe(c))
            {
                WaitForCount(c, 3);
            }

            var values = c.Snapshot();
            Assert.True(values.Count >= 3, "Not enough emissions to check ordering.");
            Assert.All(values, v => Assert.True(v > 0, "Elapsed seconds must be positive."));
            for (int i = 1; i < values.Count; i++)
                Assert.True(values[i] > values[i - 1],
                    $"Values must increase monotonically: {values[i - 1]} then {values[i]}.");
        }

        [Fact]
        public void Ticker_DisposeStopsEmissions()
        {
            var obs = new SimpleService().Ticker(0.1);
            var c = new Collector();

            var sub = obs.Subscribe(c);
            WaitForCount(c, 2);
            sub.Dispose();

            // A callback already in flight at Dispose may still land — drain,
            // then require silence over a 3-interval window.
            Thread.Sleep(150);
            var afterDrain = c.Count;
            Thread.Sleep(300);

            Assert.Equal(afterDrain, c.Count);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void Ticker_NonPositiveInterval_IsClampedNotThrown(double interval)
        {
            // The clamp floors at 0.1s: subscribing must not throw and must tick.
            var obs = new SimpleService().Ticker(interval);
            var c = new Collector();

            using (obs.Subscribe(c))
            {
                Assert.True(WaitForCount(c, 1), "Clamped ticker never emitted.");
            }
        }

        [Fact]
        public void Ticker_SubscriptionsAreIndependent()
        {
            // BettaComponent re-subscribes on input change; a disposed old
            // subscription must not kill the new one.
            var svc = new SimpleService();
            var first = new Collector();
            var second = new Collector();

            var subFirst = svc.Ticker(0.1).Subscribe(first);
            var subSecond = svc.Ticker(0.1).Subscribe(second);

            WaitForCount(first, 1);
            subFirst.Dispose();

            var secondBefore = second.Count;
            Assert.True(WaitForCount(second, secondBefore + 2),
                "Second subscription stopped after the first was disposed.");
            subSecond.Dispose();
        }

        [Fact]
        public void Ticker_IsDiscoveredAsAComponent()
        {
            var registry = new ComponentRegistry();
            registry.DiscoverFromAssembly(Assembly.GetAssembly(typeof(ISimpleService)));

            var ticker = registry.GetDescriptors().FirstOrDefault(d => d.Name == "Ticker");

            Assert.NotNull(ticker);
            Assert.Equal(typeof(IObservable<double>), ticker.Method.ReturnType);
            Assert.NotEqual(Guid.Empty, ticker.Guid);
        }
    }
}
