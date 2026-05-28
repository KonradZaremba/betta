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

        List<int> Listy(int count);

        Stats AsClass(List<double> n);
    }

    public class Stats
    {
        public double Mean { get; set; }
        public double Count { get; set; }
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
            Assert.Equal(2, plans.Count);
            Assert.Contains(plans, p => p.Name == "Mean");
            Assert.Contains(plans, p => p.Name == "Count");
        }
    }
}
