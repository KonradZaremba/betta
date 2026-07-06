---
name: betta-plugin
description: Use when authoring, creating, or debugging a Betta Grasshopper plugin or component — turning plain attributed C# service methods into Grasshopper components with zero hand-written GH_Component code. Trigger whenever the user works with [GrasshopperMethod]/[GrasshopperParameter]/[GrasshopperCollection]/IBettaCollection, references Betta.Abstractions, asks why a saved .gh broke, or wants a Grasshopper component generated from a C# method. Covers the contract, attribute set, return-type → output mapping, defaults, custom icons, GUID/save-reload stability, and deployment.
---

# Authoring a Betta plugin

## What Betta is

Betta generates Grasshopper components from attributed C# methods — Dynamo-style ZeroTouch for Grasshopper. You write a plain service interface + implementation, decorate it, and the Betta runtime (`Betta.gha`, loaded by Rhino) reflects over it and publishes real GH components. **No `GH_Component` subclass, no manual input/output wiring.**

## The contract (do this)

A "collection" is any type that inherits `IBettaCollection` and carries `[GrasshopperMethod]` on its methods. Two authoring styles, both supported:

**Class-direct (terse — preferred for simple packs):** put the attributes straight on a concrete class.
1. `public class MyPack : IBettaCollection` decorated with `[GrasshopperCollection(category, subCategory)]`.
2. Decorate each **public** method with `[GrasshopperMethod("Name")]`.
3. Decorate parameters with `[GrasshopperParameter("Name")]` (optional but recommended).

**Interface-based (contract/impl split):** put the attributes on an interface, implement it separately.
1. `public interface IMyPack : IBettaCollection` with `[GrasshopperCollection(...)]` + attributed methods.
2. Provide a concrete class implementing the interface (Betta auto-binds it in DI).
Use this when you want DI swappability, mocking in tests, or a published API surface.

Inheriting `IBettaCollection` is **required** either way — it's the opt-in marker, so attribute-only types in unrelated DLLs are ignored. If a class implements a marked interface, the **interface wins** (the class isn't scanned again), so you never get duplicates.

## Project setup

Create a `net7.0-windows` class library and reference **`Betta.Abstractions`** (the SDK contract — attributes + marker), never the runtime `Betta.gha`. Use `ExcludeAssets="runtime"` so your DLL doesn't redistribute a copy.

Package reference (preferred once published):
```xml
<PackageReference Include="Betta.Abstractions" Version="0.3.1" ExcludeAssets="runtime" />
```

Project reference (working inside the Betta repo):
```xml
<ProjectReference Include="..\Betta.Abstractions\Betta.Abstractions.csproj">
  <Private>false</Private>
  <ExcludeAssets>runtime</ExcludeAssets>
</ProjectReference>
```

Deploy: build, then copy the DLL into `%AppData%\Grasshopper\Libraries\Betta\` (the **Betta subfolder**, not the root Libraries folder). A post-build xcopy is the usual pattern:
```xml
<PostBuildEvent>xcopy /Q/Y "$(TargetDir)MyPack.dll" "%25AppData%25\Grasshopper\Libraries\Betta\" &amp; exit /b 0</PostBuildEvent>
```
Betta watches that folder, so a new DLL is hot-added without restarting Rhino (overwriting an already-loaded DLL still needs a restart).

## Minimal complete example

Class-direct style (one type, no contract duplication):

```csharp
using System.Collections.Generic;
using System.Linq;
using Betta.Attributes;
using Betta.Interfaces;

[GrasshopperCollection("MyPack", "Maths")]
public class MyPack : IBettaCollection
{
    [GrasshopperMethod("Cube", "x³")]
    public double Cube([GrasshopperParameter("Value", DefaultValue = 2.0)] double x) => x * x * x;

