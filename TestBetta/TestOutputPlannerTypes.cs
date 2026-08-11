// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Betta;
using Xunit;

namespace TestBetta
{
    // Return-type fixtures for OutputPlanner. GH-free by design, so these run
    // headlessly with no Rhino present.
    public interface IReturnTypeSamples
    {
        Bitmap MakeBitmap(int width);
        Image MakeImage(int width);
        List<Bitmap> MakeBitmaps(int count);

        // 8 and 9 elements: past 7, C# nests the remainder in TRest, so these
        // lock the recursive walk that flattens them.
        (int, int, int, int, int, int, int) SevenTuple();
        (int, int, int, int, int, int, int, int) EightTuple();
        (int, int, int, int, int, int, int, int, int) NineTuple();
        (double A, double B, double C, double D, double E, double F, double G, double H, double I) NineNamedTuple();

        // Mixed element types across the TRest boundary.
        (string S1, int I2, double D3, bool B4, string S5, int I6, double D7, bool B8, string S9) NineMixedTuple();

        // A class with an opaque property: the class still explodes per-property,
        // but the opaque property must stay a single typed output rather than
        // exploding further into its own properties.
        GraphReport MakeReport();
        PlainReport MakePlainReport();
    }

    public class GraphReport
    {
        public string Title { get; set; }
        public OpaqueGraph Graph { get; set; }
        public List<OpaqueGraph> Related { get; set; }
    }

    public class PlainReport
    {
        public string Title { get; set; }
        public ExplodableGraph Graph { get; set; }
    }

    public class TestOutputPlannerTypes
    {
        private static List<OutputPlan> Plan(string method)
        {
            var m = typeof(IReturnTypeSamples).GetMethod(method);
            return OutputPlanner.PlanOutputs(m.ReturnType, m);
        }

        [Theory]
        [InlineData(nameof(IReturnTypeSamples.MakeBitmap))]
        [InlineData(nameof(IReturnTypeSamples.MakeImage))]
        public void BitmapAndImage_StayASingleOutput(string method)
        {
            // Regression: Bitmap used to explode into Width/Height/Palette/... —
            // it is a container type, not a data record.
            var plans = Plan(method);

            Assert.Single(plans);
            Assert.False(plans[0].IsList);
            Assert.DoesNotContain(plans, p => p.Name == "Width" || p.Name == "Height");
        }

        [Fact]
        public void ListOfBitmap_IsASingleListOutput()
        {
            var plans = Plan(nameof(IReturnTypeSamples.MakeBitmaps));

            Assert.Single(plans);
            Assert.True(plans[0].IsList);
        }

        [Theory]
        [InlineData(nameof(IReturnTypeSamples.SevenTuple), 7)]
        [InlineData(nameof(IReturnTypeSamples.EightTuple), 8)]
        [InlineData(nameof(IReturnTypeSamples.NineTuple), 9)]
        public void Tuples_FlattenPastTheSevenElementTRestBoundary(string method, int expected)
        {
            // Elements 8+ live in TRest (ValueTuple<T1..T7, TRest>); without the
            // recursive walk the 8th output would surface as a nested tuple.
            var plans = Plan(method);

            Assert.Equal(expected, plans.Count);
            Assert.All(plans, p => Assert.Equal(typeof(int), p.Type));
        }

        [Fact]
        public void HighArityTuple_KeepsElementNamesAcrossTRest()
        {
            var plans = Plan(nameof(IReturnTypeSamples.NineNamedTuple));

            Assert.Equal(9, plans.Count);
            Assert.Equal(
                new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I" },
                plans.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void HighArityTuple_KeepsElementTypesInDeclarationOrder()
        {
            var plans = Plan(nameof(IReturnTypeSamples.NineMixedTuple));

            Assert.Equal(
                new[]
                {
                    typeof(string), typeof(int), typeof(double), typeof(bool),
                    typeof(string), typeof(int), typeof(double), typeof(bool),
                    typeof(string),
                },
                plans.Select(p => p.Type).ToArray());
        }

        [Fact]
        public void OpaqueProperty_BecomesOneTypedOutput()
        {
            // GraphReport explodes into its properties, but the opaque ones must
            // not explode further — Graph stays OpaqueGraph, Related stays a list.
            var plans = Plan(nameof(IReturnTypeSamples.MakeReport));

            var graph = Assert.Single(plans, p => p.Name == nameof(GraphReport.Graph));
            Assert.Equal(typeof(OpaqueGraph), graph.Type);
            Assert.False(graph.IsList);

            // A list output carries its declared type (List<T>) with IsList set —
            // matching how a top-level List<T> return is planned.
            var related = Assert.Single(plans, p => p.Name == nameof(GraphReport.Related));
            Assert.Equal(typeof(List<OpaqueGraph>), related.Type);
            Assert.True(related.IsList);

            // The opaque property's own members must never leak out as outputs.
            Assert.DoesNotContain(plans, p => p.Name == nameof(OpaqueGraph.NodeCount));
        }

        [Fact]
        public void NonOpaqueProperty_StillExplodes()
        {
            // The counterpart to the above: without an opt-in, a nested plain
            // class is flattened as before. Guards against over-collapsing.
            var plans = Plan(nameof(IReturnTypeSamples.MakePlainReport));

            Assert.DoesNotContain(plans, p => p.Type == typeof(ExplodableGraph));
        }
    }
}
