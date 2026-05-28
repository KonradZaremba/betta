// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Betta.Attributes;
using Betta.Interfaces;

namespace Betta.Quickstart
{
    /// <summary>
    /// Minimal Betta plugin authored against the <c>Betta.Abstractions</c>
    /// NuGet package (see the csproj). Demonstrates three headline features in
    /// one tiny interface:
    ///   • DefaultValue — Cube's input is seeded to 2.0 so an unwired socket
    ///     still produces a result.
    ///   • List input — Stats takes List&lt;double&gt;, inferred as a list access
    ///     input automatically (no [Access] attribute).
    ///   • Tuple output — Stats returns a named tuple, fanned out to one GH
    ///     output per element (Sum, Average).
    /// </summary>
    [GrasshopperCollection("Quickstart", "Demo")]
    public interface IQuickstart : IBettaCollection
    {
        [GrasshopperMethod("Cube", "x³ — cube of a number")]
        double Cube([GrasshopperParameter("Value", DefaultValue = 2.0)] double x);

        [GrasshopperMethod("Stats", "Sum and average of a list")]
        (double Sum, double Average) Stats(
            [GrasshopperParameter("Numbers", "N", "The numbers to summarize")] List<double> numbers);
    }

    public class Quickstart : IQuickstart
    {
        public double Cube(double x) => x * x * x;

        public (double Sum, double Average) Stats(List<double> numbers)
        {
            if (numbers == null || numbers.Count == 0) return (0d, 0d);
            var sum = numbers.Sum();
            return (sum, sum / numbers.Count);
        }
    }
}
