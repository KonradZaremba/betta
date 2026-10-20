// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
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

        // MCP's stdio transport is UTF-8. Console.In/Out use the console code
        // page on Windows (e.g. 852), which garbles any non-ASCII argument a
        // client sends, so wrap the raw streams explicitly. The reader also
        // strips a leading BOM.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stdin = new StreamReader(Console.OpenStandardInput(), utf8);
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8);
        var server = new McpServer();
        await server.RunAsync(stdin, stdout, default).ConfigureAwait(false);
        return 0;
    }
}
