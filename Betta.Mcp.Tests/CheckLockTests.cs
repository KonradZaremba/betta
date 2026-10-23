// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Mcp.Tools;
using Xunit;
using static Betta.Mcp.Tests.DescribeTests;

namespace Betta.Mcp.Tests;

/// <summary>
/// betta_check_lock against a copy of this test assembly, whose <see cref="DescribeFixture"/> is
/// the plugin: the same verdicts the build's BETTA012 gives, read from the DLL.
/// </summary>
public class CheckLockTests : IDisposable
{
    private const string Fixture = "Betta.Mcp.Tests.DescribeFixture";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "betta-mcp-lock-" + Guid.NewGuid().ToString("N"));
    private readonly string _dll;
    private readonly string _lock;

    public CheckLockTests()
    {
        var bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        var self = typeof(CheckLockTests).Assembly.Location;
        _dll = Path.Combine(bin, Path.GetFileName(self));
        File.Copy(self, _dll);
        _lock = Path.Combine(_root, "betta.lock");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private async Task<JsonElement> Check(string? lockFile = null) =>
        JsonSerializer.SerializeToElement(await new BettaCheckLockTool().InvokeAsync(
            JsonSerializer.SerializeToElement(new { lockFile = lockFile ?? _lock, dll = _dll })));

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToList();

    [Fact]
    public async Task Without_a_lock_it_lists_the_file_to_start_one_with()
    {
        var result = await Check();

        Assert.False(result.GetProperty("lockExists").GetBoolean());
        Assert.Empty(result.GetProperty("gone").EnumerateArray());
        var toAdd = Strings(result.GetProperty("toAdd"));
        Assert.Equal(6, toAdd.Count);
        Assert.Contains($"{Md5($"{Fixture}|DerivedRemap")} {Fixture}.DerivedRemap", toAdd);
        Assert.Contains($"{DescribeFixture.PinnedGuid} {Fixture}.PinnedRemap", toAdd);
        Assert.Contains(toAdd, l => l.EndsWith($" {Fixture}.Scale(System.Collections.Generic.List`1[System.Double])"));
    }

    [Fact]
    public async Task A_locked_former_name_still_answers()
    {
        var lines = Strings((await Check()).GetProperty("toAdd"));
        // MapRange lists FormerNames = { "Remap2" }: files saved as Remap2 still open.
        lines.Add($"{Md5($"{Fixture}|Remap2")} {Fixture}.Remap2");
        File.WriteAllLines(_lock, lines);

        var result = await Check(_root);
        Assert.True(result.GetProperty("lockExists").GetBoolean());
        Assert.Empty(result.GetProperty("gone").EnumerateArray());
        Assert.Empty(result.GetProperty("toAdd").EnumerateArray());
    }

    [Fact]
    public async Task A_rename_without_FormerNames_is_reported_with_its_fix()
    {
        // The lock from before Map Range was renamed from "Remap3": every other component, and Remap3.
        var lines = Strings((await Check()).GetProperty("toAdd")).Where(l => !l.EndsWith(".MapRange")).ToList();
        lines.Add($"{Md5($"{Fixture}|Remap3")} {Fixture}.Remap3");
        File.WriteAllLines(_lock, lines);

        var result = await Check();
        var gone = Assert.Single(result.GetProperty("gone").EnumerateArray());
        Assert.Equal($"{Fixture}.Remap3", gone.GetProperty("label").GetString());
        Assert.Equal(lines.Count, gone.GetProperty("line").GetInt32());
        Assert.Contains(@"renamed to 'MapRange', add FormerNames = new[] { ""Remap3"" }", gone.GetProperty("message").GetString());
        var fix = gone.GetProperty("fix");
        Assert.Equal("MethodFormerName", fix.GetProperty("kind").GetString());
        Assert.Equal("Remap3", fix.GetProperty("value").GetString());
        Assert.Equal($"{Fixture}.MapRange", fix.GetProperty("on").GetString());
        // MapRange's new GUID isn't added until the rename is resolved.
        Assert.Empty(result.GetProperty("toAdd").EnumerateArray());
    }
}
