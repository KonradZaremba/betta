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
using System.Threading.Tasks;
using Betta.Attributes;
using Betta.Interfaces;
using Betta.Services;
using Xunit;

namespace TestBetta
{
    // Fixture: plain, generic, pinned and overloaded methods.
    [GrasshopperCollection("StableGuids", "Demo")]
    public class StableGuidSamples : IBettaCollection
    {
        public const string PinnedGuid = "4f0c3a8e-1d2b-4c5e-9a7f-0b1c2d3e4f50";

        [GrasshopperMethod("Plain Remap")]
        public double Remap(double value, double sourceMin) => value - sourceMin;

        [GrasshopperMethod("Batch Remap")]
        public List<double> Batch(List<double> values) => values;

        [GrasshopperMethod("Async Pair")]
        public Task<(double Sum, double Mean)> Pair(double[] values) => Task.FromResult((0.0, 0.0));

        [GrasshopperMethod("Ported", Guid = PinnedGuid)]
        public List<double> Ported(List<double> values) => values;

        // Was the only Scale (published as Type|Scale) until the list overload was added.
        [GrasshopperMethod("Scale Number", FormerNames = new[] { "Scale" })]
        public double Scale(double x, double factor) => x * factor;

        [GrasshopperMethod("Scale List")]
        public List<double> Scale(List<double> xs, double factor) => xs;

        [GrasshopperMethod("Map Range", FormerNames = new[] { "Remap2", "Other.Plugin.IOldPack.Remap" })]
        public double MapRange(double value) => value;
    }

    // A renamed type: its components keep answering to the GUIDs they had under the old name.
    [GrasshopperCollection("StableGuids", "Moved", FormerNames = new[] { "Old.Plugin.MathPack" })]
    public class MovedSamples : IBettaCollection
    {
        [GrasshopperMethod("Moved Clamp", FormerNames = new[] { "Limit" })]
        public double Clamp(double value) => value;
    }

    /// <summary>
    /// Derived component GUIDs depend only on the service type and method name, like a
    /// hand-written GH_Component's fixed ComponentGuid: parameters and outputs can change
    /// without breaking saved .gh files, and no assembly version leaks in (Type.FullName
    /// assembly-qualifies generic type arguments, which made pre-0.8 List/tuple/Task
    /// signatures change GUID with every plugin, RhinoCommon or .NET version). GH-free.
    /// </summary>
    public class TestStableGuids
    {
        private static MethodInfo M(string name) => typeof(StableGuidSamples).GetMethod(name);

        private static MethodInfo Scale(Type first) =>
            typeof(StableGuidSamples).GetMethods().Single(m => m.Name == "Scale" && m.GetParameters()[0].ParameterType == first);

