// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Mcp.Inspection;

namespace Betta.Mcp.Tools;

/// <summary>
/// betta_recent_logs — tails Betta.log so the agent can read the most recent
/// run without the user pasting it in.
/// </summary>
public sealed class BettaRecentLogsTool : ITool
{
    public string Name => "betta_recent_logs";
    public string Description =>
        "Return the last N lines of %AppData%/Grasshopper/Libraries/Betta.log (default 50). Useful for diagnosing why a plugin failed to register or what was loaded at startup.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            lines = new
            {
                type = "integer",
                description = "Number of lines from the end of the log to return. Defaults to 50, max 2000.",
                minimum = 1,
                maximum = 2000,
            },
        },
        additionalProperties = false,
    };

    public Task<object> InvokeAsync(JsonElement? arguments)
    {
        int n = 50;
        if (arguments.HasValue && arguments.Value.ValueKind == JsonValueKind.Object
            && arguments.Value.TryGetProperty("lines", out var linesEl)
            && linesEl.TryGetInt32(out var requested))
        {
            n = Math.Clamp(requested, 1, 2000);
        }

        var path = Paths.LogFile;
        if (!File.Exists(path))
        {
            return Task.FromResult<object>(new
            {
                path,
                exists = false,
                lines = Array.Empty<string>(),
            });
        }

        // Read share-friendly: Betta's FileLoggerProvider holds an exclusive
        // write lock briefly. Using FileShare.ReadWrite lets us tail while
        // Grasshopper is running.
        var collected = new List<string>(n);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs))
        {
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (collected.Count == n) collected.RemoveAt(0);
                collected.Add(line);
            }
        }

        return Task.FromResult<object>(new
        {
            path,
            exists = true,
            lines = collected,
        });
    }
}
