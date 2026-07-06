// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Opt out of "return-type explosion" so a method returns a domain object
    /// as a single typed output instead of one output per public property.
    /// Betta auto-generates a typed Param/Goo pair for the type so other Betta
    /// methods can accept it as a single typed input. Use on the type itself
    /// (or its interface) to opt the whole type in, or on a method / return
    /// value for a one-off override.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.ReturnValue | AttributeTargets.Method |
        AttributeTargets.Class | AttributeTargets.Interface,
        AllowMultiple = false)]
    public class GrasshopperOpaqueAttribute : Attribute
    {
    }
}
