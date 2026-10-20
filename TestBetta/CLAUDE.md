# TestBetta

Notes for the test project. Running the suite (x64, `xunit.runner.json`, the `dotnet test` commands) is covered in the root [CLAUDE.md](../CLAUDE.md).

## Expected test failures: 6

The fixture-based tests (`TestBettaComponent`, `TestDifferentPrimitives`, `TestPrimitives`, 2 each). They construct a `RhinoCore` in-process and it fails with COM `E_FAIL`. Investigated in depth; **do not re-litigate these without reading this first**:

- **Root cause: the resolver picks the wrong Rhino.** `Rhino.Inside 7.0.0` defaults `Resolver.UseLatest = false`, so with both **Rhino 7 and Rhino 8 installed** it resolves `C:\Program Files\Rhino 7\System` — while everything is compiled against RhinoCommon/Grasshopper `8.0.23304.9001`. That mismatch is the `E_FAIL`. The old note blaming "a vstest quirk" / "a non-interactive session" was wrong: the shell runs in interactive session 1, and closing Rhino changes nothing.
- **`UseLatest = true` is NOT the fix.** It does resolve Rhino 8 and RhinoCore genuinely starts (~28s), but three more walls follow: Rhino 8 does not auto-load Grasshopper in-process (needs an explicit `PlugIn.LoadPlugIn(b45a29b1-4343-4035-989e-044e8580d9cf)`), `RhinoApp.GetPlugInObject("Grasshopper")` then returns a **`System.__ComObject`** rather than the managed `GH_RhinoScriptInterface` (so the cast in `GrasshopperFixture` yields null — it needs IDispatch late binding), and finally the in-process Rhino **crashes the test host**, which aborts the entire run (only ~32 of 101 tests report). Skipping the `[Fact]`s does not help — the collection fixture is constructed regardless. A fast clean `E_FAIL` costing 6 tests beats an aborted suite, so the default stands.
- **Consequence:** `RhinoResolverInit`'s probe currently loads `Grasshopper.dll` from **Rhino 7**. Tolerated because .NET Core does not enforce strict version binding and the API surface used is compatible.
- Their `.gh` files also still need re-saving against Rhino 8, so they may not pass even once Rhino starts.
- Real fix is likely `Rhino.Inside` 8.x — which **does not exist on nuget.org** (verified; 7.0.0 is the only published version, which is why it's pinned). Uninstalling Rhino 7 would also make `UseLatest=false` resolve Rhino 8, but the host-crash wall remains.

## Headless assembly resolution

Lives in **`GrasshopperSingleton.InitializeResolver()`** ([GrasshopperFixture.cs](GrasshopperFixture.cs)) — the single entry point that makes BOTH RhinoCommon (via `RhinoInside.Resolver`, from `<Rhino>\System`) and `Grasshopper.dll`/`GH_IO.dll` (via an `AssemblyResolve` probe of `<RhinoRoot>\Plug-ins\Grasshopper`; root derived from `Resolver.RhinoSystemDirectory`, never hardcoded) resolvable.

The resolver must be initialized before any Rhino assembly loads. [RhinoResolverInit.cs](RhinoResolverInit.cs) contributes only the **timing**: its `[ModuleInitializer]` calls `InitializeResolver()` before any test body; the fixtures' static ctors call it too. Never call `RhinoInside.Resolver.Initialize()` anywhere else. `GrasshopperFixture` then starts headless Grasshopper via `GH_RhinoScriptInterface.RunHeadless()`.

## ProjectReferences that look unused

`TestBetta` carries two ProjectReferences no test code uses directly — don't "clean up" either:
- `Betta.Preview`: `TestGeneratorBootstrap` loads the OpaqueDemo sample via `Assembly.LoadFrom`, and the sample deliberately does not redistribute `Betta.Preview` (`Private=false` + `ExcludeAssets=runtime`, as a real plugin author would). The reference puts the DLL on the test's probing path.
- `Betta.OpaqueDemo`: copies a fresh sample DLL next to `TestBetta.dll` so `LoadOpaqueDemo` is a single deterministic path instead of probing the sample's bin tree (which could pick up a stale build).

## Regression surface

- `TestParamInjector.cs` has the pure-reflection tests (descriptor discovery, deterministic GUID generation, MD5 hash format stability, DI registration, `AutoRegisterServices`). These don't need Rhino and should be the primary regression surface.
- `TestClassDirectDiscovery.cs` covers the class-direct authoring style (attributes on a concrete class, no interface) and the "interface wins" dedup rule. Pure reflection.
- `TestOutputNaming.cs` covers output naming via `OutputPlanner.PlanOutputs` — `[GrasshopperOutput]`, ValueTuple element names, and defaults. GH-free, so it runs without Rhino.
