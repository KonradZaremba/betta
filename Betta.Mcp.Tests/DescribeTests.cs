// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;
using Betta.Attributes;
using Betta.Interfaces;
using Betta.Mcp.Inspection;
using Xunit;

namespace Betta.Mcp.Tests;

/// <summary>A plugin collection the inspector reads from a copy of this test assembly.</summary>
public class DescribeFixture : IBettaCollection
{
    public const string PinnedGuid = "8b18f440-198b-0ca5-ac87-0a05f9815894";

    [GrasshopperMethod("Pinned Remap", "Has an explicit GUID", Guid = PinnedGuid)]
    public double PinnedRemap(double number, bool clamp) => number;

    [GrasshopperMethod("Derived Remap", "GUID comes from the type and method name")]
    public double DerivedRemap(double value) => value;

    [GrasshopperMethod("Batch Remap", "Generic over this plugin's own type")]
    public List<DescribeFixture> BatchRemap(int count) => new();

    [GrasshopperMethod("Scale Number", "Overloaded: GUID includes the signature; was the only Scale", FormerNames = new[] { "Scale" })]
    public double Scale(double x) => x;

    [GrasshopperMethod("Scale List", "Overloaded: GUID includes the signature")]
    public List<double> Scale(List<double> xs) => xs;

    [GrasshopperMethod("Map Range", "Renamed from Remap2", FormerNames = new[] { "Remap2" })]
    public double MapRange(double value) => value;
}

public class DescribeTests : IDisposable
{
    private const string OldGuid = "11111111-2222-3333-4444-555555555555";
    private const string NewerGuid = "66666666-7777-8888-9999-000000000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "betta-mcp-describe-" + Guid.NewGuid().ToString("N"));
    private readonly PluginInspector _inspector;

    public DescribeTests()
    {
        var pluginFolder = Path.Combine(_root, "Betta");
        Directory.CreateDirectory(pluginFolder);
        var self = typeof(DescribeTests).Assembly.Location;
        File.Copy(self, Path.Combine(pluginFolder, Path.GetFileName(self)));

        // Betta.log lines as ComponentRegistry writes them; a plugin update re-registered Batch Remap,
        // and the log has rolled to Betta.log.old since the older release ran.
        var log = Path.Combine(_root, "Betta.log");
        File.WriteAllLines(log + ".old", new[]
        {
            $"09:00:00.000 [INFO] ComponentRegistry:   + Betta/General/Batch Remap ({OldGuid})",
            $"09:00:00.001 [INFO] ComponentRegistry:   + Betta/General/Derived Remap ({DescribeFixture.PinnedGuid})",
        });
        File.WriteAllLines(log, new[]
        {
            $"10:00:00.000 [INFO] ComponentRegistry:   + Betta/General/Batch Remap ({NewerGuid})",
        });
        _inspector = new PluginInspector(pluginFolder, log);
    }

    public void Dispose()
    {
        _inspector.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Reports_the_pinned_guid_when_the_attribute_sets_one()
    {
        var d = _inspector.Describe("Pinned Remap");

        Assert.NotNull(d);
        Assert.Equal(DescribeFixture.PinnedGuid, d!.Guid);
    }

    internal static string Md5(string input) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToString();

    [Fact]
    public void Derives_the_guid_from_the_type_and_method_name_when_none_is_pinned()
    {
        var d = _inspector.Describe("Derived Remap");

        // ComponentDescriptor.GenerateGuidFromMethod: MD5 of "Type|Method"; 0.7 hashed the whole signature.
        Assert.NotNull(d);
        Assert.Equal(Md5($"{typeof(DescribeFixture).FullName}|DerivedRemap"), d!.Guid);
        Assert.False(d.GuidPinned);
        Assert.Equal(Md5($"{typeof(DescribeFixture).FullName}|DerivedRemap|System.Double value|System.Double"), d.LegacyGuid);
    }

    [Fact]
    public void Reports_the_alias_guids_from_former_names()
    {
        var d = _inspector.Describe("Map Range")!;

        Assert.Equal(Md5($"{typeof(DescribeFixture).FullName}|MapRange"), d.Guid);
        Assert.Equal(new[] { Md5($"{typeof(DescribeFixture).FullName}|Remap2") }, d.FormerGuids);
        Assert.Contains(d.Notes, n => n.Contains(d.FormerGuids[0]));
    }

    [Fact]
    public void An_overloaded_method_hashes_its_signature()
    {
        var d = _inspector.Describe("Scale Number")!;

        Assert.Equal(Md5($"{typeof(DescribeFixture).FullName}|Scale|System.Double x|System.Double"), d.Guid);
        Assert.Null(d.LegacyGuid);
        Assert.Contains(d.Notes, n => n.Contains("overloaded"));
        // It lists its own name, so it answers to the GUID it had before the overload was added.
        Assert.Equal(new[] { Md5($"{typeof(DescribeFixture).FullName}|Scale") }, d.FormerGuids);
        Assert.Empty(_inspector.Describe("Scale List")!.FormerGuids);
    }

    [Fact]
    public void A_pinned_guid_has_no_legacy_value()
    {
        var d = _inspector.Describe("Pinned Remap")!;

        Assert.True(d.GuidPinned);
        Assert.Null(d.LegacyGuid);
    }

    [Fact]
    public void A_generic_signature_reports_the_versioned_legacy_guid()
    {
        var d = _inspector.Describe("Batch Remap")!;

        var listType = typeof(List<DescribeFixture>).FullName;
        Assert.Contains("Version=", listType);
        Assert.Equal(Md5($"{typeof(DescribeFixture).FullName}|BatchRemap"), d.Guid);
        Assert.Equal(Md5($"{typeof(DescribeFixture).FullName}|BatchRemap|System.Int32 count|{listType}"), d.LegacyGuid);
        Assert.False(d.GuidPinned);
        Assert.NotNull(d.LegacyGuid);
        Assert.NotEqual(d.Guid, d.LegacyGuid);
        Assert.Contains(d.Notes, n => n.Contains(d.LegacyGuid!));
    }

    [Fact]
    public void Only_dlls_that_reference_Betta_Abstractions_are_plugin_candidates()
    {
        // A Libraries\Betta folder also holds plugin dependencies (System.Text.Json, Azure.*, ...).
        // Loading those into the inspector collides with the framework's copies.
        Assert.True(PluginInspector.IsPluginCandidate(typeof(DescribeTests).Assembly.Location));
        Assert.False(PluginInspector.IsPluginCandidate(typeof(FactAttribute).Assembly.Location));
    }

    [Fact]
    public void Lists_the_guids_Betta_logged_for_the_component_oldest_first_including_the_rolled_log()
    {
        var d = _inspector.Describe("Batch Remap")!;

        Assert.Equal(new[] { OldGuid, NewerGuid }, d.LoggedGuids);
    }
}
