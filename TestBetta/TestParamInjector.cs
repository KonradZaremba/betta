// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Betta.Attributes;
using Betta.Interfaces;
using Betta.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TestBetta
{
    public class TestParamInjector
    {
        [Fact]
        public void TestComponentDescriptorGeneration()
        {
            var methods = typeof(ISimpleService).GetMethods()
                .Where(m => m.GetCustomAttribute<GrasshopperMethodAttribute>() != null);

            Assert.True(methods.Any(), "Should find methods with GrasshopperMethod attributes");

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<GrasshopperMethodAttribute>();
                Assert.NotNull(attr);
                Assert.False(string.IsNullOrEmpty(attr.Name));
            }
        }

        [Fact]
        public void TestGuidGeneration()
        {
            var method = typeof(ISimpleService).GetMethod("GetValue2");
            Assert.NotNull(method);

            var guid1 = ComponentDescriptor.GenerateGuidFromMethod(typeof(ISimpleService), method);
            var guid2 = ComponentDescriptor.GenerateGuidFromMethod(typeof(ISimpleService), method);

            Assert.Equal(guid1, guid2);
        }

        [Fact]
        public void TestGuidMatchesHandComputedMD5()
        {
            // Locks the hash format: if someone "refactors" the signature string
            // the existing .gh documents would silently break.
            var method = typeof(ISimpleService).GetMethod("GetValue2");
            var expectedInput =
                $"{typeof(ISimpleService).FullName}|GetValue2|System.Double value21212|System.Double";
            using var md5 = MD5.Create();
            var expected = new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(expectedInput)));

            var actual = ComponentDescriptor.GenerateGuidFromMethod(typeof(ISimpleService), method);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TestServiceRegistration()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<ISimpleService, SimpleService>();

            var provider = serviceCollection.BuildServiceProvider();
            var service = provider.GetService<ISimpleService>();

            Assert.NotNull(service);
            Assert.IsType<SimpleService>(service);
        }

        [Fact]
        public void TestComponentRegistryDiscovery()
        {
            var registry = new ComponentRegistry();
            registry.DiscoverFromAssembly(Assembly.GetAssembly(typeof(ISimpleService)));

            var descriptors = registry.GetDescriptors();
            Assert.True(descriptors.Count > 0, "Should discover at least one component");

            var descriptor = descriptors.First();
            Assert.False(string.IsNullOrEmpty(descriptor.Name));
            Assert.NotEqual(Guid.Empty, descriptor.Guid);
        }

        [Fact]
        public void TestAutoRegisterServicesBindsInterfaceToImpl()
        {
            var registry = new ComponentRegistry();
            registry.DiscoverFromAssembly(Assembly.GetAssembly(typeof(ISimpleService)));

            var services = new ServiceCollection();
            registry.AutoRegisterServices(services, Assembly.GetAssembly(typeof(ISimpleService)));

            var provider = services.BuildServiceProvider();
            var resolved = provider.GetService<ISimpleService>();

            Assert.NotNull(resolved);
            Assert.IsType<SimpleService>(resolved);
        }
    }
}
