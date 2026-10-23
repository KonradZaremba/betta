// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Lock;
using Betta.Mcp.Inspection;

namespace Betta.Mcp.Tools;

/// <summary>
/// betta_check_lock — the build's BETTA012 check, against a built plugin DLL instead of the
/// source: which components in betta.lock nothing answers to any more, and which aren't locked
/// yet. The rules are the generator's own (<see cref="GuidLockCore"/>, a linked file), so it
/// also covers plugins built without Betta.Generators.
/// </summary>
public sealed class BettaCheckLockTool : ITool
{
    public string Name => "betta_check_lock";
    public string Title => "Check betta.lock";
    public string Description =>
        "Check a plugin's betta.lock against its built DLL, as the build's BETTA012 warning does: lists the locked components that no [GrasshopperMethod] " +
        "answers to any more (a rename or removal that breaks saved .gh files), each with the likely cause and the FormerNames or Guid to add, and the " +
        "components not locked yet, as the lines the build would append. Works for plugins built without the Betta.Generators source generator too: " +
        "without a betta.lock, toAdd is the file to start one with. Reads the DLL without running it.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            lockFile = new
            {
                type = "string",
                description = "Path to betta.lock, or to the plugin project folder that holds it.",
            },
            dll = new
            {
                type = "string",
                description = "Path to the built plugin DLL. Default: every plugin in the Betta folder.",
            },
        },
        required = new[] { "lockFile" },
        additionalProperties = false,
    };

    public Task<object> InvokeAsync(JsonElement? arguments)
    {
        var lockArg = arguments.GetString("lockFile")
            ?? throw new ArgumentException("betta_check_lock requires { lockFile: string, dll?: string }.");
        string? dll = arguments.GetString("dll");

        var lockFile = Path.GetFullPath(lockArg);
        if (Directory.Exists(lockFile)) lockFile = Path.Combine(lockFile, GuidLockCore.FileName);
        if (dll != null)
        {
            dll = Path.GetFullPath(dll);
            if (!File.Exists(dll)) throw new ArgumentException($"No plugin DLL at {dll}.");
        }

        var errors = new List<string>();
        using var inspector = new PluginInspector(extraFolders: dll != null ? new[] { Path.GetDirectoryName(dll)! } : null);
        var components = inspector.LockComponents(dll, errors);

        var lockExists = File.Exists(lockFile);
        var entries = lockExists ? GuidLockCore.Parse(SharedFile.ReadLines(lockFile)) : new List<LockEntry>();
        var result = GuidLockCore.Check(entries, components);

        return Task.FromResult<object>(new
        {
            lockFile,
            lockExists,
            dll = dll ?? inspector.PluginFolder,
            components = components.Count,
            locked = entries.Count,
            gone = result.Gone.Select(f => new
            {
                label = f.Entry.Label,
                guid = f.Entry.Guid.ToString("D"),
                line = f.Entry.Line + 1,
                message = f.Message,
                fix = f.Fix == LockFix.None ? null : new { kind = f.Fix.ToString(), value = f.FixValue, on = f.Target.TypeName + "." + f.Target.MethodName },
            }).ToList(),
            toAdd = result.ToAdd.Select(GuidLockCore.Line).ToList(),
            notes = Notes(lockExists, result).ToList(),
            errors,
        });
    }

    private static IEnumerable<string> Notes(bool lockExists, LockResult result)
    {
        if (!lockExists)
            yield return "No betta.lock yet. With Betta.Generators the build writes it; otherwise start one with the toAdd lines and check it in.";
        if (result.Gone.Count > 0)
            yield return "toAdd stays empty until every gone component is resolved, so a rename's new GUID doesn't take the old one's place unnoticed.";
    }
}
