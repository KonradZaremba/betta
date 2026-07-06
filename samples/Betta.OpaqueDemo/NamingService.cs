// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Betta.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Betta.OpaqueDemo
{
    /// <summary>
    /// A plain service the demo collection can ctor-inject. The whole point of
    /// this file is to demonstrate Feature 2 — a plugin assembly registers its
    /// own DI service via IBettaModule, and a Betta collection class
    /// constructor-injects it instead of reaching for a static service locator.
    /// </summary>
    public interface INamingService
    {
        string LabelFor(GraphKind kind);
    }

    public class NamingService : INamingService
    {
        public string LabelFor(GraphKind kind) => kind switch
        {
            GraphKind.Triangle => "tri-poly",
            GraphKind.Square => "quad-poly",
            GraphKind.Star => "star-poly",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Betta discovers every IBettaModule in this assembly at startup (before
    /// BuildServiceProvider) and calls ConfigureServices, so INamingService is
    /// available to inject into any Betta collection's ctor.
    /// </summary>
    public class OpaqueDemoModule : IBettaModule
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<INamingService, NamingService>();
        }
    }
}
