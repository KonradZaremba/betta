# Agent notes: Betta plugin

This project is a [Betta](https://github.com/KonradZaremba/betta) Grasshopper plugin: public methods on types that implement `IBettaCollection` become Grasshopper components at runtime. Don't write `GH_Component` classes.

- **Add a component:** a public method with `[GrasshopperMethod("Name", "Description")]` on an `IBettaCollection` type, parameters with `[GrasshopperParameter("Input name", DefaultValue = …)]`. Follow the existing file's style (interface + class, or attributes directly on the class).
- **Category:** `[GrasshopperCollection("Category", "Subcategory")]` on the type, or `Category` / `SubCategory` on `[GrasshopperMethod]`.
- **Inputs:** one per parameter. `List<T>` or `T[]` is a list input. `Rhino.Geometry` types (`Curve`, `Point3d`, …) work as they are.
- **Outputs:** the return value. A named tuple `(double Sum, double Average)` gives one named output per element; `FlatCount` shows as "Flat Count".
- **Async:** return `Task<T>` and take a `CancellationToken` parameter (no input pin; cancelled when the inputs change). Betta runs it off the UI thread. An `IProgress<T>` parameter shows progress.
- **Live data:** return `IObservable<T>`.
- **Errors and warnings:** throw an exception to show an error; the component outputs nothing. To warn and still output results, take an `IBettaReport report` parameter (no input pin) and call `report.Warning("…")` or `report.Remark("…")`.
- **Saved files:** `.gh` files find a component by its GUID, which Betta derives from the type and method name only, so parameters and outputs can change freely; add new ones after the existing ones, since files reconnect wires by position. When you rename the method, keep saved files working by listing its old name: `[GrasshopperMethod(…, FormerNames = new[] { "OldName" })]` (`"Old.Namespace.OldType.OldName"` if it moved from another type); when you rename a type, list its old full name in `[GrasshopperCollection(…, FormerNames = new[] { "Old.Namespace.OldType" })]`. Never invent a GUID. To change the subcategory, set `SubCategory` on the attribute. Give each component method its own name.
- **More** (opaque types, viewport preview, trees, validation, menu/secret/trigger inputs, DI): the `betta-plugin` skill if installed, otherwise the Betta README.
