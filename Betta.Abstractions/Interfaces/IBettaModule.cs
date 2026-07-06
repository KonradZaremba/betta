// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;

namespace Betta.Interfaces
{
    /// <summary>
    /// Optional hook for a plugin to register its own dependency-injection
    /// services. Betta discovers every concrete type implementing this
    /// interface across its own assembly and each loaded plugin assembly, and
    /// invokes ConfigureServices on a parameterless instance before
    /// BuildServiceProvider runs at startup.
    ///
    /// This lets a Betta collection constructor-inject dependencies (logging,
    /// custom services, options, …) sourced from the same plugin DLL, instead
    /// of reaching for a static service locator.
    ///
    /// <para>
    /// Runtime-loaded plugins (DLLs dropped into the Betta folder after Rhino
    /// is already running) currently do not get DI: the ServiceProvider is
    /// built once at startup, and rebuilding it would orphan resolved
    /// singletons. A plugin that needs services from a Betta collection must
    /// therefore ship in the Betta folder before Rhino starts.
    /// </para>
    /// </summary>
    public interface IBettaModule
    {
        void ConfigureServices(IServiceCollection services);
    }
}