        private static Guid Md5(string input) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes(input)));

        [Theory]
        [InlineData(typeof(double), "System.Double")]
        [InlineData(typeof(double[]), "System.Double[]")]
        [InlineData(typeof(double[,]), "System.Double[,]")]
        [InlineData(typeof(List<double>), "System.Collections.Generic.List`1[System.Double]")]
        [InlineData(typeof(List<double>[]), "System.Collections.Generic.List`1[System.Double][]")]
        [InlineData(typeof(Dictionary<string, List<int>>),
            "System.Collections.Generic.Dictionary`2[System.String,System.Collections.Generic.List`1[System.Int32]]")]
        [InlineData(typeof((double, double)), "System.ValueTuple`2[System.Double,System.Double]")]
        [InlineData(typeof(Task<(double, double)>),
            "System.Threading.Tasks.Task`1[System.ValueTuple`2[System.Double,System.Double]]")]
        public void Stable_type_name_has_no_assembly_versions(Type type, string expected)
        {
            Assert.Equal(expected, ComponentDescriptor.StableTypeName(type));
        }

        [Theory]
        [InlineData("Remap")]
        [InlineData("Batch")]
        [InlineData("Pair")]
        public void Guid_hashes_the_type_and_method_name_only(string method)
        {
            Assert.Equal(Md5($"{typeof(StableGuidSamples).FullName}|{method}"),
                         ComponentDescriptor.GenerateGuidFromMethod(typeof(StableGuidSamples), M(method)));
        }

        [Fact]
        public void Overloads_are_told_apart_by_their_signature()
        {
            var type = typeof(StableGuidSamples).FullName;
            var number = ComponentDescriptor.GenerateGuidFromMethod(typeof(StableGuidSamples), Scale(typeof(double)));
            var list = ComponentDescriptor.GenerateGuidFromMethod(typeof(StableGuidSamples), Scale(typeof(List<double>)));

            Assert.Equal(Md5($"{type}|Scale|System.Double x,System.Double factor|System.Double"), number);
            Assert.Equal(Md5($"{type}|Scale|System.Collections.Generic.List`1[System.Double] xs,System.Double factor|" +
                             "System.Collections.Generic.List`1[System.Double]"), list);
        }

        [Fact]
        public void Legacy_guid_is_the_old_signature_hash()
        {
            var type = typeof(StableGuidSamples).FullName;
            Assert.Equal(Md5($"{type}|Remap|System.Double value,System.Double sourceMin|System.Double"),
                         ComponentDescriptor.LegacyGuidFromMethod(typeof(StableGuidSamples), M("Remap")));

            // For generics the old hash carried assembly versions.
            Assert.Contains("Version=", typeof(List<double>).FullName);
            Assert.Equal(Md5($"{type}|Batch|{typeof(List<double>).FullName} values|{typeof(List<double>).FullName}"),
                         ComponentDescriptor.LegacyGuidFromMethod(typeof(StableGuidSamples), M("Batch")));
        }

        [Fact]
        public void Discovery_records_a_legacy_guid_only_where_it_differs()
        {
            var registry = new ComponentRegistry();
            registry.DiscoverFromAssembly(typeof(StableGuidSamples).Assembly);
            var byName = registry.GetDescriptors()
                .Where(d => d.ServiceType == typeof(StableGuidSamples))
                .ToDictionary(d => d.Name);

            Assert.Equal(ComponentDescriptor.LegacyGuidFromMethod(typeof(StableGuidSamples), M("Remap")),
                         byName["Plain Remap"].LegacyGuid);
            Assert.Equal(ComponentDescriptor.LegacyGuidFromMethod(typeof(StableGuidSamples), M("Batch")),
                         byName["Batch Remap"].LegacyGuid);
            Assert.NotNull(byName["Async Pair"].LegacyGuid);
            Assert.Equal(Guid.Parse(StableGuidSamples.PinnedGuid), byName["Ported"].Guid);
            Assert.Null(byName["Ported"].LegacyGuid);
            // A non-generic overload already hashed its signature: same GUID as before, no alias.
            Assert.Null(byName["Scale Number"].LegacyGuid);
            Assert.NotNull(byName["Scale List"].LegacyGuid);
        }

        [Fact]
        public void Former_method_names_give_alias_guids()
        {
            var type = typeof(StableGuidSamples).FullName;
            var former = ComponentDescriptor.FormerGuidsFromAttributes(typeof(StableGuidSamples), M("MapRange"));

            // The GUIDs the method had as Remap2 on this type, and as Remap on the type it moved from.
            Assert.Equal(new[] { Md5($"{type}|Remap2"), Md5("Other.Plugin.IOldPack|Remap") }, former);
        }

        [Fact]
        public void An_overload_listing_its_own_name_answers_to_the_guid_from_before_the_overload()
        {
            var type = typeof(StableGuidSamples).FullName;

            Assert.Equal(new[] { Md5($"{type}|Scale") },
                         ComponentDescriptor.FormerGuidsFromAttributes(typeof(StableGuidSamples), Scale(typeof(double))));
            Assert.Empty(ComponentDescriptor.FormerGuidsFromAttributes(typeof(StableGuidSamples), Scale(typeof(List<double>))));
            // Without an overload, the method's own name is its current GUID, not an alias.
            Assert.Empty(ComponentDescriptor.ComputeFormerGuids(typeof(StableGuidSamples), M("Remap"), new[] { "Remap" }, null));

            var registry = new ComponentRegistry();
            registry.DiscoverFromAssembly(typeof(StableGuidSamples).Assembly);
            var number = registry.GetDescriptors().Single(d => d.Name == "Scale Number");
            Assert.Contains(Md5($"{type}|Scale"), number.AliasGuids);
        }

        [Fact]
        public void Former_type_names_combine_with_current_and_former_method_names()
        {
            var type = typeof(MovedSamples).FullName;
            var former = ComponentDescriptor.FormerGuidsFromAttributes(typeof(MovedSamples), typeof(MovedSamples).GetMethod("Clamp"));

            Assert.Equal(new[] { Md5($"{type}|Limit"), Md5("Old.Plugin.MathPack|Clamp"), Md5("Old.Plugin.MathPack|Limit") },
                         former.OrderBy(g => g == Md5($"{type}|Limit") ? 0 : g == Md5("Old.Plugin.MathPack|Clamp") ? 1 : 2));
        }

        [Fact]
        public void Discovery_registers_former_names_and_the_legacy_guid_as_aliases()
        {
            var registry = new ComponentRegistry();
            registry.DiscoverFromAssembly(typeof(StableGuidSamples).Assembly);
            var mapRange = registry.GetDescriptors().Single(d => d.Name == "Map Range");

            Assert.Equal(Md5($"{typeof(StableGuidSamples).FullName}|MapRange"), mapRange.Guid);
            Assert.Equal(new[] { mapRange.LegacyGuid.Value }.Concat(mapRange.FormerGuids), mapRange.AliasGuids);
            Assert.Equal(3, mapRange.AliasGuids.Count());
            Assert.DoesNotContain(mapRange.Guid, mapRange.AliasGuids);
        }

        [Fact]
        public void Generator_blueprints_honour_a_pinned_guid()
        {
            // The source-generator fast path used to recompute every GUID from the signature,
            // dropping [GrasshopperMethod(Guid = ...)]: the attribute that keeps a ported
            // GH_Component library's saved files working.
            var blueprints = new[]
            {
                new ComponentDescriptorBlueprint
                {
                    ServiceTypeName = typeof(StableGuidSamples).FullName,
                    MethodName = "Ported",
                    ParameterTypeNames = new[] { "System.Collections.Generic.List`1[System.Double]" },
                    Name = "Ported", Category = "StableGuids", SubCategory = "Demo",
                    Guid = Guid.NewGuid(),
                    Enabled = true,
                },
                new ComponentDescriptorBlueprint
                {
                    ServiceTypeName = typeof(StableGuidSamples).FullName,
                    MethodName = "Batch",
                    ParameterTypeNames = new[] { "System.Collections.Generic.List`1[System.Double]" },
                    Name = "Batch Remap", Category = "StableGuids", SubCategory = "Demo",
                    Guid = Guid.NewGuid(),
                    Enabled = true,
                },
            };

            var descriptors = ComponentDescriptorBlueprint.ConvertToDescriptors(blueprints, typeof(StableGuidSamples).Assembly);

            Assert.Equal(Guid.Parse(StableGuidSamples.PinnedGuid), descriptors[0].Guid);
            Assert.Equal(ComponentDescriptor.GenerateGuidFromMethod(typeof(StableGuidSamples), M("Batch")), descriptors[1].Guid);
            Assert.NotNull(descriptors[1].LegacyGuid);
        }
    }
}
