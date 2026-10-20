# Betta.Preview

> Same silhouette. Every fish is its own.
> Optional viewport preview for **Betta** opaque types.

Implement `IBettaPreview` on an opaque domain type and Betta draws it in the Rhino viewport — no hand-written `GH_Component` or `GH_Goo`. Plugins that don't need preview don't need this package.

## Install

Reference it next to `Betta.Abstractions`, both with `ExcludeAssets="runtime"` (the Betta runtime ships them):

```xml
<PackageReference Include="Betta.Abstractions" Version="0.8.0" ExcludeAssets="runtime" />
<PackageReference Include="Betta.Preview" Version="0.8.0" ExcludeAssets="runtime" />
```

Or scaffold a ready-made project: `dotnet new install Betta.Templates`, then `dotnet new bettaplugin-opaque-preview -n MyPlugin`.

## Usage

```csharp
using Betta.Attributes;
using Betta.Preview;
using Grasshopper.Kernel;
using Rhino.Geometry;

[GrasshopperOpaque]
public class Footprint : IBettaPreview
{
    public Polyline Outline { get; set; }

    public BoundingBox ClippingBox => Outline.BoundingBox;

    public void DrawWires(IGH_PreviewArgs args) =>
        args.Display.DrawPolyline(Outline, args.WireColour);

    public void DrawMeshes(IGH_PreviewArgs args) { }
}
```

Any component that outputs a `Footprint` now previews it. Implement `IBettaBakeable` as well to support baking into the Rhino document. `BettaImage` is a ready-made opaque image type that bakes to a PictureFrame and serializes to PNG.

## License

Mozilla Public License 2.0 — file-level copyleft; your plugin code is unaffected. See the [repository](https://github.com/KonradZaremba/betta).
