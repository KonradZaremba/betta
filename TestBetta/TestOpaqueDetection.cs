// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Betta;
using Betta.Attributes;
using Betta.Interfaces;
using Xunit;

namespace TestBetta
{
    // Domain types for opaque-detection scenarios. Plain class still explodes
    // into per-property outputs; opt-in via attribute or marker collapses to a
    // single typed output. All GH-free.
    public class ExplodableGraph
    {
        public int NodeCount { get; set; }
        public int EdgeCount { get; set; }
    }

    [GrasshopperOpaque]
    public class OpaqueGraph
    {
        public int NodeCount { get; set; }
        public int EdgeCount { get; set; }
    }

    public class MarkedGraph : IBettaValue
    {
        public int NodeCount { get; set; }
        public int EdgeCount { get; set; }
    }

    public interface IOpaqueSamples
    {
        ExplodableGraph Plain();
        OpaqueGraph ByAttribute();
        MarkedGraph ByMarker();

        [return: GrasshopperOpaque]
        ExplodableGraph ByReturnOverride();

        List<OpaqueGraph> BatchByAttribute();
    }

    public class TestOpaqueDetection
    {
        private static List<OutputPlan> Plan(string method)
        {
            var m = typeof(IOpaqueSamples).GetMethod(method);
            return OutputPlanner.PlanOutputs(m.ReturnType, m);
        }

        [Fact]
        public void PlainClass_ExplodesIntoProperties()
        {
            var plans = Plan(nameof(IOpaqueSamples.Plain));
            Assert.Equal(2, plans.Count);
            Assert.Contains(plans, p => p.Name == "NodeCount");
            Assert.Contains(plans, p => p.Name == "EdgeCount");
        }

        [Fact]
        public void TypeLevelAttribute_CollapsesToSingleOutput()
        {
            var plans = Plan(nameof(IOpaqueSamples.ByAttribute));
            Assert.Single(plans);
            Assert.Equal(typeof(OpaqueGraph), plans[0].Type);
            Assert.False(plans[0].IsList);
        }

        [Fact]
        public void MarkerInterface_CollapsesToSingleOutput()
        {
            var plans = Plan(nameof(IOpaqueSamples.ByMarker));
            Assert.Single(plans);
            Assert.Equal(typeof(MarkedGraph), plans[0].Type);
        }

        [Fact]
        public void ReturnAttribute_CollapsesEvenWhenTypeIsNotOpaque()
        {
            var plans = Plan(nameof(IOpaqueSamples.ByReturnOverride));
            Assert.Single(plans);
            Assert.Equal(typeof(ExplodableGraph), plans[0].Type);
        }

        [Fact]
        public void ListOfOpaque_StaysASingleListOutput()
        {
            var plans = Plan(nameof(IOpaqueSamples.BatchByAttribute));
            Assert.Single(plans);
            Assert.True(plans[0].IsList);
            // The plan carries List<T>; ParamVector strips the IList wrapper
            // and resolves to the registered Param_BettaGoo<T> via ParamRegistry.
            Assert.True(plans[0].Type.IsGenericType);
            Assert.Equal(typeof(OpaqueGraph), plans[0].Type.GetGenericArguments()[0]);
        }

        [Fact]
        public void TypeOpaqueDetection_HelpersAgreeWithAttributeAndMarker()
        {
            Assert.False(OpaqueDetection.IsTypeOpaque(typeof(ExplodableGraph)));
            Assert.True(OpaqueDetection.IsTypeOpaque(typeof(OpaqueGraph)));
            Assert.True(OpaqueDetection.IsTypeOpaque(typeof(MarkedGraph)));
        }
    }
}
