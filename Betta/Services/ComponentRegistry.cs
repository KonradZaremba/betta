// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Betta.Attributes;
using Betta.Components;
using Betta.Interfaces;
using Grasshopper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Betta.Services
{
    /// <summary>
    /// Discovers attributed service methods, auto-registers their implementations
    /// in DI, and publishes one GH proxy per method so the zero-touch pipeline
    /// produces real Grasshopper components with no manual wiring.
    /// </summary>
    public class ComponentRegistry
    {
        private readonly List<ComponentDescriptor> _descriptors = new();
        private readonly HashSet<Guid> _published = new();

        /// <summary>
        /// Injected after DI is built (see Startup.PriorityLoad). Defaults to a
        /// NullLogger so DiscoverFromAssembly / AutoRegisterServices can run
        /// before the service provider exists without NREs.
        /// </summary>
        public ILogger<ComponentRegistry> Logger { get; set; } = NullLogger<ComponentRegistry>.Instance;

        /// <summary>
        /// Scan an assembly for interfaces that (a) inherit IBettaCollection
        /// and (b) carry [GrasshopperMethod] attributes on their methods. Every
        /// matching method produces a ComponentDescriptor appended to the
        /// internal list. Returns the newly added descriptors so a caller can
        /// register just-loaded plugins without re-publishing older ones.
        /// </summary>
        public IReadOnlyList<ComponentDescriptor> DiscoverFromAssembly(Assembly assembly)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            // Qualifying interfaces — marked with IBettaCollection and carrying
            // [GrasshopperMethod]. The contract-first authoring style.
            var interfaceTypes = types
                .Where(t => t != null && t.IsInterface &&
                            typeof(IBettaCollection).IsAssignableFrom(t) &&
                            t != typeof(IBettaCollection) &&
                            HasGrasshopperMethods(t))
                .ToList();

            // Qualifying classes — concrete, marked with IBettaCollection, and
            // carrying [GrasshopperMethod] on their own methods. The terse,
            // no-interface style (Dynamo ZeroTouch-like): one type, no contract
            // duplication. A class that implements one of the scanned
            // interfaces above is skipped so the same method isn't registered
            // twice — the interface wins. (Attributes on an interface method
            // don't propagate to the implementing class method, so a class that
            // only *implements* a marked interface naturally has no GH methods
            // of its own and is excluded here regardless.)
            var classTypes = types
                .Where(t => t != null && t.IsClass && !t.IsAbstract &&
                            typeof(IBettaCollection).IsAssignableFrom(t) &&
                            HasGrasshopperMethods(t) &&
                            !t.GetInterfaces().Any(interfaceTypes.Contains))
                .ToList();

            var serviceTypes = interfaceTypes.Concat(classTypes);

            var added = new List<ComponentDescriptor>();
            foreach (var serviceType in serviceTypes)
            {
                var collectionAttr = serviceType.GetCustomAttribute<GrasshopperCollectionAttribute>();
                var defaultCategory = collectionAttr?.Category ?? TrimInterfacePrefix(serviceType.Name);
                var defaultSubCategory = collectionAttr?.SubCategory ?? "General";

                foreach (var method in serviceType.GetMethods())
                {
                    var attr = method.GetCustomAttribute<GrasshopperMethodAttribute>();
                    if (attr == null || !attr.Enabled) continue;

                    var name = attr.Name ?? method.Name;
                    var descriptor = new ComponentDescriptor
                    {
                        ServiceType = serviceType,
                        Method = method,
                        Name = name,
                        NickName = attr.NickName ?? name,
                        Description = attr.Description ?? name,
                        Category = attr.Category ?? defaultCategory,
                        SubCategory = attr.SubCategory ?? defaultSubCategory,
                        Guid = string.IsNullOrEmpty(attr.Guid)
                            ? ComponentDescriptor.GenerateGuidFromMethod(serviceType, method)
                            : Guid.Parse(attr.Guid),
                        Enabled = attr.Enabled,
                        IconResource = attr.IconResource
                    };

                    _descriptors.Add(descriptor);
                    added.Add(descriptor);
                }
            }
            return added;
        }

        private static string TrimInterfacePrefix(string typeName)
        {
            if (!string.IsNullOrEmpty(typeName) && typeName.Length > 1 &&
                typeName[0] == 'I' && char.IsUpper(typeName[1]))
                return typeName.Substring(1);
            return typeName;
        }

        /// <summary>
        /// For every discovered service type, bind it to a concrete
        /// implementation in DI if the caller has not already done so. For an
        /// interface service type this is the first non-abstract implementer in
        /// the assembly; for a class service type (no-interface style) the
        /// class is its own implementation — a class is assignable from itself,
        /// so it self-registers.
        /// </summary>
        public void AutoRegisterServices(IServiceCollection services, Assembly assembly)
        {
            var serviceTypes = _descriptors.Select(d => d.ServiceType).Distinct();

            foreach (var serviceType in serviceTypes)
            {
                if (services.Any(s => s.ServiceType == serviceType)) continue;

                var impl = assembly.GetTypes()
                    .FirstOrDefault(t => !t.IsAbstract && !t.IsInterface && serviceType.IsAssignableFrom(t));

                if (impl != null)
                    services.AddSingleton(serviceType, impl);
            }
        }

        /// <summary>
        /// Publish one GH proxy per descriptor so Grasshopper exposes them in
        /// the toolbar. Must be called from GH_AssemblyPriority.PriorityLoad()
        /// after Instances.ComponentServer is available.
        /// </summary>
        public void RegisterWithGrasshopper()
        {
            Logger.LogInformation("Registering {Count} components", _descriptors.Count);
            foreach (var descriptor in _descriptors)
                RegisterDescriptor(descriptor);
        }

        /// <summary>
        /// Publish a single descriptor as a GH proxy. Idempotent: a descriptor
        /// with a Guid that has already been published is silently skipped,
        /// so runtime plugin loads can safely re-emit descriptors they already
        /// saw on startup.
        /// </summary>
        public void RegisterDescriptor(ComponentDescriptor descriptor)
        {
            if (!_published.Add(descriptor.Guid)) return;

            try
            {
                DescriptorCache.Add(descriptor);
                Instances.ComponentServer.AddProxy(new BettaComponentProxy(descriptor));
                Logger.LogInformation("  + {Category}/{SubCategory}/{Name} ({Guid})",
                    descriptor.Category, descriptor.SubCategory, descriptor.Name, descriptor.Guid);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to register {Descriptor}", descriptor);
                _published.Remove(descriptor.Guid);
            }
        }

        public IReadOnlyList<ComponentDescriptor> GetDescriptors() => _descriptors.AsReadOnly();

        private static bool HasGrasshopperMethods(Type type) =>
            type.GetMethods().Any(m => m.GetCustomAttribute<GrasshopperMethodAttribute>() != null);
    }
}
