# Betta roadmap

What's planned after the current release, roughly in priority order. Shipped work is in [CHANGELOG.md](CHANGELOG.md).

## Next

- **Headless contract test in CI** (testing brief Tier 2) — load the sample plugins through `Betta.Mcp`'s `MetadataLoadContext` loader and assert the expected component set, signatures and zero load errors on every push.
- **Yak from CI** — build the `.yak` on the runner with the downloaded `yak.exe` and push it when a `YAK_TOKEN` secret is set, so one tag publishes to both nuget.org and the Rhino Package Manager.
- **Runtime target** — Betta targets `net7.0-windows`, which Microsoft no longer supports. Move to the runtime current Rhino 8 ships, and lift the Grasshopper `[8.0, 8.27)` pin as Rhino 8 updates.
- **Rhino 8 test fixtures** — re-save the `.gh` test fixtures (and `samples/betta_showcase.gh`) in Rhino 8.
- **Betta.Generators on nuget.org** — plugin authors get `betta.lock`, its quick fix and the faster load only from the package; add it to `release.yml`.
- **Feature showcase pipeline** — a private `betta-showcase` repo that regenerates an MP4 + GIF per feature on each release: an in-Rhino "Director" plugin drives Grasshopper from per-scene scripts, ffmpeg records the fixed-size Rhino window, gifski makes the GIFs, and Playwright checks the website embeds. Output feeds the README, website, nuget.org pages and Food4Rhino. Built, with four scenes scripted (hero, code vs canvas, named outputs, watch folder); the first recording waits on an in-Rhino spike at 200% display scaling.

## Later

- **In-Rhino integration tests** (testing brief Tier 3) — run the real solve pipeline on a runner with Rhino.
- **Hot-reload of replaced plugins** — true unload; today a replaced DLL makes Betta ask for a restart.
- **`IBettaModule` for plugins dropped in mid-session** — their services are created (constructor injection from the startup provider), but the provider is built once, so their own `IBettaModule` registrations wait for a Rhino restart.
- **Canvas LoadLibrary component** — load a plugin DLL from the Grasshopper canvas.
- **Settings panel** — a real UI for Betta's options.
- **More packages on nuget.org** — `Betta.Docs` (dotnet tool).
- **Betta.Mcp on the official MCP C# SDK** — replace the hand-rolled JSON-RPC layer once the SDK covers the 2026-07-28 revision.
- **Pure-logic tests on CI** — about 50 tests (input coercion, enum inputs, param mapping) are tagged `Requires=Rhino` only because RhinoCommon/Grasshopper resolve from a Rhino install; loading them from the NuGet package on CI would bring them back.
