# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Betta** (assembly name) / **GH_AutoCreator** (repo name) is a Grasshopper plugin that auto-generates GH components from attributed service methods — Dynamo-style ZeroTouch for Grasshopper. Developers write plain C# services decorated with `[GrasshopperMethod]` / `[GrasshopperParameter]`; the runtime reflects over them, builds inputs/outputs, handles type coercion, and publishes them as Grasshopper components with no manual `GH_Component` subclasses required.

Tagline (from the brand brief): *"Same silhouette. Every fish is its own. Runtime adaptation for Grasshopper."* Implemented as: each component gets one of **six embedded betta silhouettes** (the Mini PNGs in `media/`, embedded into `Betta.gha` via `LogicalName` resources) picked deterministically from its descriptor GUID. The six silhouettes share the same pose (fan tail left, head right) but were each painted with their own colors and gradients — the visual variety comes from the artist, not from procedural tinting.

Icon render path (in `Betta/Services/IconProvider.cs`):
1. **At first request**, all six fish are rendered once and cached in a `Lazy<Bitmap[]>` field — `PickRendered(d)` then returns `_rendered.Value[d.Guid.ToByteArray()[0] % icons.Length]`. Multiple components landing on the same fish share the same `Bitmap` instance via `descriptor.CachedIcon`. The pick is deterministic per component, stable across sessions/machines because `descriptor.Guid` is MD5 over the method signature.
2. **Per-fish rendering**: `CropToContent(source)` finds the non-transparent bounding box and crops to it (raw PNGs ship with transparent padding around the fish; cropping is what gives the final 24×24 enough pixels to look painted rather than smushed). Then `LetterboxResample` does a two-step bicubic downscale (interim ~64px on the longer edge → final 24×24, inset by a `Padding` margin) **preserving aspect ratio** — the cropped fish is centered in the tile with transparent letterbox bars, never stretched.
3. **No tinting / recoloring.** The hand-drawn source PNGs already carry rich gradients and detail; per-pixel recolor was tried and obliterated the artist's work, so it's gone. If more variety is wanted, drop more PNGs into `media/`.
4. Output is **24×24** — the Grasshopper standard. The Mini source PNGs use bold outlines and solid fills that stay legible at this size, so there's no benefit to shipping a larger bitmap and letting GH downscale. 48×48 was tried and overlapped the parameter labels in compact components; 96×96 was even worse.

Caching: each `ComponentDescriptor.CachedIcon` holds its own rendered `Bitmap` (the downsampled source, no further mutation). Plugin-supplied PNGs via `[GrasshopperMethod(IconResource = "...")]` skip the fish system entirely; they are loaded verbatim from the plugin's own assembly.

The fish PNGs live in `media/` at the repo root. The active set is the **Mini** variants (`NBTabBettaMini_*.png`) — bold, cartoon-style silhouettes with thick outlines, designed to scale cleanly to icon sizes. They're wired in via `<EmbeddedResource Include="..\media\NBTabBettaMini_*.png" LogicalName="Betta.Resources.Fish_*.png" />` in `Betta.csproj`. The legacy painted bettas (`NBTabBetta_*.png`, ~250×250 detailed line art) stay in `media/` as artwork but are not currently embedded.

`SessionFish` exposes `IReadOnlyList<Bitmap> All`, `IReadOnlyList<string> Names` (`Amber`, `Aqua`, `Cosmic`, `Forest`, `Lime`, `Rose`), and `int Count` — the bitmaps load lazily on first access via `Lazy<Bitmap[]>`. The startup log line is `Fish library loaded: 6 silhouettes (Amber, Aqua, Cosmic, Forest, Lime, Rose)`. `SessionMorph` and `BettaPalette` exist but are no longer load-bearing — parked for future surfaces (settings panel, doc badges) where flat color won't compete with the source art.

Target: **Rhino 8, .NET 7 (`net7.0-windows`), C# 11**.

End-user facing docs live in [README.md](README.md); the maintainer-facing tiered roadmap + brainstorm is [ROADMAP.md](ROADMAP.md).

## Solution layout

Three projects + one test project:

