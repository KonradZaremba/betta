# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Betta** (assembly name) / **GH_AutoCreator** (repo name) is a Grasshopper plugin that auto-generates GH components from attributed service methods — Dynamo-style ZeroTouch for Grasshopper. Developers write plain C# services decorated with `[GrasshopperMethod]` / `[GrasshopperParameter]`; the runtime reflects over them, builds inputs/outputs, handles type coercion, and publishes them as Grasshopper components with no manual `GH_Component` subclasses required.

Component icons: each component gets one of the six embedded betta silhouettes in `media/` (`NBTabBettaMini_*.png`, rendered by `Betta/Services/IconProvider.cs`), picked deterministically from its descriptor GUID. Two decisions not to undo:
- **No tinting / recoloring.** The hand-drawn source PNGs already carry rich gradients and detail; per-pixel recolor was tried and obliterated the artist's work, so it's gone. If more variety is wanted, drop more PNGs into `media/`.
- Output is **24×24** — the Grasshopper standard. The Mini source PNGs use bold outlines and solid fills that stay legible at this size, so there's no benefit to shipping a larger bitmap and letting GH downscale. 48×48 was tried and overlapped the parameter labels in compact components; 96×96 was even worse.

Target: **Rhino 8, .NET 7 (`net7.0-windows`), C# 11**.

End-user facing docs live in [README.md](README.md); release history in [CHANGELOG.md](CHANGELOG.md); design notes in [docs/](docs/) (e.g. `betta-guids-and-persistence.md`, `testing-brief.md`). Plugin-author guidance (attribute set, output mapping, GUID stability) is in the `betta-plugin` skill.

## Solution layout

