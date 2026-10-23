// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Betta.Attributes;

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
            IsOverloaded(serviceType, method)
                ? SignatureGuid(serviceType, method, StableTypeName)
                : Md5($"{StableTypeName(serviceType)}|{method.Name}");

        /// <summary>
        /// The pre-0.8 scheme: MD5 over the whole signature with <see cref="Type.FullName"/>,
        /// which assembly-qualifies generic type arguments (<c>List`1[[System.Double,
        /// System.Private.CoreLib, Version=8.0.0.0, …]]</c>). It changed with every
        /// parameter change, and for generic signatures with every plugin, RhinoCommon
        /// or .NET version.
        /// </summary>
        public static Guid LegacyGuidFromMethod(Type serviceType, MethodInfo method) =>
            SignatureGuid(serviceType, method, t => t.FullName);

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
            string[] formerMethodNames, string[] formerTypeNames)
        {
            static IEnumerable<string> Clean(string[] names) =>
                (names ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim());

            var methodNames = Clean(formerMethodNames).ToList();
            var types = new[] { StableTypeName(serviceType) }.Concat(Clean(formerTypeNames));
            var names = new[] { method.Name }.Concat(methodNames.Where(n => !n.Contains('.'))).ToList();

            // A dotted former name is "Type.Method": its last dot becomes the hash's separator.
            var inputs = types.SelectMany(t => names.Select(m => $"{t}|{m}"))
                .Concat(methodNames.Where(n => n.Contains('.')).Select(n => { var dot = n.LastIndexOf('.'); return $"{n[..dot]}|{n[(dot + 1)..]}"; }));
            // The current Type|Method hash is the GUID itself, except on an overload, which hashes its signature.
            var current = $"{StableTypeName(serviceType)}|{method.Name}";
            var keepCurrent = methodNames.Contains(method.Name) && IsOverloaded(serviceType, method);
            return inputs.Where(i => keepCurrent || i != current).Distinct().Select(Md5).ToList();
        }

        /// <summary>
        /// <see cref="Type.FullName"/> without assembly qualification: a closed generic
        /// is written <c>List`1[System.Double]</c> instead of <c>List`1[[System.Double,
        /// System.Private.CoreLib, Version=…]]</c>. Equal to FullName for every type
        /// without generic arguments. Betta.Generators and Betta.Mcp build the same string.
        /// </summary>
        public static string StableTypeName(Type type)
        {
            if (type.IsArray)
            {
                var rank = type.GetArrayRank();
                return StableTypeName(type.GetElementType()) + (rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]");
            }
            if (type.IsByRef) return StableTypeName(type.GetElementType()) + "&";
            if (type.IsPointer) return StableTypeName(type.GetElementType()) + "*";
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
                return type.GetGenericTypeDefinition().FullName + "[" +
                       string.Join(",", type.GetGenericArguments().Select(StableTypeName)) + "]";
            return type.FullName ?? type.Name;
        }

        /// <summary>
        /// True when the service type has another public <c>[GrasshopperMethod]</c> with
        /// the same name (disabled ones count, so toggling Enabled changes no GUID).
        /// </summary>
        private static bool IsOverloaded(Type serviceType, MethodInfo method) =>
            serviceType.GetMethods().Count(m => m.Name == method.Name &&
                                                m.GetCustomAttribute<GrasshopperMethodAttribute>() != null) > 1;

        private static Guid SignatureGuid(Type serviceType, MethodInfo method, Func<Type, string> typeName)
        {
            var paramSig = string.Join(",",
                method.GetParameters().Select(p => typeName(p.ParameterType) + " " + p.Name));
            return Md5($"{serviceType.FullName}|{method.Name}|{paramSig}|{typeName(method.ReturnType)}");
        }

        private static Guid Md5(string input)
        {
            using var md5 = MD5.Create();
            return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

        public override string ToString()
        {
            return $"{ServiceType.Name}.{Method.Name} -> {Name}";
        }
    }
}