    [GrasshopperMethod("Stats")]
    public (double Sum, double Average) Stats(
        [GrasshopperParameter("Numbers")] List<double> numbers)
    {
        if (numbers == null || numbers.Count == 0) return (0, 0);
        var sum = numbers.Sum();
        return (sum, sum / numbers.Count);
    }
}
```

Same thing in interface-based style — move the attributes to `public interface IMyPack : IBettaCollection { … }` and implement it with `public class MyPack : IMyPack`.

## Attribute reference

| Attribute | Target | Key members |
|---|---|---|
| `[GrasshopperCollection(category, subCategory)]` | interface / class | Default `Category` + `SubCategory` for every method on the type. |
| `[GrasshopperMethod(name, description?)]` | method | `Name`, `NickName`, `Description`, `Category`, `SubCategory`, `IconResource`, `Guid`, `Enabled`. |
| `[GrasshopperParameter(name, nickName?, description?)]` | parameter | `Name`, `NickName`, `Description`, `Optional`, `DefaultValue`. |

`DefaultValue` arguments are C# attribute arguments, so they must be **compile-time constants** (numbers, strings, bools, enums). Complex defaults belong in the method body.

## Type mapping (return type → outputs, parameter type → inputs)

| In code | Becomes |
|---|---|
| Parameter `T` (e.g. `double`, `string`, `Point3d`) | One **item** input |
| Parameter `List<T>` | One **list** input (access inferred — there is no `[Access]` attribute) |
| Parameter `params T[]` / `T[]` | List input (zoom UI for individually-named slots is roadmap) |
| Parameter `enum` | One integer input (wire a slider or panel; member name string parses too) |
| Parameter `T` where `T` is an **opaque** type | One typed input via auto-generated `Param_BettaGoo<T>` |
| Parameter `GH_Structure<TGoo>` | **Tree input** — the whole structure is handed to the method |
| Parameter `CancellationToken` | Synthetic — bound to a per-solve CTS; cancels when the component re-solves |
| Parameter `IProgress<T>` | Synthetic — `Report(value)` updates the component's `Message` |
| Parameter with `[GrasshopperMenuState]` | **Right-click menu pick**, not a wired input; persisted in the .gh |
| Parameter with `[GrasshopperSecret("service.key")]` | **Read from OS credential store**, not a wired input; missing values Warn + skip |
| Parameter with `[GrasshopperTrigger]` on `bool` | No input pin — component adds a "Run" menu item and only fires when clicked |
| Parameter with `[GrasshopperValueList(items…)]` | Regular input pin, but Betta auto-drops a wired `GH_ValueList` with those items when the component is placed |
| Parameter with `[GrasshopperRange(min,max)]` / `[GrasshopperNotEmpty]` | Validated before invocation; failures Warn + skip |
| Implicit input conversions | `Line`/`Arc`/`Polyline`/`Circle` → `Curve`; `Surface` → `Brep` |
| Color / Guid / Interval / Transform / BoundingBox | Map to `Param_Colour`/`Param_Guid`/`Param_Interval`/`Param_Transform`/`Param_Box` |
| Return / parameter `Bitmap` / `Image` | Single `Param_GenericObject` — no property explosion. For bake + PNG serialize return `Betta.Preview.BettaImage` (opaque). |
| Return primitive / Rhino geometry | One output |
| Return `List<T>` | One list output |
| Return `Tuple<...>` or named tuple `(A, B, ...)` | One output **per element** (looked up by `Item1..Item8`, TRest walked for arity 8+) |
| Return a **plain** custom class | One output **per public property** — includes simple types **and** opaque types / `List<opaque>` (v0.6+) |
| Return an **opaque** custom class | **One typed output** via `Param_BettaGoo<T>` — no explosion |
| Return `List<T>` of opaque `T` | One typed list output |
| Return `Task<T>` / `ValueTask<T>` | **Async** — runtime kicks off the task, caches by input hash, calls `ExpireSolution(true)` on completion |
| Method / class with `[GrasshopperRequiresEntitlement("k")]` | Inert annotation unless a plugin registers `IBettaLicenseGate` — then the component Warns + skips when the entitlement isn't granted |

### Naming outputs

Output names/nicknames/tooltips resolve in priority order:

1. **`[GrasshopperOutput(...)]`** on the method or its return value. Use `[return: GrasshopperOutput("Result", "R", "tooltip")]` for a single output; for a tuple, repeat it with `Index` to target each element: `[GrasshopperOutput("Sum", Index = 0)]`, `[GrasshopperOutput("Avg", Index = 1)]`.
2. **ValueTuple element names** — `(double Sum, double Average)` automatically yields outputs named `Sum` / `Average` (no attribute needed).
3. **Defaults** — `Output`, or `Output1`..`OutputN` for unnamed tuples, or the property name for class returns.

So the easiest way to get good output names is a named tuple; reach for `[GrasshopperOutput]` only to override or add a tooltip.

## Custom icon (opt out of the default betta silhouette)

Ship a PNG as an embedded resource and reference it by name suffix:
```xml
<EmbeddedResource Include="Resources\my_icon.png" />
```
```csharp
[GrasshopperMethod("Foo", IconResource = "my_icon.png")]
double Foo(double x);
```
The runtime matches `IconResource` against any embedded resource name **ending with** the string, so the namespace prefix isn't required. The PNG renders verbatim. Without `IconResource`, the component gets a betta silhouette picked deterministically from its GUID.

## Opaque domain values (pass-through, no explosion)

By default a method that returns a custom class explodes into one output per public property — convenient for value bags, terrible for pipelines. Mark a class **opaque** to keep it as a single typed wire that another Betta method can accept as a typed input:

```csharp
[GrasshopperOpaque]                  // option A — type attribute
public class FloorplanGraph { ... }