The solution file is at the legacy path `Betta/Betta.sln` (inside the runtime project's folder). Non-obvious points:

- `Betta.Abstractions/` — the **public SDK contract** (`netstandard2.0`). Its only package dependency is `Microsoft.Extensions.DependencyInjection.Abstractions`. Plugin authors reference this package with `ExcludeAssets="runtime"`; never redistribute it.
- `Betta/Betta.csproj` — the runtime. Compiles to `Betta.dll`; `Betta.Build.targets` produces `Betta.gha` (see Build below). `UseWindowsForms` is required for the `ToolStripDropDown` in the menu-state UI.
- `Betta.Generators/` — optional Roslyn source generator. Emits `Betta.Generated.BettaPluginBootstrap`, which `Startup` / `ComponentDescriptorBlueprint` use as a fast-path descriptor manifest instead of the reflection scan; also reports `BETTA001`/`BETTA011` diagnostics. The reflection path must keep working without it. It also checks `betta.lock` ([GuidLock.cs](Betta.Generators/GuidLock.cs), `BETTA012`), but a generator can't write files: it lists unlocked components in the generated `BettaLockPending.g.cs`, and [build/Betta.Generators.targets](Betta.Generators/build/Betta.Generators.targets) (packed under `build/`; OpaqueDemo imports it by hand) reads that from obj after `CoreCompile` and appends to the lock, which is why the props turn on `EmitCompilerGeneratedFiles`. The lock's rules (format, which GUIDs answer, rename hints) live in [GuidLockCore.cs](Betta.Generators/GuidLockCore.cs), Roslyn-free and compiled into Betta.Mcp too as a linked file (`betta_check_lock`). The quick fix is `Betta.Generators.CodeFixes/`, a separate assembly because it needs Roslyn Workspaces, which csc doesn't load; the generator's package ships both. BETTA012 tells it what to insert through the `BettaLockFix`/`BettaLockValue` diagnostic properties.
- `Betta.Mcp/` (outside the solution) — stdio MCP server that recomputes descriptor GUIDs with the runtime's own scheme (`GuidScheme.cs`, linked in). Speaks MCP 2026-07-28 and the legacy `initialize` revisions side by side. Tests: `Betta.Mcp.Tests/` (net8.0, run by CI).
- `Betta.Docs/` (outside the solution) — the `betta-docs` tool, reading plugins with MetadataLoadContext. It keeps its own copies of the runtime's pin rules (`ParamInjector.GenerateInputs`), output planning (`OutputPlanner`, incl. `SplitWords`) and list access (`ParamVector.GetParamAccessType`): change them together. Tests: `Betta.Docs.Tests/` (net8.0, run by CI; they fake a Rhino install via `BETTA_RHINO_SYSTEM_FOLDER`).
- `Betta.Windows/` contains only stale, git-ignored `bin/`/`obj/`. It is not a project.
- Licensing ships as **Betta.Pro** in a separate repo; this repo has only the hooks (`IBettaLicenseGate`, `[GrasshopperRequiresEntitlement]`).

Packaging and releases (version bumps, `dotnet pack`, Yak, the `v*` tag workflow) are in the `betta-release` skill. Two rules apply everywhere: the version is set only in the repo-root `Directory.Build.props`, never in a csproj; and never pack `Betta.gha` or `Betta.Strings`.

## Build / Run / Test

```bash
dotnet build Betta/Betta.sln -c Debug
dotnet test TestBetta/TestBetta.csproj
dotnet test TestBetta/TestBetta.csproj --filter "FullyQualifiedName~TestOutputNaming"   # one class; use ~Class.Method for one test
```

- `Betta.gha` deploys to `%AppData%\Grasshopper\Libraries\` via post-build xcopy. The xcopy is wrapped with `& exit /b 0` so a file-lock failure (Rhino holding the previous copy) does not fail the build — deployed copy just stays stale until Rhino closes.
- `Betta.Strings.dll` deploys to `%AppData%\Grasshopper\Libraries\Betta\` — the plugin subfolder that `PluginLoader` scans.
- Debug launches Rhino 8 as `StartProgram` (path hardcoded to `C:\Program Files\Rhino 8\System\Rhino.exe`).
- Tests must run x64 (`RuntimeIdentifier=win-x64` in TestBetta.csproj) because of `Rhino.Inside`. `xunit.runner.json` disables AppDomains and parallelization — required for Rhino to load exactly once.

**`dotnet test` works.** It used to abort with `Failed to create CoreCLR, HRESULT: 0x80070057`; this was *not* a vstest/testhost packaging quirk. `Betta.csproj` set `<TargetExt>.gha</TargetExt>`, so the assembly was written into every referencing project's `deps.json` as `"Betta.gha"`, and `hostpolicy` rejects a non-`.dll` entry in `TRUSTED_PLATFORM_ASSEMBLIES` (`E_INVALIDARG`). Betta now compiles to `Betta.dll` and `_BettaWriteGha` (in `Betta.Build.targets`, alongside the Libraries deploy and yak packaging — the shared `BettaRuntimePayload` item is the single definition of what ships) copies it to `Betta.gha` — never revert to emitting `.gha` from the compiler, or the whole suite goes dark again. `Betta.dll` is deliberately excluded from the Libraries deploy and the yak payload: same assembly identity as `Betta.gha`, so shipping both double-loads the plugin.

**6 fixture-based tests fail by design** (`TestBettaComponent`, `TestDifferentPrimitives`, `TestPrimitives`). Read [TestBetta/CLAUDE.md](TestBetta/CLAUDE.md) before investigating any test failure.

## Architecture

Pipeline: **Attributed service interface → `ComponentRegistry` discovers descriptors → `BettaComponentProxy` per descriptor → `Instances.ComponentServer.AddProxy(...)` publishes to GH → on canvas drop, proxy builds a `BettaComponent(descriptor)` → `ParamInjector` reflects inputs, invokes method, dispatches outputs**.

### The zero-touch generation mechanism

Grasshopper requires one `IGH_DocumentObject` type per toolbar entry, so most plugins write one `GH_Component` subclass per component. Betta dodges that by registering per-method **`IGH_ObjectProxy`** instances — all backed by the single generic `BettaComponent` CLR type — where each proxy carries its method's identity (name, GUID, category, icon). This is how Speckle and Hops do dynamic registration too.

### Key files

**Abstractions (public SDK surface)**:
- [GrasshopperParameterAttribute.cs](Betta.Abstractions/Attributes/GrasshopperParameterAttribute.cs) — **No `Access` enum** — list access is inferred from the CLR type (`List<T>` → list, else → item).
- [GrasshopperOpaqueAttribute.cs](Betta.Abstractions/Attributes/GrasshopperOpaqueAttribute.cs) + [IBettaValue.cs](Betta.Abstractions/Interfaces/IBettaValue.cs) — opt out of "return-type explosion." When a method returns a custom class marked opaque (class/interface attribute, marker, or `[return: GrasshopperOpaque]`), Betta auto-generates a `GH_BettaGoo<T>` + `Param_BettaGoo<T>` pair (deterministic ComponentGuid = MD5 of `T.FullName`) and ships the value as a single typed wire. `List<T>` of opaque also flows as one list output. [OpaqueDetection.cs](Betta.Abstractions/Attributes/OpaqueDetection.cs) decides opaqueness without touching Grasshopper types, so `OutputPlanner` and `ComponentRegistry` can share it.
- [IBettaModule.cs](Betta.Abstractions/Interfaces/IBettaModule.cs) — optional `ConfigureServices(IServiceCollection)` hook, invoked by `Startup.PriorityLoad` *before* `BuildServiceProvider`. **Limitation:** the ServiceProvider is built once, so a plugin dropped in mid-session has no binding; [LateServices](Betta/Services/LateServices.cs) creates its implementation (constructor injection from the startup provider, singleton, evicted on hot reload), but its `IBettaModule` never runs until Rhino restarts.

**Runtime**:
- [Services/ComponentDescriptor.cs](Betta/Services/ComponentDescriptor.cs) — `GenerateGuidFromMethod` uses **MD5** over `ServiceType.FullName | Method.Name` only, so parameters and outputs can change without breaking saved files (GH re-binds params by position, like a hand-written `GH_Component` with a fixed `ComponentGuid`). Overloaded `[GrasshopperMethod]` names append `| parameter signature | ReturnType` with version-free type names (`StableTypeName`: ``List`1[System.Double]``). `[GrasshopperMethod(Guid = …)]` pins one; `ResolveGuid` is the one place that decides, for both the reflection and the source-generator path. Never hash `Type.FullName` of a parameter again: for generics it assembly-qualifies type arguments, so pre-0.8 GUIDs changed with the plugin's own version, every RhinoCommon SR and .NET (Betta.Files' unchanged `Watch Folder` had three). The pre-0.8 whole-signature value is kept as `LegacyGuid`, and `FormerNames` on `[GrasshopperMethod]`/`[GrasshopperCollection]` give `FormerGuids` (the `Type|Method` hashes of earlier names; an overload listing its own name gets back the name-only GUID it had before the overload); each is registered as a hidden alias proxy (`AliasGuids`), whose instance carries the current GUID so files migrate on save — GH's `EmitObject` never checks the GUID of what a proxy creates. `BettaComponent.Read` sets aside params a file has no chunk for (method gained inputs/outputs), else GH logs "Archive is corrupt"; `TestFileCompatibility` covers both headlessly. The formula exists once, in [GuidScheme.cs](Betta.Generators/GuidScheme.cs): Roslyn-free, compiled into the generator and linked into Betta and Betta.Mcp. Only the generator's type-to-name step is its own (`StableFullName(ITypeSymbol)`), and the overload signature starts with `Type.FullName`, not `StableTypeName`, as it always has. `TestGuidScheme` pins the values as literals; `TestGeneratorStableGuids` checks the generator against the runtime.
- [Services/PluginLoader.cs](Betta/Services/PluginLoader.cs) — scans `%AppData%\Grasshopper\Libraries\Betta\`, loads each DLL via **`Assembly.Load(byte[])`** (not `LoadFrom`) so the DLL on disk isn't locked while Rhino runs. `FileSystemWatcher` hot-adds new drops on the UI thread (via `Rhino.RhinoApp.InvokeOnUiThread`) and calls `GH_ComponentServer.UpdateRibbonUI()` to refresh the toolbar. No unload support (deliberate).
- Logs go to `%AppData%\Grasshopper\Libraries\Betta.log` ([Services/FileLoggerProvider.cs](Betta/Services/FileLoggerProvider.cs)), Debug level, a line per solve. Above 10 MB it rolls to `Betta.log.old`; append failures are swallowed so logging never fails a solve.
- [Components/BettaComponentProxy.cs](Betta/Components/BettaComponentProxy.cs) — implements `IGH_ObjectProxy` **directly** (Grasshopper 8 dropped the concrete `GH_ObjectProxy` base class). `CreateInstance()` sets `BettaComponent.Pending` then calls `new BettaComponent()`.
- [ParamInjector.cs](Betta/ParamInjector.cs) — runtime workhorse (input/output generation, per-iteration arg bag, `ChangeTypeStrong` coercion, output dispatch). `UnwrapGoo` uses `(IGH_Goo).ScriptVariable()` (interface call, not `dynamic`) to avoid `RuntimeBinderException` on .NET 7.
- [OutputPlanner.cs](Betta/OutputPlanner.cs) — **GH-free** (no Grasshopper types), so it's unit-testable headlessly. Output-name priority: `[GrasshopperOutput]` (method or `[return:]`, `Index`-keyed for tuples) → ValueTuple element names (`(double Sum, double Average)` → `Sum`/`Average`) → defaults (`Output`/`Output1..N`/property name). Element and property names go through `SplitWords` (`FlatCount` → "Flat Count"); outputs dispatch by position, never by name.
- [Interfaces/ISimpleService.cs](Betta/Interfaces/ISimpleService.cs) + [Services/SimpleService.cs](Betta/Services/SimpleService.cs) — **sample** Math/Geometry service. Stays in the main assembly because it's demo content, not plugin surface.
- [Goo/GH_BettaGoo.cs](Betta/Goo/GH_BettaGoo.cs) + [Goo/Param_BettaGoo.cs](Betta/Goo/Param_BettaGoo.cs) — generic IGH_Goo/IGH_Param pair for opaque domain values. Closed generics produce stable per-T ComponentGuids so `.gh` files round-trip across machines.

**Preview (optional companion package)**:
- [Betta.Preview/Interfaces/IBettaPreview.cs](Betta.Preview/Interfaces/IBettaPreview.cs) — opaque domain types that implement this draw in the Rhino viewport. Detected at discovery via interface-name string (`"Betta.Preview.IBettaPreview"`) — a deliberate exception to the no-string-type-checks rule — so the Betta runtime does NOT take a hard reference to the Betta.Preview package and plugins that don't need preview don't drag a transitive RhinoCommon ref through their consumers. It still ships `Betta.Preview.dll` (plugins reference it with `ExcludeAssets="runtime"`): a build-order-only `ProjectReference` (`ReferenceOutputAssembly="false"`) feeds it into `BettaRuntimePayload`, and `PluginLoader.FindDependency` resolves `Betta.*` assemblies from Betta.gha's folder when the plugin folder has none.

### Project rules

- Services must be framework-agnostic. **No Grasshopper types in `Services/` or `Interfaces/`**. `Rhino.Geometry` types are OK.
- **Never `new BettaComponent()` from `PriorityLoad`**. Instantiation goes through proxies.
- **No static state for per-instance data.** `ParamInjector` is per-instance. The `[ThreadStatic] Pending` slot is scoped to a single constructor call, always cleared in `finally`.
- Use `DA.SetData` / `DA.SetDataList`; never touch `VolatileData` directly for outputs.
- No string-based type checking — use `typeof(T)` / `Type` comparisons.

### Non-obvious facts worth remembering

- `Instances.ComponentServer.AddProxy(...)` is the **only** registration API — `GH_ObjectProxy` no longer exists in Grasshopper 8, so `BettaComponentProxy` implements `IGH_ObjectProxy` by hand.
- `GH_ComponentServer.UpdateRibbonUI()` (static) refreshes the toolbar after runtime AddProxy. Not `RebuildObjectIndex` (doesn't exist) or `DocumentEditor.RefreshRibbon` (also doesn't exist).
- The `Grasshopper` NuGet is pinned to `[8.0, 8.27)` because Rhino 8 installs up to 8.26.x on user machines and a forward-incompatible build triggers GH's SDK mismatch breakpoint. `System.Drawing.Common 7.0.0` is an explicit dep because GH 8.0 assemblies forward `Bitmap` to that package.
- `nuget.config` at the user level is **manually managed**. Don't edit it (see global rule).

## Gotchas to internalize

- **`_descriptor` is null during base `GH_Component` ctor.** Always read `_descriptor ?? Pending` in any method that might fire from the base constructor (`RegisterInputParams`, `RegisterOutputParams`, `ComponentGuid`).
- **Expando bag keys = CLR parameter names** (`p.Name`), not display names. Keyed off `input.NickName` is a bug that returns 0 for every argument.
- **Tuple elements must be looked up by `Item{N}` name**, not via `GetProperties()`/`GetFields()` order. Reflection order is not guaranteed to match declaration order.
- **Plugin types must inherit `IBettaCollection`** (on the interface *or* the class) or they'll be silently skipped by `ComponentRegistry.DiscoverFromAssembly`. The filter is deliberate — `[GrasshopperMethod]` alone in a random DLL isn't enough to opt in. Class-direct discovery requires the attribute on the class's **own public methods** (interface-method attributes don't propagate to the implementing class method). When a class implements a marked interface, the interface wins (the class isn't scanned again) so methods never double-register.
- **Byte-stream-loaded plugins don't auto-resolve siblings.** `PluginLoader.ResolveSiblingDependency` is subscribed to `AppDomain.AssemblyResolve` and probes the plugin folder — a plugin that ships its own deeper tree should bundle deps in the same folder.
