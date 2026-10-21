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

    [GrasshopperMethod("Derived Remap", "GUID comes from the signature")]
    public double DerivedRemap(double value) => value;

    [GrasshopperMethod("Batch Remap", "Generic over this plugin's own type")]
    public List<DescribeFixture> BatchRemap(int count) => new();
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

        // Betta.log lines as ComponentRegistry writes them; a plugin update re-registered Batch Remap.
        var log = Path.Combine(_root, "Betta.log");
        File.WriteAllLines(log, new[]
        {
            $"09:00:00.000 [INFO] ComponentRegistry:   + Betta/General/Batch Remap ({OldGuid})",
            $"09:00:00.001 [INFO] ComponentRegistry:   + Betta/General/Derived Remap ({DescribeFixture.PinnedGuid})",
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

    [Fact]
    public void Derives_the_guid_from_the_signature_when_none_is_pinned()
    {
        var d = _inspector.Describe("Derived Remap");

        // ComponentDescriptor.GenerateGuidFromMethod: MD5 of "Type|Method|paramType name,...|ReturnType".
        var input = $"{typeof(DescribeFixture).FullName}|DerivedRemap|System.Double value|System.Double";
        var expected = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToString();
        Assert.NotNull(d);
        Assert.Equal(expected, d!.Guid);
        Assert.False(d.GuidPinned);
        Assert.False(d.GuidVersionDependent);
    }

    [Fact]
    public void A_pinned_guid_does_not_depend_on_versions()
    {
        var d = _inspector.Describe("Pinned Remap")!;

        Assert.True(d.GuidPinned);
        Assert.False(d.GuidVersionDependent);
        Assert.Empty(d.VersionedAssemblies);
    }

    [Fact]
    public void A_derived_guid_on_a_generic_signature_names_the_versions_it_embeds()
    {
        var d = _inspector.Describe("Batch Remap")!;

        // List<DescribeFixture>: the type name carries this plugin's own assembly version.
        Assert.False(d.GuidPinned);
        Assert.True(d.GuidVersionDependent);
        Assert.Contains(d.VersionedAssemblies, a => a.StartsWith("Betta.Mcp.Tests ", StringComparison.Ordinal));
        Assert.Contains(d.Notes, n => n.Contains("Guid = \"" + d.Guid + "\""));
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
    public void Lists_the_guids_Betta_logged_for_the_component_oldest_first()
    {
        var d = _inspector.Describe("Batch Remap")!;

        Assert.Equal(new[] { OldGuid, NewerGuid }, d.LoggedGuids);
    }
}
