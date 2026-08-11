// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Betta.Interfaces;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Betta.Services
{
    public class SimpleService : ISimpleService
    {
        public SimpleService()
        {

        }

        //Gets Values form interface
        public double GetValue(List<double> list, double value1121212, double value21212, string testString, List<Point3d> pointTest)
        {
            var test = testString;
            return value21212 + value1121212;
        }

        //Adds 100 to double
        //TODO add desription atribute
        public double GetValue2(double value21212)
        {
            return value21212 + 100;
        }

        public Tuple<Circle, double, double> CreateCircleInfo(Point3d center, double radius)
        {
            var circle = new Circle(center, radius);
            var area = Math.PI * radius * radius;
            var circumference = 2 * Math.PI * radius;
            
            return new Tuple<Circle, double, double>(circle, area, circumference);
        }

        public List<double> ProcessNumbers(List<double> numbers, string operation)
        {
            if (numbers == null || !numbers.Any())
                return new List<double>();

            switch (operation?.ToLower())
            {
                case "square":
                    return numbers.Select(x => x * x).ToList();
                case "sqrt":
                    return numbers.Select(x => Math.Sqrt(Math.Abs(x))).ToList();
                case "double":
                    return numbers.Select(x => x * 2).ToList();
                case "half":
                    return numbers.Select(x => x / 2).ToList();
                default:
                    return numbers.ToList();
            }
        }

        public MathStatistics GetStatistics(List<double> numbers)
        {
            if (numbers == null || !numbers.Any())
                return new MathStatistics();

            var sortedNumbers = numbers.OrderBy(x => x).ToList();
            
            return new MathStatistics
            {
                Mean = numbers.Average(),
                Median = GetMedian(sortedNumbers),
                StandardDeviation = GetStandardDeviation(numbers),
                Min = numbers.Min(),
                Max = numbers.Max(),
                Count = numbers.Count
            };
        }

        private double GetMedian(List<double> sortedNumbers)
        {
            var count = sortedNumbers.Count;
            if (count % 2 == 0)
            {
                return (sortedNumbers[count / 2 - 1] + sortedNumbers[count / 2]) / 2.0;
            }
            else
            {
                return sortedNumbers[count / 2];
            }
        }

        private double GetStandardDeviation(List<double> numbers)
        {
            var mean = numbers.Average();
            var variance = numbers.Select(x => Math.Pow(x - mean, 2)).Average();
            return Math.Sqrt(variance);
        }

        public async Task<string> DelayedEcho(string message, double seconds)
        {
            var delay = TimeSpan.FromSeconds(Math.Max(0, seconds));
            await Task.Delay(delay);
            return message ?? string.Empty;
        }

        public IObservable<double> Ticker(double intervalSeconds)
            => new TickerObservable(TimeSpan.FromSeconds(Math.Max(0.1, intervalSeconds)));

        /// <summary>
        /// Hand-rolled observable (no System.Reactive dependency — this is demo
        /// content and Betta ships no Rx). Emits elapsed seconds since subscribe
        /// on a timer thread; BettaComponent marshals the re-solve to the UI
        /// thread and coalesces bursts, so the emission thread doesn't matter.
        /// Disposing the subscription stops the timer — BettaComponent does that
        /// when inputs change or the component leaves the canvas.
        /// </summary>
        private sealed class TickerObservable : IObservable<double>
        {
            private readonly TimeSpan _interval;

            public TickerObservable(TimeSpan interval) => _interval = interval;

            public IDisposable Subscribe(IObserver<double> observer)
            {
                var started = DateTime.UtcNow;
                var timer = new System.Threading.Timer(
                    _ => observer.OnNext((DateTime.UtcNow - started).TotalSeconds),
                    null, _interval, _interval);
                return timer;
            }
        }
    }
}
