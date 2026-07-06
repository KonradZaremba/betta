# Betta.Mcp

Model Context Protocol (MCP) server that exposes the live state of a
[Betta](https://github.com/KonradZaremba/GH_AutoCreator) Grasshopper
installation to AI agents — Claude Desktop, Cursor, Continue, anything that
speaks MCP over stdio.

It answers questions like:

- "What Betta plugins are loaded?"
- "Why did `Foo.Bar.dll` fail to register?"
- "Describe the component named `Upper`."
- "Would Betta accept `double Cube(double x)` as a method signature?"

without the user pasting `Betta.log` or DLL paths into chat.

## What it does

- **Reads the plugin folder** at `%AppData%/Grasshopper/Libraries/Betta/`
  using `MetadataLoadContext` — it never executes plugin code or requires
  Rhino installed.
- **Tails the Betta log** at `%AppData%/Grasshopper/Libraries/Betta.log`.
- **Recomputes the deterministic GUID** for each discovered component using
  the exact same MD5-over-signature scheme `ComponentDescriptor` uses, so the
  GUIDs Betta.Mcp reports match the ones a saved `.gh` is bound to.

## Tools

| Tool | Input | Returns |
| --- | --- | --- |
| `betta_status` | `{}` | List of DLLs, each with `{file, size, mtime, isBettaPlugin, collectionCount, methodCount}`. |
| `betta_recent_logs` | `{lines?: int}` (default 50) | The last N lines of `Betta.log`. |
| `betta_describe` | `{name: string}` | The descriptor for a component by display name (case-insensitive). |
| `betta_validate_signature` | `{signature: string, opaqueTypes?: string[]}` | Heuristic accept/reject for a method signature against Betta's known parameter / output types. |

## Resources

| URI | Content |
| --- | --- |
| `betta://log` | Tail of `Betta.log`. |
| `betta://registry` | Same payload as `betta_status`, formatted as pretty JSON. |

## Wiring it into Claude Desktop

Edit `claude_desktop_config.json` (Settings → Developer → Edit Config) and
add an entry under `mcpServers`. The simplest form, after a
`dotnet pack` + `dotnet tool install`:

```json
{
  "mcpServers": {
    "betta": {
      "command": "betta-mcp"
    }
  }
}
```

If you'd rather run from a build output without installing the tool:

```json
{
  "mcpServers": {
    "betta": {
      "command": "C:\\path\\to\\Betta.Mcp\\bin\\Release\\net8.0\\betta-mcp.exe"
    }
  }
}
```

Or via `dotnet run` during development:

```json
{
  "mcpServers": {
    "betta": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\Users\\konra\\source\\repos\\betta\\Betta.Mcp\\Betta.Mcp.csproj", "--no-build"]
    }
  }
}
```

Restart Claude Desktop. The server appears as **betta** in the MCP slash menu
with the four tools above.

## Build / pack / install

```pwsh
# Build
dotnet build Betta.Mcp/Betta.Mcp.csproj -c Release

# Pack (NuGet + dotnet-tool)
dotnet pack Betta.Mcp/Betta.Mcp.csproj -c Release
# -> artifacts/Betta.Mcp.0.5.0.nupkg

# Install globally as a CLI tool
dotnet tool install --global Betta.Mcp --add-source artifacts/
```

## Smoke test

The server is line-delimited JSON-RPC 2.0 over stdio. The fastest "is it
alive?" check is to pipe a single `tools/list` call to it:

```pwsh
'{"jsonrpc":"2.0","method":"tools/list","id":1}' | dotnet run --project Betta.Mcp -- 
```

Expected output: a JSON-RPC response whose `result.tools` contains
`betta_status`, `betta_recent_logs`, `betta_describe`,
`betta_validate_signature`.

A non-interactive one-shot mode is also provided for CI:

```pwsh
dotnet run --project Betta.Mcp -- --tools-list
```

Prints `{"tools":["betta_status","betta_recent_logs","betta_describe","betta_validate_signature"]}` and exits.

## Configuration

Environment variables (optional):

- `BETTA_PLUGIN_FOLDER` — override the plugin folder Betta watches.
- `BETTA_LOG_FILE` — override the log file path.

Both default to the standard Grasshopper Libraries locations under
`%AppData%`.

## License

MPL-2.0. See [LICENSE](../LICENSE).
