// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Custom validator for a parameter. <paramref name="ValidatorType"/>
    /// must be a concrete type implementing <see cref="Betta.Interfaces.IBettaValidator"/>
    /// with a public parameterless constructor. The runtime instantiates it
    /// per solve and calls Validate(value) — a non-null return is treated as
    /// the warning message and skips invocation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
    public class GrasshopperValidationAttribute : Attribute
    {
        public Type ValidatorType { get; }

        public GrasshopperValidationAttribute(Type validatorType)
        {
            ValidatorType = validatorType;
        }
    }
}
