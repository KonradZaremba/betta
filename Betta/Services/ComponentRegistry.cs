// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
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

            // Track GUIDs already in the registry so a re-scan (e.g. a plugin
            // assembly that was already populated via the generator's bootstrap
            // fast path) doesn't append duplicate descriptors.
            var seen = new HashSet<Guid>(_descriptors.Select(d => d.Guid));
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
                    // Entitlement: method-level attribute wins; otherwise the
                    // service-type's attribute cascades to every method on the
                    // class. Null when neither is present ⇒ no gate at solve.
                    var entitlementAttr = method.GetCustomAttribute<GrasshopperRequiresEntitlementAttribute>()
                                          ?? serviceType.GetCustomAttribute<GrasshopperRequiresEntitlementAttribute>();
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
                        IconResource = attr.IconResource,
                        HasPreview = ReturnSurfacesInterface(method, PreviewInterfaceFullName),
                        HasBakeable = ReturnSurfacesInterface(method, BakeableInterfaceFullName),
                        RequiredEntitlement = entitlementAttr?.Entitlement,
                        HasTrigger = method.GetParameters().Any(p =>
                            p.GetCustomAttribute<GrasshopperTriggerAttribute>() != null),
                    };

                    if (!seen.Add(descriptor.Guid)) continue;

                    _descriptors.Add(descriptor);
                    added.Add(descriptor);

                    RegisterOpaqueTypesFor(method);
                }
            }
            return added;
        }

        /// <summary>
        /// True if the method's return type (or, transitively, its list
        /// element / tuple element types) implements the named interface.
        /// The interface is matched by FullName string so this assembly does
        /// not take a hard reference to Betta.Preview — plugins that don't
        /// need preview/bake don't pay the transitive RhinoCommon cost.
        /// </summary>
        private static bool ReturnSurfacesInterface(MethodInfo method, string interfaceFullName)
        {
            var returnType = UnwrapTask(method.ReturnType);
            return ContainsInterface(returnType, interfaceFullName);
        }

        private static bool ContainsInterface(Type t, string interfaceFullName)
        {
            if (t == null) return false;

            var element = UnwrapList(t);
            if (element != null && element != t)
                return ContainsInterface(element, interfaceFullName);

            if (ImplementsByName(t, interfaceFullName)) return true;

            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition().FullName;
                if (def != null && (def.StartsWith("System.Tuple") || def.StartsWith("System.ValueTuple")))
                {
                    foreach (var arg in t.GetGenericArguments())
                        if (ContainsInterface(arg, interfaceFullName)) return true;
                }
            }
            return false;
        }

        private const string PreviewInterfaceFullName = "Betta.Preview.IBettaPreview";
        private const string BakeableInterfaceFullName = "Betta.Preview.IBettaBakeable";

        private static bool ImplementsByName(Type t, string interfaceFullName)
        {
            if (t == null) return false;
            if (t.FullName == interfaceFullName) return true;
            foreach (var iface in t.GetInterfaces())
                if (iface.FullName == interfaceFullName) return true;
            return false;
        }

        /// <summary>
        /// Push a pre-built batch of descriptors (typically materialised by
        /// the source generator via
        /// <see cref="ComponentDescriptorBlueprint.ConvertToDescriptors"/>)
        /// into the registry, running the same opaque-type registration walk
        /// that <see cref="DiscoverFromAssembly"/> performs. Skips descriptors
        /// without a usable <see cref="ComponentDescriptor.Method"/> handle so
        /// callers don't have to pre-filter. Returns the descriptors that were
        /// actually added — duplicates (matched by GUID against prior content)
        /// are dropped.
        /// </summary>
        public IReadOnlyList<ComponentDescriptor> AddDescriptors(IEnumerable<ComponentDescriptor> descriptors)
        {
            if (descriptors == null) return Array.Empty<ComponentDescriptor>();

            var existing = new HashSet<Guid>(_descriptors.Select(d => d.Guid));
            var added = new List<ComponentDescriptor>();
            foreach (var d in descriptors)
            {
                if (d == null || d.Method == null) continue;
                if (!existing.Add(d.Guid)) continue;

                _descriptors.Add(d);
                added.Add(d);
                RegisterOpaqueTypesFor(d.Method);
            }
            return added;
        }

        /// <summary>
        /// Walk a method's input parameter types and return type — including
        /// the element type of <c>List&lt;T&gt;</c> and tuple element types —
        /// and call <see cref="ParamRegistry.RegisterOpaque(Type)"/> for any
        /// type declared opaque (via [GrasshopperOpaque] on the type, the
        /// interface, the method, or the return value, or via the IBettaValue
        /// marker). Idempotent. Guarantees the typed Param_BettaGoo&lt;T&gt; +
        /// GH_BettaGoo&lt;T&gt; pair exists before AddProxy needs it.
        /// </summary>
        private static void RegisterOpaqueTypesFor(MethodInfo method)
        {
            var methodReturnOpaque = OpaqueDetection.IsMethodReturnOpaque(method);

            foreach (var p in method.GetParameters())
                Consider(p.ParameterType, opaqueOverride: false);

            var returnType = UnwrapTask(method.ReturnType);
            Consider(returnType, opaqueOverride: methodReturnOpaque);
        }

        private static void Consider(Type t, bool opaqueOverride)
        {
            if (t == null) return;

            // Strip List<T>/IEnumerable<T> wrappers — opaqueness applies to the
            // element type.
            var element = UnwrapList(t);
            if (element != null && element != t)
                t = element;

            if (OpaqueDetection.IsTypeOpaque(t) || opaqueOverride)
                ParamRegistry.RegisterOpaque(t);

            // Tuple element types — each can independently be opaque.
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition().FullName;
                if (def != null && (def.StartsWith("System.Tuple") || def.StartsWith("System.ValueTuple")))
                {
                    foreach (var arg in t.GetGenericArguments())
                        if (OpaqueDetection.IsTypeOpaque(arg))
                            ParamRegistry.RegisterOpaque(arg);
                }
            }
        }

        private static Type UnwrapTask(Type t)
        {
            if (t == null || !t.IsGenericType) return t;
            var gt = t.GetGenericTypeDefinition();
            if (gt == typeof(System.Threading.Tasks.Task<>) ||
                gt == typeof(System.Threading.Tasks.ValueTask<>))
                return t.GetGenericArguments()[0];
            return t;
        }

        private static Type UnwrapList(Type t)
        {
            if (t == null) return null;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return t.GetGenericArguments()[0];
            foreach (var iface in t.GetInterfaces())
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
                    t != typeof(string))
                    return iface.GetGenericArguments()[0];
            return null;
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

            // A single GetTypes() call — cached — so a ReflectionTypeLoadException
            // (one type in the plugin references a missing dep) does NOT abort
            // the whole registration pass. We take whatever types loaded
            // successfully and register from that subset. Without this guard, a
            // partially-broken plugin loses its DI wiring even though its
            // descriptors already made it into the registry, so components
            // appear on the toolbar but fail to solve with a misleading
            // "Service not registered" error.
            Type[] candidates;
            try { candidates = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                candidates = ex.Types.Where(t => t != null).ToArray();
                Logger.LogWarning(ex,
                    "AutoRegisterServices({Asm}): partial type load — {Loaded} of {Total} types resolved. Loader exceptions: {Errors}",
                    assembly.GetName().Name, candidates.Length, ex.Types.Length,
                    string.Join("; ", ex.LoaderExceptions.Where(le => le != null).Select(le => le.Message).Take(3)));
            }

            foreach (var serviceType in serviceTypes)
            {
                if (services.Any(s => s.ServiceType == serviceType)) continue;

                var impl = candidates
                    .FirstOrDefault(t => t != null && !t.IsAbstract && !t.IsInterface && serviceType.IsAssignableFrom(t));

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
        ///
        /// If a license gate is registered and denies the descriptor's owning
        /// assembly, registration is skipped entirely (the component doesn't
        /// appear on the toolbar). This is the coarse "kill switch"; per-
        /// method entitlement checks run later in <c>BettaComponent.SolveInstance</c>
        /// so gated components can still appear in the toolbar and warn on
        /// solve.
        /// </summary>
        public void RegisterDescriptor(ComponentDescriptor descriptor)
        {
            if (!_published.Add(descriptor.Guid)) return;

            var gate = Startup.LicenseGate;
            if (gate != null &&
                descriptor.ServiceType?.Assembly != null &&
                !gate.IsAssemblyAllowed(descriptor.ServiceType.Assembly))
            {
                Logger.LogInformation("Skipped by license gate: {Category}/{SubCategory}/{Name} ({Asm})",
                    descriptor.Category, descriptor.SubCategory, descriptor.Name,
                    descriptor.ServiceType.Assembly.GetName().Name);
                _published.Remove(descriptor.Guid);
                return;
            }

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

        /// <summary>
        /// Drop every descriptor sourced from <paramref name="asm"/>: remove
        /// from the internal list, drop from the published set, purge
        /// <see cref="DescriptorCache"/>, and ask
        /// <c>Grasshopper.Instances.ComponentServer</c> to evict the matching
        /// proxy by GUID. Returns the count of removed descriptors.
        ///
        /// GH 8 has no public <c>RemoveProxy</c>; instead the proxy catalog
        /// (<c>_proxies: SortedList&lt;Guid,IGH_ObjectProxy&gt;</c>) is mutated
        /// via reflection. If the field is missing on the running Grasshopper
        /// build, the removal is best-effort: we log and continue so the
        /// runtime stays consistent with the internal bookkeeping.
        /// </summary>
        public int UnregisterAssembly(Assembly asm)
        {
            if (asm == null) return 0;

            var doomed = _descriptors.Where(d => d.ServiceType?.Assembly == asm).ToList();
            if (doomed.Count == 0) return 0;

            var proxiesField = typeof(Grasshopper.Kernel.GH_ComponentServer)
                .GetField("_proxies", BindingFlags.NonPublic | BindingFlags.Instance);
            var proxyCatalog = proxiesField?.GetValue(Instances.ComponentServer) as IDictionary;

            foreach (var d in doomed)
            {
                _descriptors.Remove(d);
                _published.Remove(d.Guid);

                try
                {
                    proxyCatalog?.Remove(d.Guid);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not evict proxy {Guid} from GH catalog", d.Guid);
                }
            }

            DescriptorCache.RemoveByAssembly(asm);

            Logger.LogInformation("Unregistered {Count} descriptors from {Asm}",
                doomed.Count, asm.GetName().Name);

            return doomed.Count;
        }

        private static bool HasGrasshopperMethods(Type type) =>
            type.GetMethods().Any(m => m.GetCustomAttribute<GrasshopperMethodAttribute>() != null);
    }
}
