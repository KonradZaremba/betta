# Betta — Roadmap & Brainstorm

Three tiers: **Now** (obvious completions, 1–3 days each), **Next** (meaningful new surface, a week each), **Later** (architectural bets). A brainstorm section at the end collects looser ideas to revisit.

---

## Now

Things that are half-wired and need finishing, or small gaps that bite during daily use.

### Icons
- Descriptor already carries `IconResource`; `BettaComponentProxy.Icon` returns `null`.
- Load from embedded resource when `IconResource` is set; fall back to a generated 24×24 icon showing the NickName on a subcategory-tinted background (color from a stable hash of SubCategory).
- Cache on `ComponentDescriptor.CachedIcon` so each render doesn't redraw.

### Async method support
- Detect `Task<T>` / `ValueTask<T>` returns, kick off the task in `SolveInstance`, cache by input hash, call `ExpireSolution(true)` on completion → cache hit on re-solve returns the result synchronously.
- Service methods become `async Task<string> Fetch(string url) => await http.GetStringAsync(url);` and just work.
- See Hops and Telepathy for the canonical GH async pattern this mimics.

### Tree / `GH_Structure<T>` inputs
- `ParamInjector.GetItemData` / `GetListData` currently throw on `PathCount != 1`.
- Two possible semantics, either is defensible:
  - **Iterate per path** (default): method invoked once per branch, outputs reassembled into matching tree structure.
  - **Pass whole tree**: parameter typed `GH_Structure<IGH_Goo>` or `IDictionary<GH_Path, List<T>>`, method handles the tree itself.
- Pick the first for v1; leave escape hatch for the second via an attribute flag.

### Parameter validation attributes
- `[Range(Min = 0, Max = 100)]`, `[Required]` → surface as runtime warnings on the component instead of silently dropping bad inputs.

### Default values
- `[GrasshopperParameter("Seed", DefaultValue = 42)]` — unwired input pre-filled instead of `default(T)`.
- Eliminates the "invalid circle because radius=0" confusion.

### Test coverage of the runtime
- `TestParamInjector` today only hits the descriptor / registry reflection paths.
- Add tests that construct a `ComponentDescriptor`, build a `ParamInjector` against a fake `GH_ComponentParamServer`, assert input/output param types and argument dispatch without launching Rhino. This is the most fragile code; it deserves unit tests.

---

## Next

Bigger pieces that unlock clear categories of plugin.

### NuGet publish
- `Betta.Abstractions` is netstandard2.0 and has no deps — ready to pack.
- `dotnet pack -c Release -o ./artifacts` → push to nuget.org or GitHub Packages (`@designboticteam` scope already configured per CLAUDE.md).
- CI pipeline (GitHub Actions) that runs `dotnet pack` on tags + publishes automatically.

### Source generator variant
- Compile-time component emission: for each `[GrasshopperMethod]`, the generator produces a concrete `GH_Component` subclass.
- Trade-off vs. current runtime proxy approach:
  - **Better**: debuggable types, deterministic GUIDs baked in source, IDE-friendly stack traces, can emit icons as partial classes.
  - **Worse**: more build complexity, requires consumers to run an analyzer.
- Live alongside the runtime path, not replace it — plugin authors choose which they prefer.

### Hot-reload (collectible ALC)
- Custom `AssemblyLoadContext` per plugin with `isCollectible: true`.
- Shared types (`IBettaCollection`, attribute types) resolve to the same `Type` across ALCs via a parent/fallback resolver.
- Watcher triggers unload + reload when a DLL is replaced — true sub-second iteration for plugin devs.
- Non-trivial: type identity pitfalls, reference leaks, must audit for handles that prevent collection.

### Cancellation
- `CancellationToken` as a method parameter → honored when GH aborts the solve (user edits upstream during a long task).
- Pairs naturally with async support.

### Progress reporting
- `IProgress<double>` parameter → wired to the component's little progress indicator (`Message`, `NickName` overlay, or GH 8's progress pip).
- Useful for long-running HTTP / file / ML tasks.

### Yak packaging
- `manifest.yml` + `rh8_0-any` framework folder, `dotnet yak push` → installs via Rhino's Package Manager rather than manual xcopy.

### `dotnet new bettaplugin` template
- Two-command scaffold for plugin authors: `dotnet new bettaplugin -n MyPack && dotnet build`.
- Pre-wired ProjectReference, sample service, post-build to the plugin folder.
- Until this lands, `samples/Betta.Quickstart/` is the copy-paste starting point (package-reference based).

### Settings infrastructure (prerequisite for the next two)
- Static `BettaSettings` with JSON persistence at `%AppData%\Grasshopper\Libraries\Betta\settings.json`.
- `FileSystemWatcher` for hot-reload (same pattern as `PluginLoader`).
- Helper to walk every `BettaComponent` on the active GH canvas and refresh per-instance state without losing wired connections.
- Both backlog items below depend on this; build it once, reuse twice.

