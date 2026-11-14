// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Mcp.Tools;
using Xunit;

namespace Betta.Mcp.Tests;

public class ValidateSignatureTests
{
    private static async Task<JsonElement> Validate(string signature, params string[] opaque)
    {
        var args = JsonSerializer.SerializeToElement(new { signature, opaqueTypes = opaque });
        var result = await new BettaValidateSignatureTool().InvokeAsync(args);
        return JsonSerializer.SerializeToElement(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static string[] Strings(JsonElement arr) => arr.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Theory]
    [InlineData("double Cube(double x)")]
    [InlineData("public static List<Point3d> Pts(int n)")]
    [InlineData("IObservable<double> Watch(string path)")]
    [InlineData("Task<int> Slow(int n, CancellationToken ct, IProgress<double> p)")]
    [InlineData("ValueTask<Point3d> F(double x)")]
    [InlineData("ValueTask Fire(int x)")]
    [InlineData("double[] Sum(params double[] xs)")]
    [InlineData("void Set([GrasshopperSecret(\"svc.key\")] string key, [GrasshopperTrigger] bool run)")]
    [InlineData("void Pick([GrasshopperMenuState(Name = \"mode\")] string mode, [GrasshopperValueList(Items = new[] { \"a\", \"b\" })] string pick)")]
    [InlineData("[GrasshopperMethod(\"x\")] public int Go(int a = 3)")]
    [InlineData("string Describe(DayOfWeek day)")]
    [InlineData("(double Sum, double Average) Stats(List<double> xs)")]
    [InlineData("(int, int, int, int, int, int, int, int, int) Nine(int a)")]
    [InlineData("ValueTuple<int, int, int, int, int, int, int, ValueTuple<int, int>> Nine2(int a)")]
    [InlineData("Bitmap Render(int w)")]
    [InlineData("GH_Structure<GH_Number> Tree(GH_Structure<GH_Number> t)")]
    public async Task Accepts_runtime_supported_signatures(string sig)
    {
        var r = await Validate(sig);
        Assert.True(r.GetProperty("ok").GetBoolean(), $"{sig}: {r.GetProperty("problems")}");
        Assert.Empty(Strings(r.GetProperty("problems")));
    }

    [Fact]
    public async Task Synthetic_and_attribute_parameters_have_no_pin()
    {
        var r = await Validate("Task<int> Slow(int n, CancellationToken ct, IProgress<double> p, [GrasshopperSecret(\"svc.key\", Prompt = \"Key, please\")] string key)");
        var wired = r.GetProperty("parsed").GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("wired").GetBoolean()).Select(p => p.GetProperty("name").GetString()).ToArray();
        Assert.Equal(new[] { "n" }, wired);
    }

    [Theory]
    [InlineData("Task Run(int n)", "Task")]
    [InlineData("ValueTask Fire(int x)", "ValueTask")]
    [InlineData("System.Threading.Tasks.Task Go(CancellationToken ct)", "Task")]
    public async Task Non_generic_task_return_is_async_with_no_outputs(string sig, string bare)
    {
        var r = await Validate(sig);
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Empty(Strings(r.GetProperty("problems")));
        Assert.Contains(Strings(r.GetProperty("notes")), n => n.Contains(bare) && n.Contains("runs asynchronously, no outputs"));
    }

    [Fact]
    public async Task Nine_tuple_return_parses_every_element()
    {
        var r = await Validate("(int a, int b, int c, int d, int e, int f, int g, int h, int i) Nine()");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal("Nine", r.GetProperty("parsed").GetProperty("name").GetString());
        Assert.Empty(Strings(r.GetProperty("notes")));
    }

    [Fact]
    public async Task Unknown_class_return_is_a_note_not_a_problem()
    {
        var r = await Validate("MyResult Compute(double x)");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Empty(Strings(r.GetProperty("problems")));
        Assert.Contains(Strings(r.GetProperty("notes")), n => n.Contains("MyResult") && n.Contains("property outputs"));
    }

    [Fact]
    public async Task Enum_like_unknown_parameter_is_accepted_with_a_note()
    {
        var r = await Validate("double Scale(Mode mode)");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Contains(Strings(r.GetProperty("notes")), n => n.Contains("Mode") && n.Contains("Integer"));
    }

    [Theory]
    [InlineData("Point3d Mid(Curve c)")]
    [InlineData("List<Brep> Split(Brep b, Plane p)")]
    [InlineData("Polyline Outline(Rhino.Geometry.Surface s, Interval u)")]
    public async Task Geometry_types_need_no_note_with_or_without_namespace(string sig)
    {
        var r = await Validate(sig);
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Empty(Strings(r.GetProperty("notes")));
    }

    [Fact]
    public async Task Report_parameter_is_synthetic_and_needs_no_note()
    {
        var r = await Validate("List<Curve> Pack(List<double> widths, IBettaReport report)");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Empty(Strings(r.GetProperty("notes")));
    }

    [Fact]
    public async Task Opaque_types_need_no_note()
    {
        var r = await Validate("MyModel Build(MyModel seed)", "MyModel");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Empty(Strings(r.GetProperty("notes")));
    }

    [Fact]
    public async Task Ienumerable_return_notes_degradation_but_stays_ok()
    {
        var r = await Validate("IEnumerable<int> Many(int n)");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Contains(Strings(r.GetProperty("notes")), n => n.Contains("IEnumerable"));
    }

    [Theory]
    [InlineData("this is not a signature")]
    [InlineData("double Cube(double x")]
    [InlineData("double Cube(onlyonetoken)")]
    [InlineData("")]
    public async Task Unparseable_signature_is_a_problem(string sig)
    {
        var r = await Validate(sig);
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.NotEmpty(Strings(r.GetProperty("problems")));
    }

