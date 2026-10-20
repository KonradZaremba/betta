# Opaque domain values, viewport preview, bake, BettaImage

## Opaque domain values (pass-through, no explosion)

By default a method that returns a custom class explodes into one output per public property: convenient for value bags, terrible for pipelines. Mark a class **opaque** to keep it as a single typed wire that another Betta method can accept as a typed input:

```csharp
[GrasshopperOpaque]                  // option A: type attribute
public class FloorplanGraph { ... }

public class FloorplanGraph2 : IBettaValue { ... }   // option B: marker interface

[GrasshopperMethod("Load")]
[return: GrasshopperOpaque]          // option C: per-method override
FloorplanGraphRaw Load(string path); // makes THIS return opaque
                                     // even when the type isn't marked
```

Betta auto-generates a `GH_BettaGoo<T>` + `Param_BettaGoo<T>` pair at discovery time. The `Param_BettaGoo<T>` carries a deterministic `ComponentGuid` = MD5 of `typeof(T).FullName`, so saved `.gh` files survive across rebuilds and machine moves.

A `List<T>` of opaque `T` flows as a single list-access output of the same typed param. No special syntax: Betta strips the list wrapper and reuses the registered factory.

**When to mark opaque:** any domain object you want to pass through a pipeline (Load -> Transform -> Deconstruct). **When not to:** value-bag DTOs whose properties are the useful output.

## Opt-ins for opaque types (recap)

| Marker / interface | Effect |
|---|---|
| `[GrasshopperOpaque]` or `IBettaValue` | Pass through as a single typed wire. |
| `IBettaPreview` (Betta.Preview pkg) | Viewport draw forwarded by the wrapping component. |
| `IBettaBakeable` (Betta.Preview pkg) | Right-click -> Bake adds geometry to the active Rhino doc. |
| `IBettaDefault` | Unwired opaque inputs get a fresh `new T()` instead of `null`. |
| `IBettaSerializable` | Round-trip the value through `.gh` save/reload (bytes-based). |

## Viewport preview (`Betta.Preview`)

Opaque domain objects can opt into Rhino viewport drawing by implementing `IBettaPreview` (package and namespace `Betta.Preview`; `IBettaValue` lives in `Betta.Interfaces`):

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

Betta detects `IBettaPreview` by interface-name string (so the Betta runtime stays free of a hard `Betta.Preview` reference) and the wrapping component automatically advertises `IsPreviewCapable`, sums `ClippingBox`, and forwards Draw calls via reflection. Caching is per-solve and zero-overhead when no return type implements `IBettaPreview`. `IBettaBakeable` (`Bake(RhinoDoc, ObjectAttributes, List<Guid>)`) is detected independently, so a type can bake without previewing.

## BettaImage

A `Bitmap` / `Image` return or parameter becomes a single `Param_GenericObject` (no property explosion). For bake + PNG serialize return `Betta.Preview.BettaImage` (opaque). It is bakeable (PictureFrame, PNG round-trip) but deliberately not `IBettaPreview`.
