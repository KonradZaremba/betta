// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using Rhino.Geometry;
using Betta.Attributes;
using System;

namespace Betta.Interfaces
{
    public interface ISimpleService : IBettaCollection
    {
        [GrasshopperMethod(
            Name = "Add Values",
            NickName = "Add",
            Description = "Adds two numbers and processes additional data",
            Category = "Betta",
            SubCategory = "Math")]
        double GetValue(
            [GrasshopperParameter("Numbers", "Nums", "List of numbers to process")] List<double> list,
            [GrasshopperParameter("Value A", "A", "First value")] double value145,
            [GrasshopperParameter("Value B", "B", "Second value")] double value245,
            [GrasshopperParameter("Text", "Txt", "Test string input")] string testString,
            [GrasshopperParameter("Points", "Pts", "List of 3D points")] List<Point3d> pointTest);

        [GrasshopperMethod(
            Name = "Add 100",
            NickName = "Add100", 
            Description = "Adds 100 to the input value",
            Category = "Betta",
            SubCategory = "Math")]
        double GetValue2(
            [GrasshopperParameter("Input", "In", "Value to increment")] double value21212);

        [GrasshopperMethod(
            Name = "Create Circle Info",
            NickName = "CircInfo",
            Description = "Creates a circle and returns its properties",
            Category = "Betta",
            SubCategory = "Geometry")]
        Tuple<Circle, double, double> CreateCircleInfo(
            [GrasshopperParameter("Center", "C", "Center point")] Point3d center,
            [GrasshopperParameter(Name = "Radius", NickName = "R", Description = "Circle radius", DefaultValue = 1.0)] double radius);

        [GrasshopperMethod(
            Name = "Process List",
            NickName = "ProcList",
            Description = "Processes a list of numbers and returns results",
            Category = "Betta",
            SubCategory = "Math")]
        List<double> ProcessNumbers(
            [GrasshopperParameter("Numbers", "N", "Input numbers")] List<double> numbers,
            [GrasshopperParameter("Operation", "Op", "Operation type")] string operation);

        [GrasshopperMethod(
            Name = "Get Statistics",
            NickName = "Stats",
            Description = "Calculate statistics for a list of numbers",
            Category = "Betta",
            SubCategory = "Math")]
        MathStatistics GetStatistics(
            [GrasshopperParameter("Numbers", "N", "Input numbers")] List<double> numbers);

        [GrasshopperMethod(
            Name = "Delayed Echo",
            NickName = "Delay",
            Description = "Returns the message after N seconds — demonstrates async components",
            Category = "Betta",
            SubCategory = "Async")]
        Task<string> DelayedEcho(
            [GrasshopperParameter(Name = "Message", NickName = "M", Description = "Message to echo back", DefaultValue = "hello")] string message,
            [GrasshopperParameter(Name = "Seconds", NickName = "S", Description = "Delay in seconds", DefaultValue = 2.0)] double seconds);
    }

    /// <summary>
    /// Example of a complex output type that will be decomposed
    /// </summary>
    public class MathStatistics
    {
        public double Mean { get; set; }
        public double Median { get; set; }
        public double StandardDeviation { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public int Count { get; set; }
    }
}
