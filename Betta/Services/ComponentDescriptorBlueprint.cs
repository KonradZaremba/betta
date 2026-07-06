// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Betta.Services
{
    /// <summary>
    /// Compile-time-friendly, GH-free DTO that carries everything a
    /// <see cref="ComponentDescriptor"/> needs except the live
    /// <see cref="Type"/> / <see cref="MethodInfo"/> handles. The Betta source
    /// generator emits a local twin of this DTO into each plugin assembly
    /// (<c>Betta.Generated.BettaGeneratedBlueprint</c>) so the plugin does not
    /// need to take a hard reference on <c>Betta.gha</c>. The runtime reads
    /// the generated blueprints via property reflection and materialises them
    /// into the strong-typed shape below.
    ///
    /// The blueprint embeds the GUID as a compile-time literal so plugin
    /// authors keep their existing save/reload compatibility: the generated
    /// path and the reflection path produce identical GUIDs (same MD5 over
    /// the same signature string).
    /// </summary>
    public sealed class ComponentDescriptorBlueprint
    {
        /// <summary>Reflection-style <c>Type.FullName</c> of the declaring service type.</summary>
        public string ServiceTypeName { get; set; }
        public string MethodName { get; set; }

        /// <summary>
        /// Parameter type FullNames in declaration order. Used to disambiguate
        /// overloaded methods when resolving the <see cref="MethodInfo"/>.
        /// </summary>
        public string[] ParameterTypeNames { get; set; }

        public string Name { get; set; }
        public string NickName { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public Guid Guid { get; set; }
        public bool Enabled { get; set; } = true;
        public string IconResource { get; set; }
        public bool HasPreview { get; set; }
        public bool HasBakeable { get; set; }

        /// <summary>
        /// Look for <c>Betta.Generated.BettaPluginBootstrap.GetDescriptorBlueprints()</c>
        /// on <paramref name="assembly"/>. If present, invoke it, materialise
        /// each generated blueprint via property reflection, resolve them
        /// against the assembly's types, and return real descriptors. If the
        /// type or method isn't present, returns <c>null</c> — callers should
        /// then fall back to the reflection scan.
        /// </summary>
        public static IReadOnlyList<ComponentDescriptor> TryLoadFromAssembly(Assembly assembly)
        {
            if (assembly == null) return null;
            var bootstrap = assembly.GetType("Betta.Generated.BettaPluginBootstrap", throwOnError: false);
            if (bootstrap == null) return null;
            var get = bootstrap.GetMethod("GetDescriptorBlueprints", BindingFlags.Public | BindingFlags.Static);
            if (get == null) return null;

            object raw;
            try { raw = get.Invoke(null, null); }
            catch (TargetInvocationException) { return null; }

            if (raw is not IEnumerable rawList) return null;

            var hydrated = new List<ComponentDescriptorBlueprint>();
            foreach (var item in rawList)
            {
                var bp = Hydrate(item);
                if (bp != null) hydrated.Add(bp);
            }
            return ConvertToDescriptors(hydrated, assembly);
        }

        /// <summary>
        /// Resolve every blueprint against <paramref name="assembly"/>: look up
        /// the service <see cref="Type"/> once per type (cached) and the
        /// matching <see cref="MethodInfo"/> by name + parameter signature.
        /// Returns the materialised descriptors in input order. Blueprints
        /// that cannot be resolved (renamed / removed types, mismatched
        /// signatures) are silently skipped — the reflection fallback in
        /// <see cref="ComponentRegistry.DiscoverFromAssembly"/> would have
        /// missed them too. That keeps the fast path strictly an optimisation:
        /// the worst it can do is publish a strict subset of what reflection
        /// would have published, never the wrong components.
        /// </summary>
        public static IReadOnlyList<ComponentDescriptor> ConvertToDescriptors(
            IReadOnlyList<ComponentDescriptorBlueprint> blueprints,
            Assembly assembly)
        {
            if (blueprints == null || blueprints.Count == 0)
                return Array.Empty<ComponentDescriptor>();
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            // Resolve service types once per unique name — much cheaper than
            // re-scanning the assembly for every method.
            Type[] allTypes;
            try { allTypes = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(t => t != null).ToArray(); }
            var byFullName = allTypes
                .Where(t => t.FullName != null)
                .GroupBy(t => t.FullName)
                .ToDictionary(g => g.Key, g => g.First());

            var serviceTypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
            var descriptors = new List<ComponentDescriptor>(blueprints.Count);

            foreach (var bp in blueprints)
            {
                if (bp == null || string.IsNullOrEmpty(bp.ServiceTypeName) || string.IsNullOrEmpty(bp.MethodName))
                    continue;

                if (!serviceTypeCache.TryGetValue(bp.ServiceTypeName, out var svcType))
                {
                    byFullName.TryGetValue(bp.ServiceTypeName, out svcType);
                    serviceTypeCache[bp.ServiceTypeName] = svcType;
                }
                if (svcType == null) continue;

                var method = ResolveMethod(svcType, bp.MethodName, bp.ParameterTypeNames);
                if (method == null) continue;

                // Always recompute the GUID from the resolved MethodInfo. The
                // blueprint's GUID is a compile-time best-effort: when a
                // method's signature includes closed generics over a versioned
                // runtime assembly (e.g. RhinoCommon at the user's installed
                // version, not the NuGet ref-asm version), Type.FullName at
                // runtime emits a different assembly identity than the
                // generator can see at build time. Recomputing here keeps
                // save/reload compatibility identical to the legacy reflection
                // path — the blueprint's value is in skipping the attribute
                // scan and DescriptorPlanning, not in shaving the MD5 hash.
                var runtimeGuid = ComponentDescriptor.GenerateGuidFromMethod(svcType, method);

                descriptors.Add(new ComponentDescriptor
                {
                    ServiceType = svcType,
                    Method = method,
                    Name = bp.Name,
                    NickName = bp.NickName,
                    Description = bp.Description,
                    Category = bp.Category,
                    SubCategory = bp.SubCategory,
                    Guid = runtimeGuid,
                    Enabled = bp.Enabled,
                    IconResource = bp.IconResource,
                    HasPreview = bp.HasPreview,
                    HasBakeable = bp.HasBakeable,
                });
            }
            return descriptors;
        }

        private static MethodInfo ResolveMethod(Type type, string methodName, string[] paramTypeNames)
        {
            paramTypeNames ??= Array.Empty<string>();

            // Filter by name + arity first to keep the inner comparison cheap.
            var candidates = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == methodName && m.GetParameters().Length == paramTypeNames.Length)
                .ToList();

            if (candidates.Count == 1) return candidates[0];

            foreach (var m in candidates)
            {
                var ps = m.GetParameters();
                bool match = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    var pt = ps[i].ParameterType.FullName;
                    if (pt != paramTypeNames[i])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return m;
            }
            return null;
        }

        /// <summary>
        /// Read the generated DTO via property reflection. The plugin's local
        /// blueprint type and this one share property names by contract.
        /// </summary>
        private static ComponentDescriptorBlueprint Hydrate(object generated)
        {
            if (generated == null) return null;
            var t = generated.GetType();

            string S(string prop) => t.GetProperty(prop)?.GetValue(generated) as string;
            bool B(string prop, bool fallback) => t.GetProperty(prop)?.GetValue(generated) is bool b ? b : fallback;
            Guid G(string prop) => t.GetProperty(prop)?.GetValue(generated) is Guid g ? g : Guid.Empty;
            string[] SA(string prop)
            {
                var raw = t.GetProperty(prop)?.GetValue(generated);
                if (raw is string[] sa) return sa;
                if (raw is IEnumerable<string> es) return es.ToArray();
                if (raw is IEnumerable e) return e.Cast<object>().Select(x => x?.ToString()).ToArray();
                return Array.Empty<string>();
            }

            return new ComponentDescriptorBlueprint
            {
                ServiceTypeName = S("ServiceTypeName"),
                MethodName = S("MethodName"),
                ParameterTypeNames = SA("ParameterTypeNames"),
                Name = S("Name"),
                NickName = S("NickName"),
                Description = S("Description"),
                Category = S("Category"),
                SubCategory = S("SubCategory"),
                Guid = G("Guid"),
                Enabled = B("Enabled", true),
                IconResource = S("IconResource"),
                HasPreview = B("HasPreview", false),
                HasBakeable = B("HasBakeable", false),
            };
        }
    }
}
