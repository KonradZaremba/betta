// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Betta.Attributes;
using Betta.Interfaces;
using Betta.Services;
using Xunit;

namespace TestBetta.SchemeProbe
{
    // Probe types: their names feed the GUIDs, so never rename them.
    [GrasshopperCollection("Probe", "Plain")]
    public interface IPlainProbe : IBettaCollection
    {
        [GrasshopperMethod("Probe Add")]
        double Add(double a, double b);
    }

    [GrasshopperCollection("Probe", "Overloads")]
    public interface IOverloadProbe : IBettaCollection
    {
        [GrasshopperMethod("Probe Scale Number")]
        double Scale(double x, double factor);

        [GrasshopperMethod("Probe Scale List")]
        List<double> Scale(List<double> xs, double factor);

        [GrasshopperMethod("Probe Fold Array")]
        double Fold(double[] values);

        [GrasshopperMethod("Probe Fold Ref")]
        double Fold(ref double value);
    }

    public static class ProbeHost
    {
        [GrasshopperCollection("Probe", "Nested")]
        public interface INestedProbe : IBettaCollection
        {
            [GrasshopperMethod("Probe Nested Add")]
            double Add(double a);
        }
    }

    [GrasshopperCollection("Probe", "Former")]
    public interface IFormerProbe : IBettaCollection
    {
        [GrasshopperMethod("Probe Renamed", FormerNames = new[] { "OldName" })]
        double Renamed(double x);

        [GrasshopperMethod("Probe Moved", FormerNames = new[] { "Old.Ns.OldType.OldMethod" })]
        double Moved(double x);

        // Was the only Mix until the list overload was added.
        [GrasshopperMethod("Probe Mix Number", FormerNames = new[] { "Mix" })]
        double Mix(double x);

        [GrasshopperMethod("Probe Mix List")]
        List<double> Mix(List<double> xs);
    }

    [GrasshopperCollection("Probe", "Pack", FormerNames = new[] { "Old.Ns.OldPack" })]
    public interface IPackProbe : IBettaCollection
    {
        [GrasshopperMethod("Probe Packed", FormerNames = new[] { "OldPacked" })]
        double Packed(double x);
    }
}

namespace TestBetta
{
    using TestBetta.SchemeProbe;

    /// <summary>
    /// Component GUIDs pinned as literals: any change here breaks saved .gh files. The values
    /// were captured from the runtime as it stood before the GUID code was consolidated, and
    /// must survive that refactor unchanged. GH-free.
    /// </summary>
    public class TestGuidScheme
    {
        private static MethodInfo M(Type type, string name, params Type[] parameters) =>
            type.GetMethod(name, parameters);

        private static void Check(Type type, MethodInfo method, string generated, string legacy, params string[] former)
        {
            Assert.NotNull(method);
            Assert.Equal(Guid.Parse(generated), ComponentDescriptor.GenerateGuidFromMethod(type, method));
            Assert.Equal(Guid.Parse(legacy), ComponentDescriptor.LegacyGuidFromMethod(type, method));
            Assert.Equal(former.Select(Guid.Parse).ToArray(), ComponentDescriptor.FormerGuidsFromAttributes(type, method).ToArray());
        }

        [Theory]
        [InlineData(typeof(double), "System.Double")]
        [InlineData(typeof(double[]), "System.Double[]")]
        [InlineData(typeof(List<double>), "System.Collections.Generic.List`1[System.Double]")]
        [InlineData(typeof(ProbeHost.INestedProbe), "TestBetta.SchemeProbe.ProbeHost+INestedProbe")]
        public void Stable_type_names_are_pinned(Type type, string expected)
        {
            Assert.Equal(expected, ComponentDescriptor.StableTypeName(type));
        }

        [Fact]
        public void Ref_parameters_get_an_ampersand()
        {
            Assert.Equal("System.Double&", ComponentDescriptor.StableTypeName(typeof(double).MakeByRefType()));
        }

        [Fact]
        public void A_plain_method_hashes_type_and_name()
        {
            var t = typeof(IPlainProbe);
            Check(t, M(t, "Add", typeof(double), typeof(double)),
                "0695a938-5a73-7f73-1f33-8859af3fe7cd", "06348039-e183-fcd3-8b4c-924dcfcad8e5");
        }

        [Fact]
        public void Overloads_hash_their_signature()
        {
            var t = typeof(IOverloadProbe);
            Check(t, M(t, "Scale", typeof(double), typeof(double)),
                "5b3422df-a3ce-2620-d1e4-fd9de7e34156", "5b3422df-a3ce-2620-d1e4-fd9de7e34156");
            Check(t, M(t, "Scale", typeof(List<double>), typeof(double)),
                "7bb12fc0-9955-dc59-953f-eb20a2109c2c", "e64dead6-af8a-1b1c-dd1c-c6aca9bf4d7d");
            Check(t, M(t, "Fold", typeof(double[])),
                "359c9e1c-5e16-d832-c1c1-d59d8235472e", "359c9e1c-5e16-d832-c1c1-d59d8235472e");
            Check(t, M(t, "Fold", typeof(double).MakeByRefType()),
                "940b3277-475e-ed49-e25c-37f4df2546fb", "940b3277-475e-ed49-e25c-37f4df2546fb");
        }

        [Fact]
        public void A_nested_type_keeps_the_plus_in_its_name()
        {
            var t = typeof(ProbeHost.INestedProbe);
            Check(t, M(t, "Add", typeof(double)),
                "10c30842-f43c-c2d8-9f78-a6abd579dcbf", "57368dd1-c6fe-9f1c-64f5-b9091728e25a");
        }

        [Fact]
        public void Former_names_give_the_guids_of_the_old_names()
        {
            var t = typeof(IFormerProbe);
            Check(t, M(t, "Renamed", typeof(double)),
                "bb3bedd7-3509-983f-cd08-6f96afbb2530", "83c04634-9a6e-f381-d622-5cc978749c6a",
                "21d13f8e-8623-d4c3-acc0-a16233715ad0");
            Check(t, M(t, "Moved", typeof(double)),
                "80dd4bcf-5f87-b409-7f4a-b50ff561bf1a", "c8b3df1f-97fa-f4e7-d26b-41caf0d312a2",
                "ee2e5eb4-def6-9898-0963-648d59c70f21");
        }

        [Fact]
        public void An_overload_listing_its_own_name_gets_back_the_name_only_guid()
        {
            var t = typeof(IFormerProbe);
            Check(t, M(t, "Mix", typeof(double)),
                "765e8365-089d-4ca5-1237-fd6cca3510b4", "765e8365-089d-4ca5-1237-fd6cca3510b4",
                "55128dcc-588e-0006-b1db-2232b3f2178e");
            Check(t, M(t, "Mix", typeof(List<double>)),
                "6d966d26-c7b0-79c6-e1de-f0072ce05a93", "784345d0-0177-03d3-e50d-cff3682708aa");
        }

        [Fact]
        public void Former_type_names_cross_with_current_and_former_method_names()
        {
            var t = typeof(IPackProbe);
            Check(t, M(t, "Packed", typeof(double)),
                "8e3d05b4-50d0-d431-2c02-64f9d477ab0f", "4706ba02-78b8-e025-f9c3-cb65bbe3b3e7",
                "650e0e58-cfe3-d2d5-9e46-e98b6638923e", "ab90457b-ec76-328e-2f39-adf61eb85863",
                "5db420d1-98cb-23c3-f174-314049a286e5");
        }
    }
}
