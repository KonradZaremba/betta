// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Mcp.Inspection;

namespace Betta.Mcp.Tools;

/// <summary>
/// betta_describe — given a display name like "Upper", finds the matching
/// [GrasshopperMethod] across every plugin DLL and returns its descriptor.
/// First match wins.
/// </summary>
public sealed class BettaDescribeTool : ITool
{
    public string Name => "betta_describe";
    public string Description =>
        "Find a Betta-published component by display name and return its descriptor (category, subcategory, method signature, parameters with types, return type, icon resource, deterministic GUID).";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            name = new
            {
                type = "string",
                description = "The component's display name (case-insensitive). Matches [GrasshopperMethod(Name=...)] or the method name fallback.",
            },
        },
        required = new[] { "name" },
        additionalProperties = false,
    };

    public Task<object> InvokeAsync(JsonElement? arguments)
    {
        if (!arguments.HasValue
            || arguments.Value.ValueKind != JsonValueKind.Object
            || !arguments.Value.TryGetProperty("name", out var nameEl)
            || nameEl.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("betta_describe requires { name: string }.");
        }

        var name = nameEl.GetString() ?? string.Empty;
        using var inspector = new PluginInspector();
        var found = inspector.Describe(name);
        return Task.FromResult<object>(found is null
            ? new { found = false, name }
            : (object)new { found = true, descriptor = found });
    }
}
