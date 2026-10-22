// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Betta.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TestBetta
{
    // A plugin service as a hot-loaded plugin brings it: an interface and its implementation,
    // with a constructor dependency the startup container does provide.
    public interface ILateGreeter { string Greet(string name); }

    public class LateGreeter : ILateGreeter
    {
        private readonly LateGreeting _greeting;
        public LateGreeter(LateGreeting greeting) => _greeting = greeting;
        public string Greet(string name) => _greeting.Prefix + name;
    }

    public class LateGreeting { public string Prefix { get; set; } = "Hi "; }

    // Needs a service only its plugin's IBettaModule would have registered.
    public interface ILateNeedsModule { }

    public class LateNeedsModule : ILateNeedsModule
    {
        public LateNeedsModule(ModuleOnlyService service) { }
    }

    public class ModuleOnlyService { }

    /// <summary>
    /// The ServiceProvider is built once at startup, so a plugin dropped into the Betta folder
    /// while Rhino runs got "Service not registered in DI container" on every component.
    /// LateServices creates its implementation instead. Real DI container, GH-free.
    /// </summary>
    public class TestLateServices
    {
        private static IServiceProvider Provider(Action<IServiceCollection> configure = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new LateGreeting());
            configure?.Invoke(services);
            return services.BuildServiceProvider();
        }

        [Fact]
        public void A_registered_service_comes_from_the_container()
        {
            var registered = new LateGreeter(new LateGreeting { Prefix = "Registered " });
            var provider = Provider(s => s.AddSingleton<ILateGreeter>(registered));

            Assert.Same(registered, LateServices.Resolve(provider, typeof(ILateGreeter)));
        }

        [Fact]
        public void A_hot_loaded_service_is_created_once_with_constructor_injection()
        {
            var provider = Provider();

            var first = Assert.IsAssignableFrom<ILateGreeter>(LateServices.Resolve(provider, typeof(ILateGreeter)));
            Assert.Equal("Hi Ada", first.Greet("Ada"));
            Assert.Same(first, LateServices.Resolve(provider, typeof(ILateGreeter)));
        }

        [Fact]
        public void A_service_needing_its_module_is_reported_not_thrown()
        {
            Exception reported = null;

            var service = LateServices.Resolve(Provider(), typeof(ILateNeedsModule), ex => reported = ex);

            Assert.Null(service);
            Assert.IsType<InvalidOperationException>(reported);
        }

        [Fact]
        public void A_replaced_plugin_gets_a_fresh_instance()
        {
            var provider = Provider();
            var before = LateServices.Resolve(provider, typeof(ILateGreeter));

            Assert.True(LateServices.UnregisterAssembly(typeof(ILateGreeter).Assembly) >= 1);
            Assert.NotSame(before, LateServices.Resolve(provider, typeof(ILateGreeter)));
        }
    }
}
