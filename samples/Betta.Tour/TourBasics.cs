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
    /// Tour collection 1 of 3 — <b>Basics</b>. The least an author has to write.
    /// Start here: bare attributes, type inference doing all the work.
    /// See also <see cref="ITourIntermediate"/> and <see cref="ITourAdvanced"/>.
    /// </summary>
    [GrasshopperCollection("Tour", "1 Basics")]
    public interface ITourBasics : IBettaCollection
    {
        // Zero ceremony: no display name, no parameter attributes. Betta uses
        // the method name ("Double") as the component name and the CLR
        // parameter name ("x") as the input; the double return becomes a
        // single Number output.
        [GrasshopperMethod]
        double Double(double x);

        // Names, descriptions, defaults: name the component and inputs, and
        // seed DefaultValue so unwired sockets still compute (2 ^ 3 = 8).
        [GrasshopperMethod("Power", "Raise a base to an exponent")]
        double Power(
            [GrasshopperParameter("Base", "B", "The base", DefaultValue = 2.0)] double @base,
            [GrasshopperParameter("Exponent", "E", "The power to raise to", DefaultValue = 3.0)] double exponent);
    }

    public class TourBasics : ITourBasics
    {
        public double Double(double x) => x * 2.0;

        public double Power(double @base, double exponent) => Math.Pow(@base, exponent);
    }
}