### Extended/advanced parameter toggle
- New `Extended = true` flag on `GrasshopperParameterAttribute`.
- Per-category (or global) "show extended params" toggle persisted via the settings infra.
- `BettaComponent` implements `IGH_VariableParameterComponent` so GH supports rebuilding inputs without breaking wires. Extended params placed at the end of the parameter list to keep indices stable; toggling sets them `Hidden = true` rather than deleting, so persistent data and wires survive across sessions.
- UI: right-click menu item on any Betta component → flips the flag → walks every Betta component on the canvas to call `VariableParameterMaintenance()`.
- Open design question: per-addon scope (each plugin's category gets its own switch) vs. one global switch.

### Localization / external text via JSON
- `LocalizationProvider` reads `%AppData%\Grasshopper\Libraries\Betta\strings.{culture}.json`. Schema keys components by descriptor GUID (stable) with overrides for `name`, `description`, `nickName` and per-parameter strings.
- Lookup chain in `ComponentRegistry.DiscoverFromAssembly`: attribute → JSON override for `BettaSettings.Culture` (defaults to `CultureInfo.CurrentUICulture`) → fallback.
- Hot-reload via FileSystemWatcher: on JSON change, walk live components and update `Name`, `NickName`, parameter strings — GH redraws the canvas. GUIDs never change so `.gh` files are unaffected.
- Authoring: ship a side tool (`Betta.LocalizationGen`) that scans an assembly and emits a starter JSON with English defaults pre-filled, so translators don't have to copy GUIDs by hand.
- Open design question: also override category/subcategory tab labels via `Instances.ComponentServer.AddCategoryShortName`?

---

## Later

Ideas that reshape the architecture or ecosystem; commit to these only if usage patterns demand.

### Custom `IGH_Goo` wrappers
- Today only Rhino geometry + primitives flow cleanly. A service method returning `MyCustomClass` gets decomposed into its public simple properties — which is often wrong.
- Generate an `IGH_Goo<T>` wrapper per custom type so it can flow through GH as a first-class geometry-ish object.

### Preview / draw hooks
- `[GrasshopperPreview]` method that returns draw calls (lines, meshes, labels) executed during viewport render. Works alongside the normal "compute" method.

### Right-click menu items / options
- `[GrasshopperOption]` attributes populate the component's context menu — dropdowns, toggles, seed overrides.

### Component state persistence
- `[BettaState]` field that survives `.gh` save/load via `GH_Component.Read` / `Write` round-trip (e.g. a seed for a random generator, a cached HTTP response).

### Grasshopper 2 readiness
- GH2 ships in Rhino 9 with a different component model. Plan the migration strategy: what survives from the proxy pattern, what doesn't, whether the abstractions package can target both generations.

### Diagnostics component
- A GH component that renders the current registry state / recent invocations / per-component timings. Built with Betta itself.

### LoadLibrary canvas component
- The brief's vision: drop a `Load Library` component onto the canvas, point it at a DLL, get its components published in a fresh GH tab without restarting. Today's model is folder-watched-startup-load + FileSystemWatcher hot-add — this would add a third intake on the canvas itself.
- Companion components: `Library Info` (lists generated names + version + last-load timestamp for a loaded library), `Reload Library` (button to force regeneration regardless of file watcher state).
- Per-document persistence: store the loaded library paths in GH user data so reopening a `.gh` reconnects to the same DLLs.
- Wires-survive-rebuild: when a method signature stays the same across reloads, preserve canvas connections. When it changes, warn clearly.

### Morph lock per document
- Store the session morph guid in `.gh` user data so a doc always reopens with the same color palette. Useful for documentation, demos, and screenshot stability.
- UI surface: a "Lock morph for this document" toggle in the future BettaSettingsPanel (or a context-menu item on any Betta component).

### BettaSettingsPanel
- Eto.Forms / WinForms panel listing loaded plugins across all open documents, the current session's morph, log tail, manual reload buttons, morph-lock toggle.
- Hosts the morph display ("Today's morph: Galaxy 🐠") and surfaces the wild-morph easter egg when it triggers.

### XML doc-comment integration
- Read `<summary>`, `<param>`, `<returns>` from XML doc files alongside loaded assemblies. Use them as fallback for `Description` / parameter descriptions / output descriptions when the corresponding attribute fields are not set.
- Plugin authors stop having to duplicate `<param name="x">…</param>` and `[GrasshopperParameter("x", Description = "…")]`.

### Aquarium.Core extraction
- The brand brief positions Betta as part of an "Aquarium" family of plugins, all sitting on a shared `Aquarium.Core` library (Reflection, ComponentFactory, Discovery primitives). The current `Betta.Abstractions` split is the ancestor of that — a future pass would split Reflection / Type coercion / Discovery primitives out of `Betta.gha` into `Aquarium.Core` so sibling plugins (Ghost Catfish, Cardinal, etc. mentioned in the brief) can reuse them without depending on Betta proper.

### Rhino 7 backwards compatibility
- Multi-target `Betta.csproj` to `net48;net7.0-windows` so one repo ships both runtimes.
- Conditional pieces (~50 lines, three files): `BettaComponentProxy` inherits `GH_ObjectProxy` on net48 (the class still exists in GH7) vs. implements `IGH_ObjectProxy` on net7; Grasshopper NuGet pinned to `7.13.x` for net48 vs. `[8.0,8.27)` for net7; both targets deploy to the same `%AppData%\Grasshopper\Libraries\` folder.
- `Betta.Abstractions` (netstandard2.0) and plugin DLLs already work on either runtime — no changes there.
- Hold off until someone asks. R7 is in maintenance, R8 is current, R9 is next; multi-target adds permanent test surface for diminishing audience.

---

## Brainstorm — ecosystem plugins

Shape of community-shippable `Betta.*` plugins, both to eat our own dogfood and to seed an ecosystem.

- **`Betta.Http`** — `Get`, `Post`, `Put`, `Delete`, `JsonParse`, `JsonQuery` (JSONPath/JMESPath).
- **`Betta.Data`** — CSV / XML / YAML readers + writers, basic SQL query against an embedded SQLite.
- **`Betta.Cloud`** — Azure Blob / AWS S3 / GCS get/put/list. Auth via WebView2 modal (see below).
- **`Betta.Ai`** — wrappers for OpenAI / Anthropic / local LLM via Ollama; embeddings; image gen. Each wrapper is a ~20-line service method.
- **`Betta.Process`** — launch external executables, capture stdout/stderr/exit code. Useful for wrapping CLI tools (e.g. `ogr2ogr`, `ffmpeg`).
- **`Betta.Speckle`** — wrap the Speckle SDK as service methods for send/receive/branch/commit. Already familiar territory.
- **`Betta.Revit`** — bridge between GH and a running Revit instance via Rhino.Inside.Revit.
- **`Betta.Maps`** — geocode, reverse geocode, tile fetch, routing.
- **`Betta.Vision`** — image I/O, basic OpenCV ops via OpenCvSharp, QR codes.

Each is a separate repo/NuGet/yak — the whole point of the plugin-folder architecture.

---

## Brainstorm — cross-cutting features

Ideas that would touch the core if adopted.

- **Auth modal (WebView2)** — a plugin author wants OAuth for their service. Ship a helper in core: `BettaAuth.OpenBrowserFlow(authUrl, redirectPattern) -> Task<string>` returning the captured token. Plugin methods that need auth declare a `[Authenticated]` attribute and the runtime threads the token in automatically.
- **Session-scoped caching** — decorate a method `[Cache(DurationSeconds = 60)]`; the runtime caches results keyed by input hash, invalidating after the duration. Useful alongside async.
- **Per-component settings panel** — first-class UI surface for component configuration (file picker, dropdowns). Currently would live in `[GrasshopperOption]`; could be a richer Eto.Forms panel.
- **Telemetry hooks** — `ILogger<T>` already exists, add structured `IMetric` for timing/counts. Lets plugin authors ship telemetry-aware components.
- **Plugin dependencies** — a plugin declares `[BettaRequires("Betta.Http", ">=1.0.0")]`; loader fails cleanly if missing. Prevents confusing silent-failure when a plugin uses types from a sibling plugin that isn't installed.
- **Component unit tests helper** — a fixture type in a `Betta.Testing` package that lets plugin authors `ComponentFixture.Solve<IMyService>(nameof(MyMethod), args)` without touching Grasshopper. Runs the same ParamInjector path against a mock param server.
- **Localization** — `[GrasshopperMethod("Upper", Name_pl = "Wielkie")]` or resource-file-driven. Rhino runs in many locales.
- **Component deprecation** — `[Obsolete("Use NewFoo instead")]` → sets `IGH_ObjectProxy.Obsolete = true`, shows the warning strikethrough in the toolbar. Existing `.gh` files still resolve via the same GUID.
- **Canvas quick-replace** — right-click a component → "replace with newer version" if the same service method gained a new GUID via signature change.
- **Inline C# scripting node** — a built-in Betta component that lets users type a method body in-canvas, compiled via Roslyn scripting, wrapped as a one-off plugin. Power-user feature; duplicates GH1's C# node somewhat.

---

## Known limitations to document

Copying here so we don't rediscover them later:

- Tree inputs are rejected (see *Now*).
- Hot-reload of replaced plugins needs collectible ALC (see *Next*).
- Byte-stream loading doesn't probe a plugin's own siblings for dependencies automatically — the `AssemblyResolve` handler in `PluginLoader` only probes the plugin folder. Plugins that ship their own deep dependency tree should bundle them in `Betta/<plugin-name>/*.dll` and we'd need to extend the resolver.
- Circle radius=0 outputs "Invalid Circle" — this is correct Rhino behavior, not a bug. Default values (see *Now*) will make the unwired case less confusing.
- `dotnet test` currently hits a CoreCLR startup issue on this machine; tests pass via VS Test Explorer. Worth chasing once but not blocking.
