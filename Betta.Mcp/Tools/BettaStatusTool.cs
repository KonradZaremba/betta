// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Mcp.Inspection;

namespace Betta.Mcp.Tools;

/// <summary>
/// betta_status — walks the plugin folder and reports what Betta would load.
/// Used as the "what's installed?" answer.
/// </summary>
public sealed class BettaStatusTool : ITool
{
    public string Name => "betta_status";
    public string Description =>
        "List every DLL in the Betta plugin folder (%AppData%/Grasshopper/Libraries/Betta/), report which contain IBettaCollection types and how many [GrasshopperMethod] entries they expose. Uses MetadataLoadContext — does not execute plugin code.";

    public object InputSchema => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false,
    };

    public Task<object> InvokeAsync(JsonElement? arguments)
    {
        using var inspector = new PluginInspector();
        var entries = inspector.ListAll();
        var result = new
        {
            pluginFolder = inspector.PluginFolder,
            folderExists = Directory.Exists(inspector.PluginFolder),
            entries = entries.Select(e => new
            {
                file = e.File,
                fullPath = e.FullPath,
                size = e.SizeBytes,
                mtime = e.ModifiedUtc.ToString("O"),
                isBettaPlugin = e.IsBettaPlugin,
                collectionCount = e.CollectionCount,
                methodCount = e.MethodCount,
                error = e.Error,
            }).ToList(),
        };
        return Task.FromResult<object>(result);
    }
}
