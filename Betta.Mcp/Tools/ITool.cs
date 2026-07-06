// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Betta.Mcp.Tools;

/// <summary>
/// Each MCP tool is a self-contained handler: a name, a human-readable
/// description, a JSON Schema for the inputs (so the LLM knows what to send),
/// and an invoker that turns the arguments into a structured result.
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    object InputSchema { get; }
    Task<object> InvokeAsync(JsonElement? arguments);

    /// <summary>Default shape consumed by <c>tools/list</c>.</summary>
    object Describe() => new { name = Name, description = Description, inputSchema = InputSchema };
}

public sealed class ToolRegistry
{
    private readonly List<ITool> _tools = new();
    public IReadOnlyList<ITool> Tools => _tools;

    public void RegisterAll()
    {
        _tools.Add(new BettaStatusTool());
        _tools.Add(new BettaRecentLogsTool());
        _tools.Add(new BettaDescribeTool());
        _tools.Add(new BettaValidateSignatureTool());
    }
}