public class FloorplanGraph2 : IBettaValue { ... }   // option B — marker interface

[GrasshopperMethod("Load")]
[return: GrasshopperOpaque]          // option C — per-method override
FloorplanGraphRaw Load(string path); // makes THIS return opaque
                                     // even when the type isn't marked
```

Betta auto-generates a `GH_BettaGoo<T>` + `Param_BettaGoo<T>` pair at discovery time. The `Param_BettaGoo<T>` carries a deterministic `ComponentGuid` = MD5 of `typeof(T).FullName`, so saved `.gh` files survive across rebuilds and machine moves.

A `List<T>` of opaque `T` flows as a single list-access output of the same typed param. No special syntax needed — Betta strips the list wrapper and reuses the registered factory.

**When to mark opaque:** any domain object you want to pass through a pipeline (Load → Transform → Deconstruct). **When not to:** value-bag DTOs whose properties are the useful output.

## Plugin DI services via `IBettaModule`

A plugin can register its own DI services (geometry providers, options, configuration, …) for collection ctors to consume:

```csharp
public class MyPluginModule : IBettaModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IGeometryService, GeometryService>();
        services.Configure<MyOptions>(opts => opts.Detail = 3);
    }
}

public class MyCollection : IBettaCollection
{
    private readonly IGeometryService _geom;
    public MyCollection(IGeometryService geom) { _geom = geom; }   // ctor-injected

    [GrasshopperMethod("Build")] public Mesh Build(...) => _geom.Build(...);
}
```

Betta discovers every `IBettaModule` implementor across own + loaded plugin assemblies and calls `ConfigureServices` on each (parameterless ctor required) **before** `BuildServiceProvider`. Modules are invoked best-effort — a throwing module is logged and skipped; other modules and the rest of Betta still come up.

**Limitation:** runtime-dropped plugins (hot-added after Rhino started) do **not** get a module pass — the ServiceProvider is built once at startup, and rebuilding it would orphan resolved singletons. Plugins that need DI must be in the Betta folder when Rhino starts.

## Per-instance menu state (right-click submenu, not wired input)

Tag a parameter `[GrasshopperMenuState]` and the user picks the value once from the component's right-click menu. The choice persists with the .gh. v0.5 ships UI for `enum` (sub-menu of all members) and `bool` (toggle); other types accept the persisted value but show no editor yet.

```csharp
[GrasshopperMethod("Render")]
Bitmap Render(
    [GrasshopperParameter("Quality"), GrasshopperMenuState] Quality q,
    [GrasshopperParameter("Scene")] string scene);

public enum Quality { Draft, Final }
```

## Async essentials (CancellationToken, IProgress)

Methods returning `Task<T>` can take a `CancellationToken` parameter — the runtime synthesizes one per solve and cancels the previous when the component re-solves, so stale work quits cleanly:

```csharp
[GrasshopperMethod("Slow Sum")]
async Task<double> SumAsync(List<double> xs, CancellationToken ct) { ... }
```

`IProgress<int>` / `IProgress<string>` parameters become a sink that updates the component's `Message` tag — gives users a "computing 42%" indicator for free.

## Built-in diagnostics

- **Betta Inspector** component (toolbar: Betta / Inspector) emits one line per registered descriptor: category, name, method signature, GUID, source DLL, capability flags.
- **`Betta_Status` Rhino command** writes the same to the Rhino command line — useful when GH isn't open.
- **`betta-docs` dotnet tool** (`Betta.Docs`) walks loaded plugin DLLs and emits markdown documentation: one .md per (Category, SubCategory). Install with `dotnet tool install --global Betta.Docs`.

## Tree inputs and outputs

`GH_Structure<TGoo>` parameters and returns flow as trees — the method body sees the whole structure intact rather than per-iteration items:

```csharp
[GrasshopperMethod("Batch From Tree")]
List<Graph> BatchFromTree(
    [GrasshopperParameter("Seeds")] GH_Structure<GH_Number> seeds,
    [GrasshopperParameter("Kind")] GraphKind kind);
