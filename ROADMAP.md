# Betta roadmap

What's planned after the current release, roughly in priority order. Shipped work is in [CHANGELOG.md](CHANGELOG.md).

## Next

- **Headless contract test in CI** (testing brief Tier 2) — load the sample plugins through `Betta.Mcp`'s `MetadataLoadContext` loader and assert the expected component set, signatures and zero load errors on every push.
- **Yak from CI** — build the `.yak` on the runner with the downloaded `yak.exe` and push it when a `YAK_TOKEN` secret is set, so one tag publishes to both nuget.org and the Rhino Package Manager.
- **Runtime target** — Betta targets `net7.0-windows`, which Microsoft no longer supports. Move to the runtime current Rhino 8 ships, and lift the Grasshopper `[8.0, 8.27)` pin as Rhino 8 updates.
- **Rhino 8 test fixtures** — re-save the `.gh` test fixtures (and `samples/betta_showcase.gh`) in Rhino 8.
- **Version-independent derived GUIDs** — the GUID hash uses `Type.FullName`, which assembly-qualifies a generic type's arguments (``List`1[[Rhino.Geometry.Curve, RhinoCommon, Version=8.26.25349.19001, …]]``). So a component with `List<T>`, a tuple, `Task<T>`, `IObservable<T>` or `IProgress<T>` in its signature gets a new derived GUID whenever the plugin's own version, RhinoCommon (every Rhino service release) or .NET changes, and saved `.gh` files lose it. Betta.log confirms it: Betta.Files' unchanged `Watch Folder` was published under a different GUID by 0.7.1, 0.7.2 and 0.7.2.1, each matching the hash for that version. Until this is fixed, the docs, the template guide and Betta.Mcp tell authors to pin `Guid` on published components (and `betta_describe` lists the GUIDs Betta has logged, to pin the right one). Fix: hash a version-free type name (namespace + name, recursively for type arguments), and keep today's GUIDs loadable, for example by registering the old-scheme GUID as a hidden alias proxy, so the change doesn't itself break saved files. Also pin the sample plugins' components before the next release.
- **GUID lock for refactors** — the token benchmark's refactor task (`bench/tokens/RESULTS.md`) showed that renaming a parameter silently changes a Betta component's GUID and breaks saved `.gh` files. Without tooling, 0 of 15 agent refactors kept the GUID. With Betta.Mcp attached, 6 of 6 did, because `betta_describe` gives them the current value. The lock covers everyone else (no MCP client, humans, CI) and catches accidental changes at build time. Have the source generator record each method's current GUID in a checked-in lock file and report a diagnostic (with a code fix that pins the recorded value) when a signature change would change a locked GUID; expose the same value through Betta.Mcp (`betta_validate_signature` given the service type).
- **Feature showcase pipeline** — a private `betta-showcase` repo that regenerates an MP4 + GIF per feature on each release: an in-Rhino "Director" plugin drives Grasshopper from per-scene scripts, ffmpeg records the fixed-size Rhino window, gifski makes the GIFs, and Playwright checks the website embeds. Output feeds the README, website, nuget.org pages and Food4Rhino. Built, with four scenes scripted (hero, code vs canvas, named outputs, watch folder); the first recording waits on an in-Rhino spike at 200% display scaling.

## Later

- **In-Rhino integration tests** (testing brief Tier 3) — run the real solve pipeline on a runner with Rhino.
- **Hot-reload of replaced plugins** — true unload; today a replaced DLL makes Betta ask for a restart.
- **DI for plugins dropped in mid-session** — the service provider is built once at startup, so runtime-dropped plugins skip `IBettaModule`.
- **Canvas LoadLibrary component** — load a plugin DLL from the Grasshopper canvas.
- **Settings panel** — a real UI for Betta's options.
- **More packages on nuget.org** — `Betta.Generators` and `Betta.Docs` (dotnet tool).
- **Betta.Mcp on the official MCP C# SDK** — replace the hand-rolled JSON-RPC layer once the SDK covers the 2026-07-28 revision.
- **Pure-logic tests on CI** — about 50 tests (input coercion, enum inputs, param mapping) are tagged `Requires=Rhino` only because RhinoCommon/Grasshopper resolve from a Rhino install; loading them from the NuGet package on CI would bring them back.
