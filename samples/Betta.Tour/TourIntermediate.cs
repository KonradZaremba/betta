// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Betta.Attributes;
using Betta.Interfaces;

namespace Betta.Tour
{
    /// <summary>
    /// Tour collection 2 of 3 — <b>Intermediate</b>. Collections and multiple
    /// outputs, still with no framework types in the signatures.
    /// See also <see cref="ITourBasics"/> and <see cref="ITourAdvanced"/>.
    /// </summary>
    [GrasshopperCollection("Tour", "2 Intermediate")]
    public interface ITourIntermediate : IBettaCollection
    {
        // List in: List<T> on a parameter is auto-detected as a list-access
        // input — no [Access] attribute needed.
        [GrasshopperMethod("Sum", "Add up a list of numbers")]
        double Sum([GrasshopperParameter("Numbers", "N", "Values to add")] List<double> numbers);

        // List out: a List<T> return becomes a list output.
        [GrasshopperMethod("Series", "Integers 0 .. count-1")]
        List<int> Series([GrasshopperParameter("Count", "C", "How many", DefaultValue = 5)] int count);

        // Many outputs at once: a named ValueTuple fans out to one output per
        // element (Min, Max).
        [GrasshopperMethod("MinMax", "Smallest and largest of a list")]
        (double Min, double Max) MinMax(
            [GrasshopperParameter("Numbers", "N", "Values to scan")] List<double> numbers);
    }

    public class TourIntermediate : ITourIntermediate
    {
        public double Sum(List<double> numbers) => numbers?.Sum() ?? 0d;

        public List<int> Series(int count) =>
            Enumerable.Range(0, System.Math.Max(0, count)).ToList();

        public (double Min, double Max) MinMax(List<double> numbers)
        {
            if (numbers == null || numbers.Count == 0) return (0d, 0d);
            return (numbers.Min(), numbers.Max());
        }
    }
}
