# Full type mapping (return type -> outputs, parameter type -> inputs)

The core SKILL.md has the compressed table; this is the complete one.

| In code | Becomes |
|---|---|
| Parameter `T` (e.g. `double`, `string`, `Point3d`) | One **item** input |
| Parameter `List<T>` | One **list** input (access inferred; there is no `[Access]` attribute) |
| Parameter `params T[]` / `T[]` | List input (zoom UI for individually-named slots is roadmap) |
| Parameter `enum` | One integer input (wire a slider or panel; member name string parses too) |
| Parameter `T` where `T` is an **opaque** type | One typed input via auto-generated `Param_BettaGoo<T>` |
| Parameter `GH_Structure<TGoo>` | **Tree input**: the whole structure is handed to the method (trees-validation.md) |
| Parameter `CancellationToken` | Synthetic: bound to a per-solve CTS; cancels when the component re-solves |
| Parameter `IProgress<T>` | Synthetic: `Report(value)` updates the component's `Message` |
| Parameter `IBettaReport` | Synthetic: `Warning(…)` / `Remark(…)` show on the component while the outputs are still set (trees-validation.md) |
| Parameter with `[GrasshopperMenuState]` | **Right-click menu pick**, not a wired input; persisted in the .gh |
| Parameter with `[GrasshopperSecret("service.key")]` | **Read from OS credential store**, not a wired input; missing values Warn + skip |
| Parameter with `[GrasshopperTrigger]` on `bool` | No input pin; component adds a "Run" menu item and only fires when clicked |
| Parameter with `[GrasshopperValueList(items...)]` | Regular input pin, but Betta auto-drops a wired `GH_ValueList` with those items when the component is placed |
| Parameter with `[GrasshopperRange(min,max)]` / `[GrasshopperNotEmpty]` | Validated before invocation; failures Warn + skip |
| Implicit input conversions | `Line`/`Arc`/`Polyline`/`Circle` -> `Curve`; `Surface` -> `Brep` |
| Color / Guid / Interval / Transform / BoundingBox | Map to `Param_Colour`/`Param_Guid`/`Param_Interval`/`Param_Transform`/`Param_Box` |
| Return / parameter `Bitmap` / `Image` | Single `Param_GenericObject`, no property explosion. For bake + PNG serialize return `Betta.Preview.BettaImage` (opaque). |
| Return primitive / Rhino geometry | One output |
| Return `List<T>` | One list output |
| Return `Tuple<...>` or named tuple `(A, B, ...)` | One output **per element** (looked up by `Item1..Item8`, TRest walked for arity 8+) |
| Return a **plain** custom class | One output **per public property**, including simple types **and** opaque types / `List<opaque>` (v0.6+) |
| Return an **opaque** custom class | **One typed output** via `Param_BettaGoo<T>`, no explosion |
| Return `List<T>` of opaque `T` | One typed list output |
| Return `Task<T>` / `ValueTask<T>` | **Async**: runtime kicks off the task, caches by input hash, calls `ExpireSolution(true)` on completion (async-streaming.md) |
| Return `IObservable<T>` | **Streaming**: live component (async-streaming.md) |
| Method / class with `[GrasshopperRequiresEntitlement("k")]` | Inert annotation unless a plugin registers `IBettaLicenseGate`; then the component Warns + skips when the entitlement isn't granted (di-licensing.md) |
