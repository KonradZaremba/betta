// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using Betta.Attributes;
using Betta.Docs;
using Betta.Interfaces;
using Rhino.Geometry;
using Xunit;

namespace Betta.Docs.Tests;

/// <summary>A plugin collection betta-docs reads from a copy of this test assembly.</summary>
public class DocsFixture : IBettaCollection
{
    [GrasshopperMethod("Plain Sum", "Adds two numbers")]
    public double PlainSum(double a, double b) => a + b;

    [GrasshopperMethod("Panel Layout", "Takes every kind of parameter the runtime fills itself")]
    public (List<Curve> Outlines, int SheetCount) PanelLayout(
        Surface surface,
        double width,
        CancellationToken token,
        IProgress<double> progress,
        IBettaReport report,
        [GrasshopperMenuState] string mode,
        [GrasshopperSecret("docs-test")] string key,
        [GrasshopperTrigger] bool run) => (new List<Curve>(), 0);

    [GrasshopperMethod("Points In Points Out", "Rhino types in and out")]
    public List<Point3d> Offset(List<Point3d> points, Curve guide) => points;

    [GrasshopperMethod("Word Splitting", "Tuple element names become words")]
    public (int FlatCount, int SheetCount, string CSVPath, int UCount, int PanelIDs, int IDs, int CSV, double totalArea, int flat_count, double Sum) Words() => default;

    [GrasshopperMethod("Async Number", "Task<T> outputs are T's")]
    public Task<double> AsyncNumber(double x) => Task.FromResult(x);

    [GrasshopperMethod("Async Stats", "ValueTask<tuple> outputs are the tuple's")]
    public ValueTask<(double Sum, double Average)> AsyncStats(List<double> xs) => default;

    [GrasshopperMethod("Stream Numbers", "IObservable<T> outputs are T's")]
    public IObservable<double> StreamNumbers(double x) => throw new NotSupportedException();

    [GrasshopperMethod("Async Nothing", "A bare Task has no outputs")]
    public Task AsyncNothing(double x) => Task.CompletedTask;

    [GrasshopperMethod("Async ValueNothing", "A bare ValueTask has no outputs")]
    public ValueTask AsyncValueNothing(double x) => default;

    [GrasshopperMethod("Does Nothing", "void has no outputs")]
    public void DoesNothing(double x) { }

    [GrasshopperMethod("Report Card", "A class return explodes into its explodable properties")]
    public ReportCard MakeReportCard() => new();

    [GrasshopperMethod("Raw Bitmap", "Bitmap is one output")]
    public System.Drawing.Bitmap MakeBitmap() => throw new NotSupportedException();

    [GrasshopperMethod("Array Counts", "Arrays are list access")]
    public List<int> ArrayCounts(IEnumerable<string> names, string[] tags) => new();
}

[GrasshopperOpaque]
public class DocsOpaque { }

public class ReportCard
{
    public double TotalArea { get; set; }
    public DocsOpaque Sheet { get; set; } = new();
    public List<int> PanelIDs { get; set; } = new();
    public List<DocsOpaque> Sheets { get; set; } = new();
    public Dictionary<string, int> Lookup { get; set; } = new();
}

// Env vars and Console.Error are process-wide; one collection keeps every Discoverer call serial.
[CollectionDefinition("Discoverer", DisableParallelization = true)]
public class DiscovererCollection { }

[Collection("Discoverer")]
public class DiscovererTests : IDisposable
{
    private const string RhinoVar = "BETTA_RHINO_SYSTEM_FOLDER";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "betta-docs-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _pluginDll;
    private readonly string? _savedVar = Environment.GetEnvironmentVariable(RhinoVar);
    private readonly TextWriter _savedErr = Console.Error;
    private readonly StringWriter _err = new();

    public DiscovererTests()
    {
        var pluginFolder = Path.Combine(_root, "Betta");
        Directory.CreateDirectory(pluginFolder);
        var self = typeof(DiscovererTests).Assembly.Location;
        _pluginDll = Path.Combine(pluginFolder, Path.GetFileName(self));
        File.Copy(self, _pluginDll);
        Console.SetError(_err);
    }

