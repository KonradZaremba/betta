// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Reflection;
using Betta.Attributes;
using Betta.Interfaces;
using Betta.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TestBetta
{
    // Fixture: class-direct collection — attributes on a concrete class, no
    // interface. This is the no-interface authoring style.
    [GrasshopperCollection("ClassDirect", "Demo")]
    public class DirectMaths : IBettaCollection
    {
        [GrasshopperMethod("Inc")]
        public int Increment(int x) => x + 1;
    }

    // Fixture: interface-based collection + its impl. Discovery must register
    // the method via the interface only — the impl class must NOT also produce
    // a duplicate component ("interface wins").
    [GrasshopperCollection("IfaceBased", "Demo")]
    public interface IIfaceMaths : IBettaCollection
    {
        [GrasshopperMethod("Dec")]
        int Decrement(int x);
    }

    public class IfaceMaths : IIfaceMaths
    {
        public int Decrement(int x) => x - 1;
    }

    public class TestClassDirectDiscovery
    {
        private static ComponentRegistry DiscoverThisAssembly()
        {
            var registry = new ComponentRegistry();
            registry.DiscoverFromAssembly(Assembly.GetExecutingAssembly());
            return registry;
        }

        [Fact]
        public void DiscoversClassDirectCollection()
        {
            var direct = DiscoverThisAssembly().GetDescriptors()
                .Where(d => d.ServiceType == typeof(DirectMaths))
                .ToList();

            Assert.Single(direct);
            Assert.Equal("Inc", direct[0].Name);
            Assert.Equal("ClassDirect", direct[0].Category);
            Assert.NotEqual(Guid.Empty, direct[0].Guid);
        }

        [Fact]
        public void InterfaceWins_ImplClassNotDoubleRegistered()
        {
            var descriptors = DiscoverThisAssembly().GetDescriptors();

            // "Dec" registered exactly once, via the interface — never via the
            // IfaceMaths impl class.
            Assert.Single(descriptors.Where(d => d.Name == "Dec"));
            Assert.Contains(descriptors, d => d.ServiceType == typeof(IIfaceMaths) && d.Name == "Dec");
            Assert.DoesNotContain(descriptors, d => d.ServiceType == typeof(IfaceMaths));
        }

        [Fact]
        public void AutoRegister_SelfBindsClassDirectService()
        {
            var registry = DiscoverThisAssembly();
            var services = new ServiceCollection();
            registry.AutoRegisterServices(services, Assembly.GetExecutingAssembly());

            var resolved = services.BuildServiceProvider().GetService(typeof(DirectMaths));

            Assert.NotNull(resolved);
            Assert.IsType<DirectMaths>(resolved);
        }
    }
}
