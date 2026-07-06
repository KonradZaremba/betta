// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Mcp.Protocol;
using Betta.Mcp.Tools;

namespace Betta.Mcp;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // --tools-list / --selftest: print the tool list to stdout (one shot)
        // and exit. Useful for unit tests and CI smoke checks without spinning
        // up the full JSON-RPC loop.
        if (args.Length > 0 && (args[0] == "--tools-list" || args[0] == "--selftest"))
        {
            var registry = new ToolRegistry();
            registry.RegisterAll();
            var names = registry.Tools.Select(t => t.Name);
            Console.WriteLine(JsonSerializer.Serialize(new { tools = names }));
            return 0;
        }

        var server = new McpServer();
        await server.RunAsync(Console.In, Console.Out, default).ConfigureAwait(false);
        return 0;
    }
}
