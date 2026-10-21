# Changelog

All notable changes to Betta are documented here. Versions follow
[SemVer](https://semver.org); the format follows [Keep a Changelog](https://keepachangelog.com).

## [Unreleased]

### Added
- **Warnings from inside a method** — take an `IBettaReport` parameter (no input pin, like `CancellationToken`) and call `report.Warning(…)` or `report.Remark(…)`: the message shows on the component while its outputs are still set. Works for async and streaming methods. Throwing still gives an error with no outputs. The template agent guide and the skill describe both. In the token benchmark's bigger tasks this took agents from 2.5x to 1.06x the cost of a hand-written component, and from 0 of 6 to 6 of 6 showing the warning.
- **GUID pinning help in Betta.Mcp** — `betta_describe` says whether a component's GUID is pinned or derived, which assembly versions a derived GUID depends on, and lists every GUID Betta.log recorded for it, so you can pin the one your saved files use. `betta_validate_signature` returns `guidAdvice` for signatures whose derived GUID would be version-dependent.
- **Betta.Preview, Betta.Templates and Betta.Mcp on nuget.org**, published by the release workflow alongside Betta.Abstractions. Install the MCP server with `dotnet tool install --global Betta.Mcp`.
- **Betta.Mcp speaks MCP 2026-07-28** (stateless, `server/discover`) and still serves clients that use the older `initialize` handshake. Tools are marked read-only.
- **IntelliSense docs** — Betta.Abstractions now ships its XML documentation.
- **CI** — every push and PR builds the solution and runs the headless test suite.
- **Agent guide in the `dotnet new` templates** — new plugin projects get a short `CLAUDE.md` + `AGENTS.md` with the Betta facts coding agents most often get wrong (async, streaming, named outputs, GUID stability on refactors). Measured in `bench/tokens` (run 7, seven tasks): about the cost of the hand-written `GH_Component` route (1.03x) with 0.64x the code, 0.64x the cost for five components at once. It points agents at Betta.Mcp's `betta_describe` for a component's current GUID; with the server attached, 9 of 9 benchmark refactors kept saved files working (0 of 18 without it).
- **Token benchmark** (`bench/tokens`) — headless Claude Code runs comparing Betta and plain `GH_Component` projects on cost, tokens, turns and code size.
- Headless tests for the async result cache (livelock + memoization), plugin-vs-dependency classification, and template version drift.

### Fixed
- `dotnet new bettaplugin-*` templates referenced Betta.Abstractions/Betta.Preview `0.4.0`, which was never published, so scaffolded plugins failed to restore.
- README listed tree inputs as unsupported; tree I/O has worked since the first release (0.3.1).
- `betta_validate_signature` rejected valid signatures using features from 0.5–0.7 (streaming `IObservable<T>`, `ValueTask`, `CancellationToken`/`IProgress<T>`, `params` arrays, parameter attributes). It now matches the runtime.
- Betta.Mcp answered `initialize` with whatever protocol version the client asked for, even ones it doesn't implement.
- Betta.Mcp decoded stdin with the Windows console code page instead of UTF-8, garbling any non-ASCII tool argument (paths, names).
- Betta.Mcp's `betta_describe` reported the signature-derived GUID even for a component that pins one with `[GrasshopperMethod(Guid = …)]`. Betta publishes the pinned value, so a GUID copied from the tool could break saved `.gh` files.
- `betta_describe` failed for every component when the plugin folder also held dependency DLLs (System.Text.Json, Azure.*, …), and for any signature with a Rhino type inside a generic (`List<Curve>`), because RhinoCommon wasn't on its resolver path.
- `betta_validate_signature` recognised geometry types only fully qualified: `Curve`, `Point3d`, `Brep` and the rest, as normally written, got a false note that they flow as untyped wires.
- The `betta-plugin` agent skill pointed authors at Betta.Abstractions 0.3.1 and missed streaming, the templates and Betta.Mcp.

### Changed
- **Pin the GUID of published components.** A derived GUID with a generic type in the signature (`List<T>`, tuples, `Task<T>`, `IObservable<T>`) changes with the plugin's own version, every Rhino update and the .NET version, and saved `.gh` files then lose the component. Betta.log shows Betta.Files' `Watch Folder` with a new GUID in each of 0.7.1, 0.7.2 and 0.7.2.1. README, the template agent guide and the skill now say to pin `[GrasshopperMethod(Guid = …)]`; a version-independent hash is on the roadmap.
- The async result cache moved out of `BettaComponent` into `AsyncResultCache` (no behavior change).
- The `betta-plugin` agent skill is split into a ~6k-char core plus reference files read on demand (loading it cost ~8.5k tokens per session before, ~2.5k now).

## [0.7.2.1] — 2026-08-19

### Changed
- **Republish of 0.7.2** — the `0.7.2` version slot was yanked from the Rhino
  Package Manager (Yak) and cannot be reused, so this release re-ships the same
  runtime under a fresh patch version. No functional changes over 0.7.2.
- Corrected the NuGet package `RepositoryUrl` / `PackageProjectUrl` metadata to
  point at `github.com/KonradZaremba/betta` (previously the legacy
  `GH_AutoCreator` repo).

## [0.7.2] — 2026-08-11

### Added
- **`Betta.Files` sample colony** — the first "watch-first" toolkit built entirely
  from attributed methods: a debounced live folder/file watcher (`IObservable`)
  plus read/write/split/find utilities. Split across `Files › Watch` and
  `Files › IO`. Showcases streaming, class-return explosion, async, validation
  and menu-state in one small downloadable collection.
- **Expanded headless test coverage** — input coercion, special-parameter
  detectors, plugin-trust policy, param-vector mapping, and the streaming ticker,
  plus discovery/output-shape/watcher tests for `Betta.Files`.

### Changed
- **Single-source versioning** — the release version now lives only in
  `Directory.Build.props`; individual project files no longer set `<Version>`.

### Fixed
- **Drop-in `.zip` payload** — the Food4Rhino drop-in zip now ships the full
  runtime payload (all `Microsoft.Extensions.*` dependencies), matching the `.yak`
  and the Libraries deploy. It previously shipped only `Betta.gha` +
  `Betta.Abstractions.dll`, which fails to load on a clean machine.

## [0.7.0] — 2026-07-22

### Added
- **`IObservable<T>` streaming return type.** A service method returning
  `IObservable<T>` becomes a live component: Betta subscribes on first solve
  (status shows *listening*), and every emission pushes the newest value
  through the output — re-solves are coalesced on the UI thread. Changing any
  wired input disposes the old subscription and opens a new one; subscriptions
  are cleaned up when the component leaves the canvas.
- **Multi-branch item/list input flattening** — branched item/list inputs are
  flattened automatically instead of producing per-branch mismatches.
- **55 new headless tests** (`ParamValidator`, `OutputPlanner` return-type
  planning, `IconProvider` fish pipeline — the latter verified by mutation
  testing). Suite: 95 of 101 green without Rhino.

### Fixed
- **Critical build/consumption bug:** Betta compiled directly to `Betta.gha`
  via `<TargetExt>`, which wrote `"Betta.gha"` into every referencing
  project's `deps.json`. The .NET host rejects a non-`.dll` entry in the
  trusted assembly list, so any consumer's test host died with
  `Failed to create CoreCLR, HRESULT: 0x80070057` before running a single
  test. Betta now compiles to `Betta.dll` and produces `Betta.gha` as a
  build-time copy — `dotnet test` works from the CLI for the first time.

### Changed
- The Grasshopper Libraries deploy and the yak/zip payloads exclude
  `Betta.dll` — it is the same assembly identity as `Betta.gha`, and shipping
  both made Grasshopper load the plugin twice.
- The deploy step is a proper MSBuild target (replacing the `xcopy`
  post-build event).

## [0.6.0] — 2026-07-06

### Added
- **Opt-in plugin DLL signing** — Authenticode verification before a plugin's
  bytes are loaded; publisher allowlist managed via the `Betta_Trust` command
  or **Betta → Trusted publishers…** (policy in `trust.json`, off by default).
- **Per-component secrets** — `[GrasshopperSecret("service.key")]` reads from
  Windows Credential Manager at solve time instead of creating an input pin.
  Managed via `Betta_Secrets` / **Betta → Secrets…**.
- **Manual-run trigger** — `[GrasshopperTrigger]` gates a component behind a
  right-click *Run*, for expensive or side-effecting methods.
- **Value-list auto-drop** — `[GrasshopperValueList("1:1", "16:9", …)]`
  auto-attaches a wired `GH_ValueList` to the input pin.
- **Bitmap as a first-class value** — `System.Drawing.Bitmap`/`Image` flow as
  single generic params instead of exploding into `Width`/`Height`/….
  `BettaImage` wrapper (in `Betta.Preview`) adds PNG serialization and
  bake-to-PictureFrame.
- **High-arity tuple outputs** — `TRest` nesting is walked recursively, so
  8+-element `ValueTuple`s flatten into individual outputs.
- **Licensing extension points** — `IBettaLicenseGate` +
  `[GrasshopperRequiresEntitlement]` (hooks only; inert unless a gate is
  registered via DI).
- `IBettaModule.ConfigureServices` failures are surfaced in the Rhino command
  line instead of failing silently.

### Breaking
- **Opaque property explosion**: components returning classes with opaque
  properties now emit typed outputs for those properties (and for
  `List<opaque>` properties). Saved `.gh` files may need re-wiring.

## [0.5.0] — 2026-06-16

### Added
- **Synthetic parameters** — `CancellationToken` (cancelled on re-solve) and
  `IProgress<T>` (drives the component status tag) are injected at solve time.
- **Menu-state parameters** — `[GrasshopperMenuState]` moves enum/bool
  settings into the component's right-click menu; state persists in the
  `.gh` file.
- **Variadic parameters** — `params T[]` / `T[]` arrive as list inputs.
- Param-type coverage: `BoundingBox`, `Curve`, `Interval`, `Transform`,
  `Color`, `Guid`; implicit upcasts (`Line`/`Arc`/`Circle`/`Polyline` →
  `Curve`, `Surface` → `Brep`).
- **Betta Inspector** component and **`Betta_Status`** Rhino command for
  toolbar introspection.
- **`Betta.Docs`** dotnet tool — emits markdown docs per collection without
  executing plugin code.
- Yak + drop-in-zip release packaging (`-p:BuildYak=true`).

## [0.4.0] / [0.4.1] — 2026-06-15/16

### Changed
- **Rhino 8 / .NET 7 migration.** Proxies implement `IGH_ObjectProxy`
  directly (Grasshopper 8 removed the concrete base class).
- **Deterministic component GUIDs** — MD5 over the full method signature
  replaces `string.GetHashCode()`, which changed values across runtimes and
  broke saved files during the migration.

## [0.3.1] — 2026-05-29

Initial public release: zero-touch component generation from attributed C#
service methods — attribute-driven discovery, DI, runtime plugin folder with
hot-add, type coercion, tuple outputs, opaque domain types, viewport preview
hook, tree I/O, param validation, async `Task<T>` support, and the six betta
fish icons.