    public void Dispose()
    {
        Console.SetError(_savedErr);
        Environment.SetEnvironmentVariable(RhinoVar, _savedVar);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A "System" folder holding only RhinoCommon.dll, as a Rhino 8 install lays it out.</summary>
    private string UseRhinoSystemFolder(bool withRhinoCommon)
    {
        var system = Path.Combine(_root, "Rhino", "System");
        Directory.CreateDirectory(system);
        if (withRhinoCommon)
        {
            var source = typeof(DiscovererTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "RhinoCommonPath").Value!;
            File.Copy(source, Path.Combine(system, "RhinoCommon.dll"));
        }
        Environment.SetEnvironmentVariable(RhinoVar, system);
        return system;
    }

    private DiscoveredComponent Component(string name)
    {
        var all = Discoverer.Discover(_pluginDll);
        return Assert.Single(all, c => c.name == name);
    }

    [Fact]
    public void Finds_RhinoCommon_in_the_folder_named_by_the_environment_variable()
    {
        UseRhinoSystemFolder(withRhinoCommon: true);

        var c = Component("Points In Points Out");

        Assert.DoesNotContain("skip", _err.ToString());
        Assert.Equal(new[] { "points", "guide" }, c.inputs.Select(i => i.ClrName));
        Assert.Equal("Point3d", c.inputs[0].TypeDisplay);
        Assert.True(c.inputs[0].IsList);
        Assert.Equal("Curve", c.inputs[1].TypeDisplay);
        var output = Assert.Single(c.outputs);
        Assert.Equal("Point3d", output.TypeDisplay);
        Assert.True(output.IsList);
    }

    [Fact]
    public void Skips_methods_with_Rhino_types_and_says_which_reference_is_missing_when_RhinoCommon_is_not_found()
    {
        UseRhinoSystemFolder(withRhinoCommon: false);

        var names = Discoverer.Discover(_pluginDll).Select(c => c.name).ToList();

        Assert.DoesNotContain("Points In Points Out", names);
        Assert.Contains("Plain Sum", names);
        Assert.Contains("skip DocsFixture.Offset: missing reference Could not find assembly 'RhinoCommon", _err.ToString());
    }

    [Fact]
    public void Gives_no_pin_to_parameters_the_runtime_fills_itself()
    {
        UseRhinoSystemFolder(withRhinoCommon: true);

        var c = Component("Panel Layout");

        // CancellationToken, IProgress<T>, IBettaReport, menu state, secret and trigger are all dropped.
        Assert.Equal(new[] { "surface", "width" }, c.inputs.Select(i => i.ClrName));
    }

    [Fact]
    public void Reports_a_tuple_element_of_List_T_as_a_list_output()
    {
        UseRhinoSystemFolder(withRhinoCommon: true);

        var c = Component("Panel Layout");

        // Runtime: ParamVector.ToGhParam sets Access from the type, so any IEnumerable is a list.
        Assert.Collection(c.outputs,
            o => { Assert.Equal("Outlines", o.Name); Assert.True(o.IsList); Assert.Equal("Curve", o.TypeDisplay); },
            o => { Assert.Equal("Sheet Count", o.Name); Assert.False(o.IsList); Assert.Equal("int", o.TypeDisplay); });
    }

    [Fact]
    public void Splits_tuple_element_names_into_words_for_the_output_names()
    {
        var c = Component("Word Splitting");

        Assert.Equal(
            new[] { "Flat Count", "Sheet Count", "CSV Path", "U Count", "Panel IDs", "IDs", "CSV", "Total Area", "Flat count", "Sum" },
            c.outputs.Select(o => o.Name));
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
    public void SplitWords_gives_the_same_names_as_the_runtime(string identifier, string expected)
    {
        Assert.Equal(expected, OutputPlanner.SplitWords(identifier));
    }

    [Fact]
    public void Unwraps_Task_of_T_to_the_outputs_of_T()
    {
        var o = Assert.Single(Component("Async Number").outputs);

        Assert.Equal("Output", o.Name);
        Assert.Equal("double", o.TypeDisplay);
    }

    [Fact]
    public void Unwraps_ValueTask_of_a_tuple_to_one_output_per_element()
    {
        var c = Component("Async Stats");

        Assert.Equal(new[] { "Sum", "Average" }, c.outputs.Select(o => o.Name));
    }

    [Fact]
    public void Unwraps_IObservable_of_T_to_the_outputs_of_T()
    {
        var o = Assert.Single(Component("Stream Numbers").outputs);

        Assert.Equal("double", o.TypeDisplay);
    }

    [Theory]
    [InlineData("Async Nothing")]
    [InlineData("Async ValueNothing")]
    [InlineData("Does Nothing")]
    public void A_method_returning_void_Task_or_ValueTask_has_no_outputs(string name)
    {
        Assert.Empty(Component(name).outputs);
    }

    [Fact]
    public void Reports_arrays_and_IEnumerable_of_T_as_list_access_like_the_runtime()
    {
        var c = Component("Array Counts");

        Assert.All(c.inputs, i => { Assert.True(i.IsList); Assert.Equal("string", i.TypeDisplay); });
        var o = Assert.Single(c.outputs);
        Assert.True(o.IsList);
        Assert.Equal("int", o.TypeDisplay);
    }

    [Fact]
    public void Documents_a_tuple_of_more_than_seven_elements_with_one_output_per_element()
    {
        // ValueTuple nests element 8 onwards in TRest; the Word Splitting fixture has ten.
        Assert.Equal(10, Component("Word Splitting").outputs.Count);
    }

    [Fact]
    public void Explodes_a_class_into_simple_opaque_and_list_properties_only()
    {
        var c = Component("Report Card");

        // Dictionary<string,int> does not qualify; names are split like the runtime's.
        Assert.Collection(c.outputs,
            o => { Assert.Equal("Total Area", o.Name); Assert.Equal("Total Area", o.NickName); Assert.False(o.IsList); Assert.Equal("double", o.TypeDisplay); },
            o => { Assert.Equal("Sheet", o.Name); Assert.False(o.IsList); Assert.Equal("DocsOpaque", o.TypeDisplay); },
            o => { Assert.Equal("Panel IDs", o.Name); Assert.True(o.IsList); Assert.Equal("int", o.TypeDisplay); },
            o => { Assert.Equal("Sheets", o.Name); Assert.True(o.IsList); Assert.Equal("DocsOpaque", o.TypeDisplay); });
    }

    [Fact]
    public void A_Bitmap_return_is_one_Image_output_not_its_properties()
    {
        var o = Assert.Single(Component("Raw Bitmap").outputs);

        Assert.Equal("Image", o.Name);
        Assert.Equal("Img", o.NickName);
        Assert.Equal("Bitmap output", o.Description);
    }
}
