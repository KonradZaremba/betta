// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Betta.Components;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Pins the async result cache and its key (testing-brief Tier 1, bug #4).
    /// GH-free: nothing here loads Grasshopper or Rhino, so it runs on CI.
    /// </summary>
    public class TestAsyncResultCache
    {
        [Fact]
        public void Key_IgnoresFreshCancellationTokenEachSolve()
        {
            // The livelock: every solve hands the method a new token. If it leaked into
            // the key the cache never hit and every solve relaunched the task.
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();
            Assert.Equal(AsyncResultCache.KeyFor(new object[] { 3.0, first.Token }),
                         AsyncResultCache.KeyFor(new object[] { 3.0, second.Token }));
        }

        [Fact]
        public void Key_IgnoresFreshProgressSinkEachSolve()
        {
            Assert.Equal(AsyncResultCache.KeyFor(new object[] { "x", new Progress<int>() }),
                         AsyncResultCache.KeyFor(new object[] { "x", new Progress<int>() }));
        }

        [Fact]
        public void Key_ChangesWhenARealInputChanges()
        {
            Assert.NotEqual(AsyncResultCache.KeyFor(new object[] { 3.0 }),
                            AsyncResultCache.KeyFor(new object[] { 4.0 }));
        }

        [Fact]
        public void Key_IsStableForEqualValueTypes()
        {
            var guid = Guid.NewGuid();
            Assert.Equal(AsyncResultCache.KeyFor(new object[] { guid }),
                         AsyncResultCache.KeyFor(new object[] { new Guid(guid.ToByteArray()) }));
        }

        [Fact]
        public void Key_RecursesIntoListsInOrder()
        {
            Assert.Equal(AsyncResultCache.KeyFor(new object[] { new List<int> { 1, 2 } }),
                         AsyncResultCache.KeyFor(new object[] { new List<int> { 1, 2 } }));
            Assert.NotEqual(AsyncResultCache.KeyFor(new object[] { new List<int> { 1, 2 } }),
                            AsyncResultCache.KeyFor(new object[] { new List<int> { 2, 1 } }));
        }

        [Fact]
        public void Key_DistinguishesNullFromAValue()
        {
            Assert.NotEqual(AsyncResultCache.KeyFor(new object[] { null }),
                            AsyncResultCache.KeyFor(new object[] { "n" }));
        }

        [Fact]
        public void Lookup_GoesMissThenInFlightThenHit()
        {
            var cache = new AsyncResultCache(max: 4);
            Assert.Equal(AsyncResultCache.Lookup.Miss, cache.TryGet("k", out _));

            cache.MarkInFlight("k", Task.CompletedTask);
            Assert.Equal(AsyncResultCache.Lookup.InFlight, cache.TryGet("k", out _));

            cache.ClearInFlight("k");
            cache.Store("k", 42);
            Assert.Equal(AsyncResultCache.Lookup.Hit, cache.TryGet("k", out var value));
            Assert.Equal(42, value);
        }

        [Fact]
        public void ClearInFlight_WithoutStore_ReturnsToMiss()
        {
            // A faulted task clears its in-flight entry and caches nothing, so the
            // next solve relaunches instead of waiting forever.
            var cache = new AsyncResultCache(max: 4);
            cache.MarkInFlight("k", Task.CompletedTask);
            cache.ClearInFlight("k");
            Assert.Equal(AsyncResultCache.Lookup.Miss, cache.TryGet("k", out _));
        }

        [Fact]
        public void Store_FirstResultWins()
        {
            var cache = new AsyncResultCache(max: 4);
            cache.Store("k", "first");
            cache.Store("k", "second");
            cache.TryGet("k", out var value);
            Assert.Equal("first", value);
        }

        [Fact]
        public void Evict_DropsADeliveredResult()
        {
            // The memoization bug: a delivered result must not survive into the next
            // genuine re-trigger, or a non-deterministic method (image generation, an
            // API call) returns the same output forever on unchanged inputs.
            var cache = new AsyncResultCache(max: 4);
            cache.Store("k", 1);
            cache.Evict(new[] { "k" });
            Assert.Equal(AsyncResultCache.Lookup.Miss, cache.TryGet("k", out _));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Store_EvictsTheLeastRecentlyUsedPastMax()
        {
            var cache = new AsyncResultCache(max: 2);
            cache.Store("a", 1);
            cache.Store("b", 2);
            cache.TryGet("a", out _); // touch "a" so "b" becomes the oldest
            cache.Store("c", 3);

            Assert.Equal(2, cache.Count);
            Assert.Equal(AsyncResultCache.Lookup.Hit, cache.TryGet("a", out _));
            Assert.Equal(AsyncResultCache.Lookup.Miss, cache.TryGet("b", out _));
            Assert.Equal(AsyncResultCache.Lookup.Hit, cache.TryGet("c", out _));
        }
    }
}
