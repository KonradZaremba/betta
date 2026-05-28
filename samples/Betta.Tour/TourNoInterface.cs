// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Betta.Attributes;
using Betta.Interfaces;

namespace Betta.Tour
{
    /// <summary>
    /// Tour collection 4 of 4 — <b>No-Interface</b>. The attributes live
    /// directly on a concrete class that implements <see cref="IBettaCollection"/>;
    /// there is no separate interface. This is the tersest authoring style —
    /// one type, no contract duplication, Dynamo-ZeroTouch-like.
    ///
    /// Reach for the interface style (see <see cref="ITourBasics"/>) instead
    /// when you want DI swappability, mocking in tests, or a published API
    /// surface. For a plain component pack, this class-direct form is enough.
    /// </summary>
    [GrasshopperCollection("Tour", "4 No-Interface")]
    public class TourDirect : IBettaCollection
    {
        [GrasshopperMethod("Negate", "Flip the sign of a number")]
        public double Negate(
            [GrasshopperParameter("Value", "X", "Number to negate", DefaultValue = 1.0)] double x) => -x;

        [GrasshopperMethod("Clamp01", "Clamp a number into the 0..1 range")]
        public double Clamp01(
            [GrasshopperParameter("Value", "X", "Number to clamp")] double x) =>
            x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x;

        // A more typical component: several labelled inputs (with defaults), an
        // explicitly named output via [return: GrasshopperOutput], and a real
        // block body. Remaps a value from a source range to a target range —
        // the everyday "map this slider to that range" utility.
        [GrasshopperMethod("Remap", "Remap a number from a source range to a target range")]
        [return: GrasshopperOutput("Result", "R", "The remapped value")]
        public double Remap(
            [GrasshopperParameter("Value", "V", "Number to remap")] double value,
            [GrasshopperParameter("Source Min", "S0", "Low end of the source range", DefaultValue = 0.0)] double sourceMin,
            [GrasshopperParameter("Source Max", "S1", "High end of the source range", DefaultValue = 1.0)] double sourceMax,
            [GrasshopperParameter("Target Min", "T0", "Low end of the target range", DefaultValue = 0.0)] double targetMin,
            [GrasshopperParameter("Target Max", "T1", "High end of the target range", DefaultValue = 100.0)] double targetMax)
        {
            var span = sourceMax - sourceMin;
            if (Math.Abs(span) < 1e-12)
                return targetMin; // zero-width source range — avoid divide-by-zero

            var t = (value - sourceMin) / span;
            return targetMin + t * (targetMax - targetMin);
        }
    }
}
