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

    [Theory]
    [InlineData("List<Curve> Pack(List<double> widths)")]
    [InlineData("(double Sum, double Mean) Stats(double[] xs)")]
    [InlineData("double Slow(double x, IProgress<int> progress)")]
    public async Task Generic_signature_without_a_pinned_guid_gets_pin_advice(string sig)
    {
        var r = await Validate(sig);
        Assert.Contains("Guid = ", r.GetProperty("guidAdvice").GetString());
    }

    [Theory]
    [InlineData("[GrasshopperMethod(\"Pack\", Guid = \"8b18f440-198b-0ca5-ac87-0a05f9815894\")] List<Curve> Pack(List<double> widths)")]
    [InlineData("double Cube(double x)")]
    [InlineData("Curve[] Offsets(Curve c, double[] distances)")]
    public async Task Pinned_or_non_generic_signatures_get_no_pin_advice(string sig)
    {
        var r = await Validate(sig);
        Assert.Equal(JsonValueKind.Null, r.GetProperty("guidAdvice").ValueKind);
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
}
