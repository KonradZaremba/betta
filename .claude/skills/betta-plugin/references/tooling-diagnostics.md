# Tooling, diagnostics, canonical examples

## Built-in diagnostics

- **Betta Inspector** component (toolbar: Betta / Inspector) emits one line per registered descriptor: category, name, method signature, GUID, source DLL, capability flags.
- **`Betta_Status` Rhino command** writes the same to the Rhino command line; useful when GH isn't open.
- **Log**: `%AppData%\Grasshopper\Libraries\Betta.log` (why a plugin failed to register); also tailable via the `betta_recent_logs` MCP tool.
- **`betta-docs` dotnet tool** (`Betta.Docs`) walks loaded plugin DLLs and emits markdown documentation: one .md per (Category, SubCategory). Install with `dotnet tool install --global Betta.Docs`.

## Tooling

- **Templates**: scaffold a restorable project instead of hand-writing the csproj: `dotnet new install Betta.Templates`, then `dotnet new bettaplugin-basic -n MyPlugin` (plain methods), `bettaplugin-opaque` (opaque domain pipeline) or `bettaplugin-opaque-preview` (opaque + `IBettaPreview`).
- **Betta.Mcp**: MCP server: `dotnet tool install --global Betta.Mcp`, then `claude mcp add betta -- betta-mcp`. Tools: `betta_validate_signature` (heuristic check that a signature like `"double Cube(double x)"` maps before you write it), `betta_describe` (look up a published component by display name: signature, params, GUID, whether the GUID is pinned or version-dependent, and every GUID Betta.log recorded for it: the value to pin), `betta_status` (DLLs in the Betta folder and method counts, without executing plugin code), `betta_recent_logs` (tail `Betta.log`; why a plugin failed to register).

## Canonical examples (inside the Betta repo, `KonradZaremba/betta`)

- Templates: `templates/Betta.Templates/content/`; MCP server: `Betta.Mcp/`
- Terse string-ops sample (project reference): `Betta.Strings/IStringCollection.cs`
- Defaults + list input + tuple output + custom icon + package-reference authoring: `samples/Betta.Quickstart/`
- Graduated feature tour across 4 collections (basics -> intermediate -> advanced -> no-interface class-direct), incl. Rhino geometry: `samples/Betta.Tour/`
- The attribute definitions: `Betta.Abstractions/Attributes/`
- Hand-written `GH_Component` vs. Betta, side by side: `docs/betta-vs-grasshopper.md` (`docs/` is gitignored: on disk locally, not on GitHub).
