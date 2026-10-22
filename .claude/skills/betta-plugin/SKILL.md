---
name: betta-plugin
description: Use when authoring or debugging a Betta Grasshopper plugin: plain attributed C# methods become Grasshopper components, no GH_Component code. Trigger on [GrasshopperMethod]/[GrasshopperParameter]/[GrasshopperCollection]/IBettaCollection, Betta.Abstractions, a saved .gh that broke, or asking for a GH component from a C# method. Covers attributes, return-type to output mapping, defaults, GUID stability, async/streaming, deploy.
---

# Authoring a Betta plugin

Betta (`Betta.gha`, loaded by Rhino) reflects over attributed C# types and publishes real GH components. Write a plain service; no `GH_Component` subclass, no manual wiring. Drop to raw GH only for custom canvas UI, data-tree access, or non-standard param behavior.

## The contract

A "collection" is any type inheriting **`IBettaCollection`** (the opt-in marker; without it the type is silently skipped) whose **public** methods carry `[GrasshopperMethod]`.

- **Class-direct (terse):** `public class MyPack : IBettaCollection` + `[GrasshopperCollection(category, subCategory)]`, attributes on its own public methods.
- **Interface-based:** attributes on `public interface IMyPack : IBettaCollection`, implemented by a separate class (Betta auto-binds it in DI). Use for DI swapping, mocking, published API. The interface wins: the class isn't scanned again, so no duplicates. Interface-method attributes do not propagate to the class method.

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
    public (double Sum, double Average) Stats([GrasshopperParameter("Numbers")] List<double> numbers)
        => numbers.Count == 0 ? (0, 0) : (numbers.Sum(), numbers.Average());
}
```

For interface style, move the attributes to `IMyPack : IBettaCollection` and write `class MyPack : IMyPack`.

## Attributes

| Attribute | Target | Members |
|---|---|---|
| `[GrasshopperCollection(category, subCategory)]` | interface/class | default category for every method |
| `[GrasshopperMethod(name, description?)]` | method | `Name`, `NickName`, `Description`, `Category`, `SubCategory`, `IconResource`, `Guid`, `Enabled` |
| `[GrasshopperParameter(name, nickName?, description?)]` | parameter | `Name`, `NickName`, `Description`, `Optional`, `DefaultValue` |

`DefaultValue` must be a compile-time constant (number, string, bool, enum); complex defaults go in the method body. Keep services free of Grasshopper types; `Rhino.Geometry` types are fine.

## Types to inputs/outputs (compressed)

| In code | Becomes |
|---|---|
| Param `T` / `enum` | item input (enum = integer; name string parses) |
| Param `List<T>`, `T[]`, `params T[]` | list input (access inferred from the CLR type; there is no `[Access]`) |
| Param `Line`/`Arc`/`Polyline`/`Circle`/`Surface` | accepted as `Curve` / `Brep` |
| Param `CancellationToken`, `IProgress<T>`, `IBettaReport`, `[GrasshopperMenuState]`/`[GrasshopperSecret]`/`[GrasshopperTrigger]` | no wired pin |
| `throw` in the method / `report.Warning("…")` on an `IBettaReport` param | error, no outputs / warning, outputs still set |
| Param `GH_Structure<TGoo>`, `[GrasshopperValueList]`, `[GrasshopperRange]`/`[GrasshopperNotEmpty]` | tree / auto value list / validated |
| Return primitive, Rhino geometry, `List<T>` | one output / one list output |
| Return `(A a, B b)` or `Tuple<...>` | one output per element |
| Return plain custom class | one output per public property |
| Return opaque class (`[GrasshopperOpaque]`/`IBettaValue`) | one typed wire, no explosion |
| Return `Bitmap`/`Image` | one generic object |
| Return `Task<T>`/`ValueTask<T>`, `IObservable<T>` | async / live streaming component |

### Naming outputs

Priority: (1) `[GrasshopperOutput("Name", "Nick", "tip")]` on the method or `[return: ...]`; for tuples repeat with `Index = 0`, `1`, ...; (2) ValueTuple element names (`(double Sum, double Average)` gives `Sum`/`Average`), split into words (`FlatCount` shows as "Flat Count", `CSVPath` as "CSV Path"), so a named tuple is the easy route; (3) defaults: `Output`, `Output1..N`, or property names (split the same way).

## GUID and save/reload stability

`ComponentGuid` = MD5 of `ServiceType.FullName | Method.Name`. Saved `.gh` files re-match on it across sessions, machines, plugin releases, Rhino updates and .NET versions, provided the plugin DLL is in the Betta folder.

- **Safe (not hashed):** parameters and the return type (add, rename, retype), display `Name`/`NickName`, `Description`, `Category`/`SubCategory`, icon. Like a hand-written `GH_Component` keeping its `ComponentGuid`: files reconnect wires **by position**, so add new inputs and outputs (tuple elements) after the existing ones; inserting or reordering rewires old files wrongly.
- **Renaming changes the GUID**: the method, its class/namespace, or moving the method to another type. Keep saved files working by listing the old name: `[GrasshopperMethod("Map", FormerNames = new[] { "Remap" })]` (a moved method: `"Old.Namespace.OldType.Remap"`), or for a renamed type `[GrasshopperCollection(..., FormerNames = new[] { "Old.Namespace.OldType" })]`. Betta registers each old GUID as a hidden alias; files open and take the new GUID when saved again. No tool needed: you know the old name. To change the subcategory, set `SubCategory` on `[GrasshopperMethod]` instead of moving the method.
- **Overloads:** if a type has two `[GrasshopperMethod]`s with the same method name, both GUIDs also hash the full signature (``Type|Method|paramType name,...|ReturnType``, version-free type names), so any signature change breaks them, and adding the overload changes the GUID of the method that had the name first. Give each component method its own name; the source generator warns (`BETTA011`). If an overload must be added to a published method, list its own name on that method to keep its saved files: `[GrasshopperMethod("Scale Number", FormerNames = new[] { "Scale" })]`.
- **Opaque classes** can be renamed or moved: their `Param_BettaGoo<T>` only exists inside components, which recreate it, so saved files never look it up by GUID.
- **`[GrasshopperMethod("Name", Guid = "...")]`** fixes the id for good. Use it to **port a hand-written `GH_Component`** (give the method the old `ComponentGuid`, and existing files open the Betta version). Pinning a published method's *current* GUID before a rename also works; never pin a new random one, which breaks saved files like the rename would. `betta_describe` (Betta.Mcp) reports the current GUID, the `FormerNames` aliases and every GUID Betta.log recorded for the component; the Betta Inspector component shows the live value in Rhino.
- **Before 0.8** the hash covered the whole signature with `Type.FullName`, which for generic signatures (`List<T>`, tuples, `Task<T>`, `IObservable<T>`) also included assembly versions. 0.8 registers that old GUID as a hidden alias: 0.7 files open and take the new GUID on the next save (for generic signatures only if saved with the same plugin, Rhino and .NET versions). `betta_describe` shows it as `legacyGuid`.
- A saved `.gh` showing missing components usually means a renamed or moved method or a missing DLL, not a Betta bug.

## Read when...

- New project, csproj, deploy folder, custom icon (`IconResource`): `references/setup-deploy.md`
- Need the complete mapping table (Color/Guid/Interval/Transform/Box, tuple arity 8+, entitlement row): `references/type-mapping.md`
- Returning `Task<T>`, cancellation, `IProgress`, or `IObservable<T>`: `references/async-streaming.md` (streaming is there)
- Pipeline domain objects, opaque types, viewport preview, bake, `BettaImage`, `IBettaDefault`/`IBettaSerializable`: `references/opaque-preview.md`
- Right-click menu state, secrets, run trigger, value list: `references/ui-inputs.md`
- `GH_Structure` trees, branched inputs, `[GrasshopperRange]`/`[GrasshopperNotEmpty]`/custom validators, errors and warnings from the method body: `references/trees-validation.md`
- Constructor injection, `IBettaModule`, `[GrasshopperRequiresEntitlement]`/`IBettaLicenseGate`: `references/di-licensing.md`
- Debugging registration (Inspector, `Betta_Status`, `Betta.log`), `dotnet new` templates, Betta.Mcp, `betta-docs`, example projects in the repo: `references/tooling-diagnostics.md`