- `Betta.Abstractions/` — the **public SDK contract** (`netstandard2.0`, no dependencies). Contains only the attributes and `IBettaCollection` marker. Plugin authors reference this package with `ExcludeAssets="runtime"`; never redistribute it.
- `Betta/Betta.csproj` — the runtime `.gha` (`net7.0-windows`, `UseWpf=true`). References Abstractions.
- `Betta.Strings/Betta.Strings.csproj` — sample plugin demonstrating the terse attribute pattern; builds to a DLL copied into `%AppData%\Grasshopper\Libraries\Betta\` (the plugin folder, not the root Libraries folder).
- `TestBetta/TestBetta.csproj` — xUnit tests using `Rhino.Inside` for headless Grasshopper. Must run as x64.

Solution file is still at the (legacy) path `Betta/Betta.sln` — the solution file includes all four projects.

## NuGet packaging (Betta.Abstractions)

`Betta.Abstractions` is the only project configured for NuGet publication. Metadata + readme + icon + symbol package are wired up in [Betta.Abstractions/Betta.Abstractions.csproj](Betta.Abstractions/Betta.Abstractions.csproj). `GeneratePackageOnBuild` is **off** — packaging is opt-in:

```bash
dotnet pack Betta.Abstractions/Betta.Abstractions.csproj -c Release
```

Output: `./artifacts/Betta.Abstractions.<Version>.nupkg` + matching `.snupkg`. The artifacts folder is gitignored. Publish with `dotnet nuget push`. Bump `<Version>` in the csproj before each release. The package's icon is `media/NBTabBettaMini_Cosmic.png` (mapped via `<None Include=... Pack="true" PackagePath="icon.png" />`); the package readme is the local `Betta.Abstractions/README.md` (separate from the repo root README).

Don't pack `Betta.gha` or `Betta.Strings` — they aren't designed as NuGet payloads (Betta.gha is a Grasshopper plugin, Betta.Strings is sample art).

## Build / Run / Test

```bash
dotnet build Betta.sln -c Debug
```

- `Betta.gha` deploys to `%AppData%\Grasshopper\Libraries\` via post-build xcopy. The xcopy is wrapped with `& exit /b 0` so a file-lock failure (Rhino holding the previous copy) does not fail the build — deployed copy just stays stale until Rhino closes.
- `Betta.Strings.dll` deploys to `%AppData%\Grasshopper\Libraries\Betta\` — the plugin subfolder that `PluginLoader` scans.
- Debug launches Rhino 8 as `StartProgram` (path hardcoded to `C:\Program Files\Rhino 8\System\Rhino.exe`).
- Tests must run x64 (`RuntimeIdentifier=win-x64` in TestBetta.csproj) because of `Rhino.Inside`. `xunit.runner.json` disables AppDomains and parallelization — required for Rhino to load exactly once.

**Known issue**: `dotnet test` hits a `Failed to create CoreCLR` startup error on Windows, appears to be a vstest/testhost packaging issue on this machine. Tests pass via VS Test Explorer. Not a migration blocker.

## Architecture

Pipeline: **Attributed service interface → `ComponentRegistry` discovers descriptors → `BettaComponentProxy` per descriptor → `Instances.ComponentServer.AddProxy(...)` publishes to GH → on canvas drop, proxy builds a `BettaComponent(descriptor)` → `ParamInjector` reflects inputs, invokes method, dispatches outputs**.

### The zero-touch generation mechanism

Grasshopper requires one `IGH_DocumentObject` type per toolbar entry, so most plugins write one `GH_Component` subclass per component. Betta dodges that by registering per-method **`IGH_ObjectProxy`** instances — all backed by the single generic `BettaComponent` CLR type — where each proxy carries its method's identity (name, GUID, category, icon). This is how Speckle and Hops do dynamic registration too.

### Key files

**Abstractions (public SDK surface)**:
- [Betta.Abstractions/Attributes/GrasshopperCollectionAttribute.cs](Betta.Abstractions/Attributes/GrasshopperCollectionAttribute.cs) — type-level Category/SubCategory defaults so method attributes can stay terse.
- [Betta.Abstractions/Attributes/GrasshopperMethodAttribute.cs](Betta.Abstractions/Attributes/GrasshopperMethodAttribute.cs) — marks a method as a component. Single-arg ctor (`[GrasshopperMethod("Upper")]`) is the terse form.
- [Betta.Abstractions/Attributes/GrasshopperParameterAttribute.cs](Betta.Abstractions/Attributes/GrasshopperParameterAttribute.cs) — customizes input display name/nickname/description. **No `Access` enum** — list access is inferred from the CLR type (`List<T>` → list, else → item).
- [Betta.Abstractions/Interfaces/IBettaCollection.cs](Betta.Abstractions/Interfaces/IBettaCollection.cs) — marker interface. `ComponentRegistry` scans **both** interfaces inheriting this (contract-first style) **and** concrete classes implementing it with `[GrasshopperMethod]` on their own public methods (terse, no-interface style). Either way the marker is the deliberate opt-in, so attribute-only types in unrelated DLLs aren't accidentally published. When a class implements a marked interface, the interface wins (the class isn't scanned again) so methods never double-register.

**Runtime**:
- [Startup.cs](Betta/Startup.cs) — `GH_AssemblyPriority.PriorityLoad()` runs once at GH load. Discovers own assembly, scans plugin folder via `PluginLoader.LoadExisting()`, auto-registers services in DI, builds the ServiceProvider, publishes proxies, then hands `PluginLoader` a DI-provided logger and kicks off the watcher for runtime plugin drops.
- [Services/ComponentRegistry.cs](Betta/Services/ComponentRegistry.cs) — `DiscoverFromAssembly` returns only newly-added descriptors (so runtime plugin loads can publish just their own). `RegisterDescriptor` is idempotent (GUID HashSet).
- [Services/ComponentDescriptor.cs](Betta/Services/ComponentDescriptor.cs) — `GenerateGuidFromMethod` uses **MD5** over `ServiceType.FullName | Method.Name | parameter signature | ReturnType`. Deterministic across .NET Framework and .NET 7; the old `string.GetHashCode()` path broke on the Rhino 8 migration and is gone.
- [Services/PluginLoader.cs](Betta/Services/PluginLoader.cs) — scans `%AppData%\Grasshopper\Libraries\Betta\`, loads each DLL via **`Assembly.Load(byte[])`** (not `LoadFrom`) so the DLL on disk isn't locked while Rhino runs. `FileSystemWatcher` hot-adds new drops on the UI thread (via `Rhino.RhinoApp.InvokeOnUiThread`) and calls `GH_ComponentServer.UpdateRibbonUI()` to refresh the toolbar. No unload support (deliberate — see ROADMAP.md).
- [Services/DescriptorCache.cs](Betta/Services/DescriptorCache.cs) — global Guid→Descriptor map populated at registration, used by anyone needing the descriptor post-hoc.
- [Services/FileLoggerProvider.cs](Betta/Services/FileLoggerProvider.cs) — `ILoggerProvider` that appends to `%AppData%\Grasshopper\Libraries\Betta.log`. Prefixed, structured-logging-friendly (`{Name}` placeholders). Registered via `services.AddLogging(b => b.AddProvider(new FileLoggerProvider()))`.
- [Components/BettaComponentProxy.cs](Betta/Components/BettaComponentProxy.cs) — implements `IGH_ObjectProxy` **directly** (Grasshopper 8 dropped the concrete `GH_ObjectProxy` base class). `CreateInstance()` sets `BettaComponent.Pending` then calls `new BettaComponent()`.
- [Components/BettaComponent.cs](Betta/Components/BettaComponent.cs) — **`[ThreadStatic] Pending` slot** is load-bearing: the base `GH_Component` ctor calls `RegisterInputParams`/`RegisterOutputParams` before the derived ctor body sets `_descriptor`, so those overrides must read `Pending`. Service + `ILogger<BettaComponent>` resolved from `Startup.ServiceProvider`.
- [ParamInjector.cs](Betta/ParamInjector.cs) — runtime workhorse. Input/output generation, expando-based per-iteration arg bag, `ChangeTypeStrong` coercion, output dispatch. **Expando keys are the CLR parameter name (`p.Name`)** — not NickName — because `BuildArguments` looks them up that way. `UnwrapGoo` uses `(IGH_Goo).ScriptVariable()` (interface call, not `dynamic`) to avoid `RuntimeBinderException` on .NET 7. Tuple element order is looked up by `Item1..Item8` name, not `GetProperties()` order.
- [ParamVector.cs](Betta/ParamVector.cs) — maps CLR types to `IGH_Param` subclasses (`Param_Point`, `Param_Circle`, `Param_Number`, ...). Reads `[GrasshopperParameter]` for input display metadata. `GetOutputs(type, method)` builds output params by mapping the plans from `OutputPlanner` to `IGH_Param`s.
- [OutputPlanner.cs](Betta/OutputPlanner.cs) — **GH-free** (no Grasshopper types), so it's unit-testable headlessly. `PlanOutputs(type, method)` resolves each output's name/nickname/description/type/access. Output-name priority: `[GrasshopperOutput]` (method or `[return:]`, `Index`-keyed for tuples) → ValueTuple element names (`(double Sum, double Average)` → `Sum`/`Average`) → defaults (`Output`/`Output1..N`/property name). The `GrasshopperOutputAttribute` was a stub until this was wired; now it's live.
- [Interfaces/ISimpleService.cs](Betta/Interfaces/ISimpleService.cs) + [Services/SimpleService.cs](Betta/Services/SimpleService.cs) — **sample** Math/Geometry service. Stays in the main assembly because it's demo content, not plugin surface.

**Sample plugin**:
- [Betta.Strings/IStringCollection.cs](Betta.Strings/IStringCollection.cs) — demonstrates the terse pattern: `[GrasshopperCollection("Strings", "Text")]` + `[GrasshopperMethod("Upper")]` + `[GrasshopperParameter("Text")]`.
- [Betta.Strings/StringCollection.cs](Betta.Strings/StringCollection.cs) — impl.

### Rules from `PROJECT_RULES.md`

Still applicable after the Rhino 8 migration:
- Services must be framework-agnostic. **No Grasshopper types in `Services/` or `Interfaces/`**. `Rhino.Geometry` types are OK.
- **Never `new BettaComponent()` from `PriorityLoad`**. Instantiation goes through proxies.
- **No static state for per-instance data.** `ParamInjector` is per-instance. The `[ThreadStatic] Pending` slot is scoped to a single constructor call, always cleared in `finally`.
- Use `DA.SetData` / `DA.SetDataList`; never touch `VolatileData` directly for outputs.
- No string-based type checking — use `typeof(T)` / `Type` comparisons.

### Non-obvious facts worth remembering

- `Instances.ComponentServer.AddProxy(...)` is the **only** registration API — `GH_ObjectProxy` no longer exists in Grasshopper 8, so `BettaComponentProxy` implements `IGH_ObjectProxy` by hand.
- `GH_ComponentServer.UpdateRibbonUI()` (static) refreshes the toolbar after runtime AddProxy. Not `RebuildObjectIndex` (doesn't exist) or `DocumentEditor.RefreshRibbon` (also doesn't exist).
- The `Grasshopper` NuGet is pinned to `[8.0, 8.27)` because Rhino 8 installs up to 8.26.x on user machines and a forward-incompatible build triggers GH's SDK mismatch breakpoint. `System.Drawing.Common 7.0.0` is an explicit dep because GH 8.0 assemblies forward `Bitmap` to that package.
- `.vs/` and `bin/`, `obj/` are git-ignored via the standard `.gitignore` at the repo root.
- `nuget.config` at the user level is **manually managed**. Don't edit it (see global rule).

## Testing notes

- [TestBetta/GrasshopperFixture.cs](TestBetta/GrasshopperFixture.cs) sets up headless Rhino via `RhinoInside.Resolver.Initialize()` + `GH_RhinoScriptInterface.RunHeadless()`. **The resolver must be initialized in a static constructor** before any Rhino assembly loads — do not move that call.
- `TestParamInjector.cs` has the pure-reflection tests (descriptor discovery, deterministic GUID generation, MD5 hash format stability, DI registration, `AutoRegisterServices`). These don't need Rhino and should be the primary regression surface.
- `TestClassDirectDiscovery.cs` covers the class-direct authoring style (attributes on a concrete class, no interface) and the "interface wins" dedup rule. Pure reflection.
- `TestOutputNaming.cs` covers output naming via `OutputPlanner.PlanOutputs` — `[GrasshopperOutput]`, ValueTuple element names, and defaults. GH-free, so it runs without Rhino.
- GH-document-based tests (`TestBettaComponent.cs`, `TestDifferentPrimitives.cs`) exist as templates but need their `.gh` files re-saved against Rhino 8 before they'll pass.
- `Rhino.Inside` is pinned to `7.0.0` because no stable 8.x exists on nuget.org. The 7.0.0 package's resolver finds whichever Rhino is installed, so this still works against Rhino 8.

## Gotchas to internalize

- **`_descriptor` is null during base `GH_Component` ctor.** Always read `_descriptor ?? Pending` in any method that might fire from the base constructor (`RegisterInputParams`, `RegisterOutputParams`, `ComponentGuid`).
- **Expando bag keys = CLR parameter names** (`p.Name`), not display names. Keyed off `input.NickName` is a bug that returns 0 for every argument.
- **Tuple elements must be looked up by `Item{N}` name**, not via `GetProperties()`/`GetFields()` order. Reflection order is not guaranteed to match declaration order.
- **Plugin types must inherit `IBettaCollection`** (on the interface *or* the class) or they'll be silently skipped by `ComponentRegistry.DiscoverFromAssembly`. The filter is deliberate — `[GrasshopperMethod]` alone in a random DLL isn't enough to opt in. Class-direct discovery requires the attribute on the class's **own public methods** (interface-method attributes don't propagate to the implementing class method).
- **Byte-stream-loaded plugins don't auto-resolve siblings.** `PluginLoader.ResolveSiblingDependency` is subscribed to `AppDomain.AssemblyResolve` and probes the plugin folder — a plugin that ships its own deeper tree should bundle deps in the same folder.
