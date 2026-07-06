// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

namespace Betta.Interfaces
{
    /// <summary>
    /// Extension point for a plugin (e.g. Betta.Pro) to gate which plugin
    /// assemblies are permitted to load and which entitlements are granted to
    /// the current session. Consumers implement this and register it via
    /// <see cref="IBettaModule.ConfigureServices"/>; the Betta runtime resolves
    /// the registered instance at startup and calls it before loading each
    /// plugin and before each entitlement-gated solve.
    ///
    /// If <b>no</b> implementation is registered, licensing is off — every
    /// plugin loads and every entitlement check passes. This preserves the
    /// zero-friction OSS mode; commercial gating is strictly additive.
    /// </summary>
    public interface IBettaLicenseGate
    {
        /// <summary>
        /// Return <c>false</c> to skip a plugin assembly entirely — its
        /// descriptors will not be registered with Grasshopper.
        /// Return <c>true</c> to allow the assembly to load. Called once per
        /// plugin, at startup.
        /// </summary>
        bool IsAssemblyAllowed(Assembly asm);

        /// <summary>
        /// Return <c>true</c> if the named entitlement is currently granted
        /// to the running session (typically because a valid license file
        /// present in the plugin folder lists it). Return <c>false</c> to
        /// have the component surface a warning and skip its method body.
        /// Called on every solve of a component decorated with
        /// <see cref="Betta.Attributes.GrasshopperRequiresEntitlementAttribute"/>.
        /// </summary>
        bool IsEntitlementGranted(string entitlement);
    }
}
