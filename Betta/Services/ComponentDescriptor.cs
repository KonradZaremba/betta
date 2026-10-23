// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Betta.Attributes;
using Betta.Guids;

namespace Betta.Services
{
    /// <summary>
    /// Describes a component that should be generated from a service method
    /// </summary>
    public class ComponentDescriptor
    {
        public Type ServiceType { get; set; }
        public MethodInfo Method { get; set; }
        public string Name { get; set; }
        public string NickName { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public Guid Guid { get; set; }
        public bool Enabled { get; set; } = true;
        public string IconResource { get; set; }
        internal System.Drawing.Bitmap CachedIcon { get; set; }

        /// <summary>
        /// True if the method's return type (or list element / tuple element)
        /// implements <c>Betta.Preview.IBettaPreview</c>. Set during
        /// discovery — when true, the BettaComponent runtime caches its last
        /// outputs and forwards IGH_PreviewData calls to them. Detected by
        /// interface name string so the Betta runtime stays free of a hard
        /// reference to the Betta.Preview package.
        /// </summary>
        public bool HasPreview { get; set; }

        /// <summary>
        /// True if the method's return type (or list element / tuple element)
        /// implements <c>Betta.Preview.IBettaBakeable</c>. When true, the
        /// component implements IGH_BakeAwareObject and forwards right-click
        /// → Bake to the cached values.
        /// </summary>
        public bool HasBakeable { get; set; }

        /// <summary>
        /// Entitlement key required to run this component, or null if none.
        /// Populated from <c>[GrasshopperRequiresEntitlement]</c> on the
        /// method (or its declaring class). Enforced at solve time by
        /// consulting the DI-registered <c>IBettaLicenseGate</c>; null means
        /// no entitlement check runs.
        /// </summary>
        public string RequiredEntitlement { get; set; }

        /// <summary>
        /// True if the method has one or more <c>[GrasshopperTrigger]</c>
        /// parameters. The BettaComponent surfaces a "Run" menu item and
        /// skips its solve until the user clicks it. When false, the
        /// component runs on every input change as usual.
        /// </summary>
        public bool HasTrigger { get; set; }

        /// <summary>
        /// The GUID the component was published under before 0.8, when it differs
        /// from <see cref="Guid"/>. Registered as a hidden alias so .gh files saved
        /// with that scheme still open.
        /// </summary>
        public Guid? LegacyGuid { get; set; }

        /// <summary>
        /// GUIDs the component had under the earlier method and type names its
        /// <c>FormerNames</c> list (<see cref="FormerGuidsFromAttributes"/>).
        /// </summary>
        public IReadOnlyList<Guid> FormerGuids { get; set; } = Array.Empty<Guid>();

        /// <summary>
        /// Every other GUID a saved .gh file may hold for this component: <see cref="LegacyGuid"/>
        /// and <see cref="FormerGuids"/>. Each is registered as a hidden alias proxy.
        /// </summary>
        public IEnumerable<Guid> AliasGuids =>
            (LegacyGuid is Guid legacy ? FormerGuids.Prepend(legacy) : FormerGuids).Where(g => g != Guid).Distinct();

        /// <summary>
        /// Deterministic GUID from the service type and method name: MD5 over
        /// <c>Type|Method</c>. Parameters and the return type are left out, so adding,
        /// renaming or retyping inputs and outputs keeps saved .gh files working, as a
        /// hand-written GH_Component's fixed ComponentGuid does. Display attributes
        /// (Name, Category) are left out too. When the type has more than one
        /// <c>[GrasshopperMethod]</c> with this name, the overloads are told apart by
        /// their signature (<c>Type|Method|paramType name,...|ReturnType</c> with
        /// <see cref="StableTypeName"/>).
        /// </summary>
        public static Guid GenerateGuidFromMethod(Type serviceType, MethodInfo method) =>
            GuidScheme.MethodGuid(serviceType, method, GuidScheme.IsOverloaded(serviceType, method, IsComponent));

        /// <summary>
        /// The pre-0.8 scheme: MD5 over the whole signature with <see cref="Type.FullName"/>,
        /// which assembly-qualifies generic type arguments (<c>List`1[[System.Double,
        /// System.Private.CoreLib, Version=8.0.0.0, …]]</c>). It changed with every
        /// parameter change, and for generic signatures with every plugin, RhinoCommon
        /// or .NET version.
        /// </summary>
        public static Guid LegacyGuidFromMethod(Type serviceType, MethodInfo method) =>
            GuidScheme.LegacyGuid(serviceType, method);

        /// <summary>
        /// The GUID a method is published under: <paramref name="pinnedGuid"/> when set
        /// (<c>[GrasshopperMethod(Guid = …)]</c>, e.g. kept from a ported GH_Component),
        /// otherwise the derived one, with the pre-0.8 GUID in <paramref name="legacyGuid"/>
        /// when it differs.
        /// </summary>
        public static Guid ResolveGuid(Type serviceType, MethodInfo method, string pinnedGuid, out Guid? legacyGuid)
        {
            legacyGuid = null;
            if (!string.IsNullOrEmpty(pinnedGuid)) return Guid.Parse(pinnedGuid);

            var guid = GenerateGuidFromMethod(serviceType, method);
            var legacy = LegacyGuidFromMethod(serviceType, method);
            if (legacy != guid) legacyGuid = legacy;
            return guid;
        }

        /// <summary>
        /// The GUIDs <see cref="GenerateGuidFromMethod"/> gave the method under the names in
        /// <c>[GrasshopperMethod(FormerNames = …)]</c> (earlier method names, or
        /// <c>"Namespace.Type.Method"</c> for a method moved from another type) and
        /// <c>[GrasshopperCollection(FormerNames = …)]</c> (earlier full type names).
        /// An overload that lists its own name gets back the name-only GUID it had
        /// before the overload was added.
        /// </summary>
        public static IReadOnlyList<Guid> FormerGuidsFromAttributes(Type serviceType, MethodInfo method) =>
            ComputeFormerGuids(serviceType, method,
                method.GetCustomAttribute<GrasshopperMethodAttribute>()?.FormerNames,
                serviceType.GetCustomAttribute<GrasshopperCollectionAttribute>()?.FormerNames);

        public static IReadOnlyList<Guid> ComputeFormerGuids(Type serviceType, MethodInfo method,
            string[] formerMethodNames, string[] formerTypeNames) =>
            GuidScheme.FormerInputs(GuidScheme.StableTypeName(serviceType), method.Name, GuidScheme.IsOverloaded(serviceType, method, IsComponent),
                formerMethodNames, formerTypeNames).Select(GuidScheme.Md5).ToList();

        /// <summary>
        /// <see cref="Type.FullName"/> without assembly qualification: a closed generic
        /// is written <c>List`1[System.Double]</c> instead of <c>List`1[[System.Double,
        /// System.Private.CoreLib, Version=…]]</c>. Equal to FullName for every type
        /// without generic arguments. Betta.Generators and Betta.Mcp build the same string.
        /// </summary>
        public static string StableTypeName(Type type) => GuidScheme.StableTypeName(type);

        private static bool IsComponent(MethodInfo m) => m.GetCustomAttribute<GrasshopperMethodAttribute>() != null;

        public override string ToString()
        {
            return $"{ServiceType.Name}.{Method.Name} -> {Name}";
        }
    }
}
