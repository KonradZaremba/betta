// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Numeric range check for a parameter. Applied before the service
    /// method is invoked — if the wired value falls outside [Min, Max], the
    /// component surfaces a Warning and skips invocation rather than letting
    /// the method body throw on a garbage input.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class GrasshopperRangeAttribute : Attribute
    {
        public double Min { get; }
        public double Max { get; }

        public GrasshopperRangeAttribute(double min, double max)
        {
            Min = min;
            Max = max;
        }
    }
}