    [Fact]
    public async Task Void_parameter_is_a_problem()
    {
        var r = await Validate("int Bad(void x)");
        Assert.False(r.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Missing_signature_argument_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new BettaValidateSignatureTool().InvokeAsync(null));
    }

    // --- component preview: the inputs and outputs Betta creates, and how to name them ---

    private static (string Name, string Access, string NameFrom)[] Pins(JsonElement r, string kind) =>
        r.GetProperty("component").GetProperty(kind).EnumerateArray()
            .Select(p => (p.GetProperty("name").GetString()!, p.GetProperty("access").GetString()!, p.GetProperty("nameFrom").GetString()!))
            .ToArray();

    [Fact]
    public async Task Bare_signature_previews_default_names_and_says_how_to_change_them()
    {
        var r = await Validate("double Remap(double value, double sourceMin, double sourceMax, double targetMin, double targetMax)");

        Assert.Equal(new[] { "value", "sourceMin", "sourceMax", "targetMin", "targetMax" }, Pins(r, "inputs").Select(p => p.Name));
        Assert.All(Pins(r, "inputs"), p => Assert.Equal("parameter name", p.NameFrom));
        Assert.Equal(new[] { ("Output", "item", "default") }, Pins(r, "outputs"));

        var hints = Strings(r.GetProperty("hints"));
        Assert.Contains(hints, h => h.Contains("[return: GrasshopperOutput(\"Result\")]"));
        Assert.Contains(hints, h => h.Contains("[GrasshopperParameter(\"Source Min\")] double sourceMin"));
    }

    [Fact]
    public async Task Attribute_names_from_the_signature_are_used()
    {
        var r = await Validate(
            "[GrasshopperMethod(\"Remap\")]\n[return: GrasshopperOutput(\"Result\")]\npublic double Remap(" +
            "[GrasshopperParameter(\"Value\", DefaultValue = 0.5)] double value, [GrasshopperParameter(Name = \"Source Min\")] double sourceMin)");

        Assert.Equal(new[] { ("Value", "item", "attribute"), ("Source Min", "item", "attribute") }, Pins(r, "inputs"));
        Assert.Equal(new[] { ("Result", "item", "attribute") }, Pins(r, "outputs"));
        Assert.Empty(Strings(r.GetProperty("hints")));
    }

    [Fact]
    public async Task Method_target_output_attribute_names_the_output_too()
    {
        var r = await Validate("[GrasshopperOutput(\"Result\")] double Cube(double x)");
        Assert.Equal("Result", Pins(r, "outputs").Single().Name);
    }

    [Fact]
    public async Task Named_tuple_elements_become_outputs_split_into_words()
    {
        // The runtime's own SplitWords examples (OutputPlanner): these must read the same here.
        var r = await Validate("(int FlatCount, string CSVPath, int UCount, List<int> IDs, double sum) Stats(List<double> values)");

        Assert.Equal(new[] { "Flat Count", "CSV Path", "U Count", "IDs", "Sum" }, Pins(r, "outputs").Select(p => p.Name));
        Assert.All(Pins(r, "outputs"), p => Assert.Equal("tuple element", p.NameFrom));
        Assert.Equal("list", Pins(r, "outputs")[3].Access);
        Assert.Equal(new[] { ("values", "list", "parameter name") }, Pins(r, "inputs"));
    }

    [Fact]
    public async Task Unnamed_tuple_takes_indexed_attributes_and_defaults_for_the_rest()
    {
        var r = await Validate("[return: GrasshopperOutput(\"Sum\", Index = 0)] ValueTuple<double, double> SumAndMean(double[] xs)");

        Assert.Equal(new[] { ("Sum", "item", "attribute"), ("Output2", "item", "default") }, Pins(r, "outputs"));
        Assert.Equal("list", Pins(r, "inputs").Single().Access);
        Assert.Contains(Strings(r.GetProperty("hints")), h => h.Contains("Output1..OutputN"));
    }

    [Fact]
    public async Task Async_return_unwraps_and_injected_parameters_get_no_input()
    {
        var r = await Validate("async Task<List<Point3d>> Divide(Curve curve, int count, CancellationToken ct, IProgress<double> progress, IBettaReport report)");

        Assert.Equal(new[] { "curve", "count" }, Pins(r, "inputs").Select(p => p.Name));
        Assert.Equal(new[] { ("Output", "list", "default") }, Pins(r, "outputs"));
    }

    [Theory]
    [InlineData("void Log(string message)")]
    [InlineData("Task Upload(string path)")]
    public async Task No_return_value_means_no_outputs_and_no_output_hint(string signature)
    {
        var r = await Validate(signature);
        Assert.Empty(Pins(r, "outputs"));
        Assert.DoesNotContain(Strings(r.GetProperty("hints")), h => h.Contains("GrasshopperOutput"));
    }

    [Fact]
    public async Task Unknown_class_return_previews_property_outputs()
    {
        var r = await Validate("PanelInfo Inspect(Brep panel)");
        Assert.Equal(new[] { ("(one per public property of PanelInfo)", "item", "properties") }, Pins(r, "outputs"));
    }

    [Fact]
    public async Task Opaque_class_return_is_one_output()
    {
        var r = await Validate("Panel Make(Brep b)", "Panel");
        Assert.Equal(new[] { ("Output", "item", "default") }, Pins(r, "outputs"));
    }

    [Fact]
    public async Task Tree_parameter_previews_tree_access()
    {
        var r = await Validate("int Count(GH_Structure<GH_Number> data)");
        Assert.Equal("tree", Pins(r, "inputs").Single().Access);
    }
}