```

Item/list inputs in the same method continue to iterate normally; trees co-exist with both.

## Param validation

Gate parameter values before the method body runs:

| Attribute | Behavior |
|---|---|
| `[GrasshopperRange(min, max)]` | Numeric range; out-of-range surfaces a Warning and skips invocation. |
| `[GrasshopperNotEmpty]` | Rejects null/whitespace strings and empty collections. |
| `[GrasshopperValidation(typeof(MyValidator))]` | Custom rule — `IBettaValidator.Validate(value)` returns null on success or the warning message on failure. |

The component continues solving the next branch — only the offending invocation is skipped.

## Opt-ins for opaque types (recap)

| Marker / interface | Effect |
|---|---|
| `[GrasshopperOpaque]` or `IBettaValue` | Pass through as a single typed wire. |
| `IBettaPreview` (Betta.Preview pkg) | Viewport draw forwarded by the wrapping component. |
| `IBettaBakeable` (Betta.Preview pkg) | Right-click → Bake adds geometry to the active Rhino doc. |
| `IBettaDefault` | Unwired opaque inputs get a fresh `new T()` instead of `null`. |
| `IBettaSerializable` | Round-trip the value through `.gh` save/reload (bytes-based). |

## Viewport preview (`Betta.Preview`)

Opaque domain objects can opt into Rhino viewport drawing by implementing `IBettaPreview` (from the `Betta.Preview` package — add the package reference only if you need preview):

```csharp
using Betta.Preview;
using Rhino.Geometry;
using Grasshopper.Kernel;

public class Graph : IBettaValue, IBettaPreview
{
    public BoundingBox ClippingBox => /* union of geometry */ ;
    public void DrawWires(IGH_PreviewArgs args)  => /* args.Display.Draw* */ ;
    public void DrawMeshes(IGH_PreviewArgs args) => /* args.Display.Draw* */ ;
}
```

Betta detects `IBettaPreview` by interface-name string (so the Betta runtime stays free of a hard `Betta.Preview` reference) and the wrapping component automatically advertises `IsPreviewCapable`, sums `ClippingBox`, and forwards Draw calls via reflection. Caching is per-solve and zero-overhead when no return type implements `IBettaPreview`.

## GUID & save/reload stability

A component's `ComponentGuid` is a deterministic **MD5 of the method signature**: `ServiceType.FullName | Method.Name | each (paramType.FullName + " " + paramName) | ReturnType.FullName`. Grasshopper saves a placed component by this GUID and re-matches it on reload, so a saved `.gh` keeps working **across sessions and machines** — as long as the GUID is stable *and* the plugin DLL is present in the Betta folder at load.

This is the contract to keep in mind when refactoring a published pack:

- **Safe to change** (not hashed): display `Name`/`NickName`, `Description`, `Category`/`SubCategory`, the icon.
- **Breaks saved files** (GUID changes → component shows as missing): renaming the class/namespace, renaming the method, changing/reordering a parameter, the return type — and, easy to miss, **renaming a parameter** (the parameter *name* is in the hash, so `double x` → `double value` invalidates existing definitions even though it's cosmetic).
- **Escape hatch:** pin `[GrasshopperMethod("Name", Guid = "…")]` to freeze the id and refactor the signature without breaking definitions already on someone's canvas. The trade-off is you now own uniqueness by hand, so reserve it for components that are already published.

## Rules and gotchas

- **Must inherit `IBettaCollection`** (on the class or the interface) or the type is silently skipped.
- A class method exposed via `[GrasshopperMethod]` must be **public**. The attribute on an interface method does *not* propagate to the implementing class method — put it where the type you're scanning can see it.
- **Services stay framework-agnostic**: no Grasshopper types in your service interfaces/implementations. `Rhino.Geometry` types (`Point3d`, `Curve`, `Circle`, …) are fine and map to the right GH params.
- **Don't hand-write `GH_Component`** for the common case — that's the whole point of Betta. Drop to raw GH only for custom canvas UI, data-tree access, or non-standard param behavior.
- If a saved `.gh` suddenly shows missing/unrecognized components, suspect a signature change (see GUID stability above) or a missing plugin DLL — not a Betta bug.

## Reference files (in the Betta repo)

When working inside the Betta repo (`KonradZaremba/betta`), these are the canonical examples:

- Terse string-ops sample (project reference): `Betta.Strings/IStringCollection.cs`
- Defaults + list input + tuple output + custom icon + package-reference authoring: `samples/Betta.Quickstart/`
- Graduated feature tour across 4 collections (basics → intermediate → advanced → no-interface class-direct), incl. Rhino geometry: `samples/Betta.Tour/`
- The attribute definitions: `Betta.Abstractions/Attributes/`
- Hand-written `GH_Component` vs. Betta, side by side: `docs/betta-vs-grasshopper.md` (note: `docs/` is gitignored, so it's on disk locally, not on GitHub).
