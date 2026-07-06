// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Betta.Interfaces;
using Betta.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TestBetta
{
    public interface ISampleGeometryService { string Hello(); }

    public class SampleGeometryService : ISampleGeometryService
    {
        public string Hello() => "geom";
    }

    public class GoodModule : IBettaModule
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ISampleGeometryService, SampleGeometryService>();
        }
    }

    public class FaultyModule : IBettaModule
    {
        public void ConfigureServices(IServiceCollection services)
        {
            throw new InvalidOperationException("module deliberately failing");
        }
    }

    public class TestModuleDiscovery
    {
        [Fact]
        public void Discovers_And_Invokes_GoodModule()
        {
            var services = new ServiceCollection();
            var count = ModuleLoader.InvokeFromAssembly(typeof(GoodModule).Assembly, services);

            Assert.True(count >= 1, "at least one module should be invoked");

            using var provider = services.BuildServiceProvider();
            var resolved = provider.GetService<ISampleGeometryService>();
            Assert.NotNull(resolved);
            Assert.Equal("geom", resolved.Hello());
        }

        [Fact]
        public void FaultyModule_DoesNotBreakStartup()
        {
            var services = new ServiceCollection();

            // Should not throw even though FaultyModule.ConfigureServices does.
            // GoodModule still runs successfully and registers its service.
            var ex = Record.Exception(() =>
                ModuleLoader.InvokeFromAssembly(typeof(FaultyModule).Assembly, services));
            Assert.Null(ex);

            using var provider = services.BuildServiceProvider();
            Assert.NotNull(provider.GetService<ISampleGeometryService>());
        }

        [Fact]
        public void Assembly_WithNoModules_ReturnsZero()
        {
            var services = new ServiceCollection();
            // mscorlib has no IBettaModule implementors.
            var count = ModuleLoader.InvokeFromAssembly(typeof(object).Assembly, services);
            Assert.Equal(0, count);
        }
    }
}
