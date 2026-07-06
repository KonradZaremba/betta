// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Betta.Attributes;
using Betta.Services;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Locks the contract between the source generator and the runtime:
    ///   1. The OpaqueDemo plugin compiles with the generator attached.
    ///   2. The emitted <c>BettaPluginBootstrap.GetDescriptorBlueprints()</c>
    ///      returns one entry per <c>[GrasshopperMethod]</c>.
    ///   3. Each blueprint's <c>Guid</c> matches <c>ComponentDescriptor.GenerateGuidFromMethod</c>
    ///      for the same service type / method — so saved .gh documents survive
    ///      a switch between the reflection path and the generated path.
    /// </summary>
    public class TestGeneratorBootstrap
    {
        /// <summary>
        /// Locate the OpaqueDemo DLL the build copied next to TestBetta.dll.
        /// </summary>
        private static Assembly LoadOpaqueDemo()
        {
            var here = Path.GetDirectoryName(typeof(TestGeneratorBootstrap).Assembly.Location);
            // Walk up to the repo, then drop into the sample's bin folder.
            var candidates = new[]
            {
                Path.Combine(here ?? "", "Betta.OpaqueDemo.dll"),
                Path.GetFullPath(Path.Combine(here ?? "", "..", "..", "..", "..", "samples", "Betta.OpaqueDemo", "bin", "Debug", "net7.0-windows", "Betta.OpaqueDemo.dll")),
            };
            var path = candidates.FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException("Could not locate Betta.OpaqueDemo.dll. Build the OpaqueDemo sample first.");
            return Assembly.LoadFrom(path);
        }

        [Fact]
        public void BootstrapTypeIsEmitted()
        {
            var asm = LoadOpaqueDemo();
            var t = asm.GetType("Betta.Generated.BettaPluginBootstrap");
            Assert.NotNull(t);
            var get = t.GetMethod("GetDescriptorBlueprints", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(get);

            var raw = (IEnumerable)get.Invoke(null, null);
            Assert.NotNull(raw);
            var count = raw.Cast<object>().Count();
            Assert.True(count >= 6, $"Expected at least 6 blueprints, got {count}");
        }

        [Fact]
        public void BootstrapEmitsAllOpaqueDemoMethods()
        {
            var asm = LoadOpaqueDemo();
            var graphCollection = asm.GetType("Betta.OpaqueDemo.GraphCollection");
            Assert.NotNull(graphCollection);

            var expectedMethods = new[] { "Make", "Deconstruct", "Batch", "Label", "Scaled", "BatchFromTree" };
            var t = asm.GetType("Betta.Generated.BettaPluginBootstrap");
            var get = t.GetMethod("GetDescriptorBlueprints", BindingFlags.Public | BindingFlags.Static);
            var raw = (IEnumerable)get.Invoke(null, null);

            var methodNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bp in raw)
            {
                var name = bp.GetType().GetProperty("MethodName").GetValue(bp) as string;
                if (!string.IsNullOrEmpty(name)) methodNames.Add(name);
            }
            foreach (var expected in expectedMethods)
                Assert.True(methodNames.Contains(expected), $"Bootstrap missing method '{expected}'. Present: [{string.Join(", ", methodNames.OrderBy(x => x))}]");
        }

        [Fact]
        public void GeneratedGuidsMatchReflectionForSimpleSignatures()
        {
            // For methods whose signatures involve only non-generic types,
            // the generator's compile-time GUID matches reflection exactly.
            // (For closed generics over versioned runtime assemblies the
            //  values can diverge — see ConvertToDescriptors which recomputes
            //  the runtime GUID from the resolved MethodInfo.)
            var asm = LoadOpaqueDemo();
            var graphCollection = asm.GetType("Betta.OpaqueDemo.GraphCollection");

            var t = asm.GetType("Betta.Generated.BettaPluginBootstrap");
            var get = t.GetMethod("GetDescriptorBlueprints", BindingFlags.Public | BindingFlags.Static);
            var raw = (IEnumerable)get.Invoke(null, null);

            // Make / Label / Scaled have no generic parameters or return types
            // so their compile-time hash matches reflection 1:1.
            var simpleMethods = new HashSet<string>(new[] { "Make", "Label", "Scaled" });
            var checks = 0;
            foreach (var bp in raw)
            {
                var bpType = bp.GetType();
                var name = bpType.GetProperty("MethodName").GetValue(bp) as string;
                if (!simpleMethods.Contains(name ?? "")) continue;
                var generatedGuid = (Guid)bpType.GetProperty("Guid").GetValue(bp);
                var m = graphCollection.GetMethod(name);
                var reflectionGuid = ComponentDescriptor.GenerateGuidFromMethod(graphCollection, m);
                Assert.Equal(reflectionGuid, generatedGuid);
                checks++;
            }
            Assert.True(checks >= 3, $"Expected at least 3 simple-signature method checks, ran {checks}");
        }

        [Fact]
        public void ConvertToDescriptorsAlwaysUsesReflectionGuid()
        {
            // The fast path materialises descriptors whose Guid is recomputed
            // from the resolved MethodInfo, so save/reload compatibility is
            // identical to the legacy reflection scan even when the generator's
            // compile-time GUID drifts (closed generics over versioned ref asms).
            var asm = LoadOpaqueDemo();
            var descriptors = ComponentDescriptorBlueprint.TryLoadFromAssembly(asm);
            Assert.NotNull(descriptors);

            foreach (var d in descriptors)
            {
                var expected = ComponentDescriptor.GenerateGuidFromMethod(d.ServiceType, d.Method);
                Assert.Equal(expected, d.Guid);
            }
        }

        [Fact]
        public void TryLoadFromAssemblyMaterializesDescriptors()
        {
            var asm = LoadOpaqueDemo();
            var descriptors = ComponentDescriptorBlueprint.TryLoadFromAssembly(asm);

            Assert.NotNull(descriptors);
            Assert.True(descriptors.Count >= 6, $"Expected at least 6 descriptors, got {descriptors.Count}");

            // Every descriptor should round-trip to a usable Type + MethodInfo.
            foreach (var d in descriptors)
            {
                Assert.NotNull(d.ServiceType);
                Assert.NotNull(d.Method);
                Assert.NotEqual(Guid.Empty, d.Guid);
                Assert.False(string.IsNullOrEmpty(d.Name));
            }
        }

        [Fact]
        public void RegistryAddDescriptorsAcceptsBootstrapOutput()
        {
            var asm = LoadOpaqueDemo();
            var descriptors = ComponentDescriptorBlueprint.TryLoadFromAssembly(asm);
            Assert.NotNull(descriptors);

            var registry = new ComponentRegistry();
            var added = registry.AddDescriptors(descriptors);
            Assert.Equal(descriptors.Count, added.Count);

            // Idempotent — a second pass adds nothing.
            var addedAgain = registry.AddDescriptors(descriptors);
            Assert.Empty(addedAgain);
        }
    }
}
