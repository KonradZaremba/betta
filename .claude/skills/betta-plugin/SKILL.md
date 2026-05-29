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
| Return primitive / Rhino geometry | One output |
| Return `List<T>` | One list output |
| Return `Tuple<...>` or named tuple `(A, B, ...)` | One output **per element** (looked up by `Item1..Item8`) |
| Return a custom class | One output **per public simple property** (output named by property) |
| Return `Task<T>` / `ValueTask<T>` | **Async** — runtime kicks off the task, caches by input hash, calls `ExpireSolution(true)` on completion |

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
