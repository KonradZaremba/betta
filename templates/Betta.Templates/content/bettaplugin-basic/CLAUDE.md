# Agent notes: Betta plugin

This project is a [Betta](https://github.com/KonradZaremba/betta) Grasshopper plugin: public methods on types that implement `IBettaCollection` become Grasshopper components at runtime. Don't write `GH_Component` classes.

- **Add a component:** a public method with `[GrasshopperMethod("Name", "Description")]` on an `IBettaCollection` type, parameters with `[GrasshopperParameter("Input name", DefaultValue = …)]`. Follow the existing file's style (interface + class, or attributes directly on the class).
- **Category:** `[GrasshopperCollection("Category", "Subcategory")]` on the type, or `Category` / `SubCategory` on `[GrasshopperMethod]`.
- **Inputs:** one per parameter. `List<T>` or `T[]` is a list input. `Rhino.Geometry` types (`Curve`, `Point3d`, …) work as they are.
- **Outputs:** the return value. A named tuple `(double Sum, double Average)` gives one named output per element.
- **Async:** return `Task<T>` and take a `CancellationToken` parameter (no input pin; cancelled when the inputs change). Betta runs it off the UI thread. An `IProgress<T>` parameter shows progress.
- **Live data:** return `IObservable<T>`.
- **Errors and warnings:** throw an exception to show an error; the component outputs nothing. To warn and still output results, take an `IBettaReport report` parameter (no input pin) and call `report.Warning("…")` or `report.Remark("…")`.
- **Saved files:** `.gh` files find a component by its GUID. Unless pinned with `[GrasshopperMethod(…, Guid = "…")]`, it's derived from the signature: renaming the method, its type or a parameter, or reordering or retyping a parameter, breaks saved files. With a generic type in the signature (`List<T>`, a tuple, `Task<T>`, `IObservable<T>`) it also changes whenever the plugin's version, Rhino or .NET updates, so pin a GUID on those when you create them (a new one is fine then). When changing a published component, pin the GUID its saved files use; never invent one. The `betta_describe` tool (Betta.Mcp) shows it, plus every GUID Betta has logged for the component.
- **More** (opaque types, viewport preview, trees, validation, menu/secret/trigger inputs, DI): the `betta-plugin` skill if installed, otherwise the Betta README.
