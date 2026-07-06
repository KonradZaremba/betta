// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Declares that a method (or every method of a class) requires a named
    /// entitlement to run. <b>This attribute is inert by default</b> — it
    /// annotates a component but does not itself enforce anything. Enforcement
    /// happens when a plugin implementing <c>IBettaLicenseGate</c> is
    /// registered in DI (typically the Betta.Pro extension).
    ///
    /// Design intent: the attribute lives in Betta.Abstractions so plugin
    /// authors can decorate their commercial methods today. If no license
    /// gate is present in the running Betta install, the annotation is a no-op
    /// and the method runs freely — the OSS mode. If Betta.Pro (or another
    /// gate) is installed, the gate is consulted before every solve and can
    /// block or allow the invocation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class GrasshopperRequiresEntitlementAttribute : Attribute
    {
        /// <summary>Entitlement key expected on the current license.</summary>
        public string Entitlement { get; }

        public GrasshopperRequiresEntitlementAttribute(string entitlement)
        {
            Entitlement = entitlement;
        }
    }
}
