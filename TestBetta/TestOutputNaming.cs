// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Betta;
using Betta.Attributes;
using Xunit;

namespace TestBetta
{
    // Fixtures for output-naming. These don't need Grasshopper — OutputPlanner
    // is deliberately GH-free, so the naming logic is unit-testable headlessly.
    public interface IOutputSamples
    {
        double Plain(double x);

        [return: GrasshopperOutput("Result", "R", "The remapped value")]
        double Named(double x);

        (double Sum, double Average) NamedTuple(List<double> n);

        (double, double) UnnamedTuple(List<double> n);

        [GrasshopperOutput("Lo", "L", "Lower", Index = 0)]
        [GrasshopperOutput("Hi", "H", "Upper", Index = 1)]
        (double Min, double Max) OverriddenTuple(List<double> n);

        [GrasshopperOutput("FlatCount", Index = 1)]
        (List<bool> Flat, int FlatCount, string CSVPath) MultiWordTuple(List<double> n);

        List<int> Listy(int count);

        Stats AsClass(List<double> n);

        System.Drawing.Bitmap Picture(int size);
    }

    public class Stats
    {
        public double Mean { get; set; }
        public double Count { get; set; }
        public double TotalArea { get; set; }
    }

    public class TestOutputNaming
    {
        private static List<OutputPlan> Plan(string method) =>
            OutputPlanner.PlanOutputs(
                typeof(IOutputSamples).GetMethod(method).ReturnType,
                typeof(IOutputSamples).GetMethod(method));

        [Fact]
        public void SingleOutput_DefaultsToOutput()
        {
            var plans = Plan(nameof(IOutputSamples.Plain));
            Assert.Single(plans);
            Assert.Equal("Output", plans[0].Name);
            Assert.False(plans[0].IsList);
        }

        [Fact]
        public void SingleOutput_HonorsReturnAttribute()
        {
            var plans = Plan(nameof(IOutputSamples.Named));
            Assert.Single(plans);
            Assert.Equal("Result", plans[0].Name);
            Assert.Equal("R", plans[0].NickName);
            Assert.Equal("The remapped value", plans[0].Description);
        }

        [Fact]
        public void NamedTuple_UsesElementNames()
        {
            var plans = Plan(nameof(IOutputSamples.NamedTuple));
            Assert.Equal(2, plans.Count);
            Assert.Equal("Sum", plans[0].Name);
            Assert.Equal("Average", plans[1].Name);
        }

        [Fact]
        public void UnnamedTuple_FallsBackToOutputN()
        {
            var plans = Plan(nameof(IOutputSamples.UnnamedTuple));
            Assert.Equal(2, plans.Count);
            Assert.Equal("Output1", plans[0].Name);
            Assert.Equal("Output2", plans[1].Name);
        }

        [Fact]
        public void Tuple_AttributeOverridesElementName()
        {
            var plans = Plan(nameof(IOutputSamples.OverriddenTuple));
            Assert.Equal(2, plans.Count);
            // [GrasshopperOutput] by Index wins over the (Min, Max) element names.
            Assert.Equal("Lo", plans[0].Name);
            Assert.Equal("Hi", plans[1].Name);
        }

        // With no description given, an output's tooltip is its own display name.
        [Fact]
        public void Description_single_output_defaults_to_its_name()
        {
            var plans = Plan(nameof(IOutputSamples.Plain));
            Assert.Equal("Output", plans[0].Description);
        }

        [Fact]
        public void Description_list_output_defaults_to_its_name()
        {
            var plans = Plan(nameof(IOutputSamples.Listy));
            Assert.Equal("Output", plans[0].Description);
        }

        [Fact]
        public void Description_unnamed_tuple_elements_default_to_their_names()
        {
            var plans = Plan(nameof(IOutputSamples.UnnamedTuple));
            Assert.Equal(new[] { "Output1", "Output2" }, plans.Select(p => p.Description));
        }

        [Fact]
        public void Description_named_tuple_elements_default_to_their_names()
        {
            var plans = Plan(nameof(IOutputSamples.MultiWordTuple));
            Assert.Equal(new[] { "Flat", "FlatCount", "CSV Path" }, plans.Select(p => p.Description));
        }

        [Fact]
        public void Description_class_property_defaults_to_split_words_name()
        {
            var plans = Plan(nameof(IOutputSamples.AsClass));
            Assert.Equal(new[] { "Mean", "Count", "Total Area" }, plans.Select(p => p.Description));
        }

        [Fact]
        public void Description_bitmap_output_defaults_to_its_name()
        {
            var plans = Plan(nameof(IOutputSamples.Picture));
            Assert.Equal("Image", plans[0].Description);
        }

        [Fact]
        public void Description_explicit_attribute_wins()
        {
            Assert.Equal("The remapped value", Plan(nameof(IOutputSamples.Named))[0].Description);
            Assert.Equal(new[] { "Lower", "Upper" },
                Plan(nameof(IOutputSamples.OverriddenTuple)).Select(p => p.Description));
        }

        [Fact]
        public void ListReturn_IsSingleListOutput()
        {
            var plans = Plan(nameof(IOutputSamples.Listy));
            Assert.Single(plans);
            Assert.True(plans[0].IsList);
        }

        [Fact]
        public void ClassReturn_FansOutByProperty()
        {
            var plans = Plan(nameof(IOutputSamples.AsClass));
            Assert.Equal(3, plans.Count);
            Assert.Contains(plans, p => p.Name == "Mean");
            Assert.Contains(plans, p => p.Name == "Count");
            Assert.Contains(plans, p => p.Name == "Total Area" && p.NickName == "Total Area");
        }

        [Fact]
        public void Multi_word_tuple_names_get_spaces_unless_an_attribute_names_them()
        {
            var plans = Plan(nameof(IOutputSamples.MultiWordTuple));
            Assert.Equal(new[] { "Flat", "FlatCount", "CSV Path" }, plans.Select(p => p.Name));
            Assert.Equal("CSV Path", plans[2].NickName);
        }

        [Theory]
        [InlineData("FlatCount", "Flat Count")]
        [InlineData("SheetCount", "Sheet Count")]
        [InlineData("CSVPath", "CSV Path")]
        [InlineData("UCount", "U Count")]
        [InlineData("PanelIDs", "Panel IDs")]
        [InlineData("IDs", "IDs")]
        [InlineData("CSV", "CSV")]
        [InlineData("Point3d", "Point3d")]
        [InlineData("totalArea", "Total Area")]
        [InlineData("flat_count", "Flat count")]
        [InlineData("Sum", "Sum")]
        public void Identifiers_split_into_words(string identifier, string expected)
        {
            Assert.Equal(expected, OutputPlanner.SplitWords(identifier));
        }
    }
}
