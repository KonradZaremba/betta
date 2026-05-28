# Betta.Abstractions

> Same silhouette. Every fish is its own.
> Public SDK contract for authoring **Betta** plugins — zero-touch Grasshopper components from attributed C# service methods.

This package contains only the attributes and marker interface needed to author a Betta plugin. The runtime (`Betta.gha`) is a Grasshopper plugin loaded by Rhino, **not** shipped inside your plugin DLL.

## Quick start

1. Create a class library targeting `net7.0-windows` (or any TFM that can reference `netstandard2.0`).
2. Reference `Betta.Abstractions` with `ExcludeAssets="runtime"` so your DLL doesn't redistribute a copy:

   ```xml
   <PackageReference Include="Betta.Abstractions" Version="0.3.1"
                     ExcludeAssets="runtime" />
   ```
3. Define a service interface inheriting `IBettaCollection` and decorate it:

   ```csharp
   using Betta.Attributes;
   using Betta.Interfaces;

   [GrasshopperCollection("Strings", "Text")]
   public interface IStringCollection : IBettaCollection
   {
       [GrasshopperMethod("Upper")]
       string ToUpper([GrasshopperParameter("Text")] string text);

       [GrasshopperMethod("Concat")]
       string Concat(
           [GrasshopperParameter("Left")] string left,
           [GrasshopperParameter("Right")] string right,
           [GrasshopperParameter("Separator")] string separator);
   }

   public class StringCollection : IStringCollection
   {
       public string ToUpper(string text) => text?.ToUpper();
       public string Concat(string l, string r, string s) => l + s + r;
   }
   ```
4. Build. Drop the resulting DLL into `%AppData%\Grasshopper\Libraries\Betta\`. Restart Rhino — your components appear in the toolbar.

## Attribute reference

| Attribute | Target | Purpose |
|---|---|---|
| `[GrasshopperCollection(category, subCategory)]` | interface / class | Default Category + SubCategory for every method on the type. |
| `[GrasshopperMethod(name)]` | method | Marks the method as a Grasshopper component. Optional: `NickName`, `Description`, `Category`, `SubCategory`, `IconResource`, `Guid`, `Enabled`. |
| `[GrasshopperParameter(name, ...)]` | parameter | Customizes input display name / nickname / description. List access is inferred from the CLR type (`List<T>` → list input). `DefaultValue = ...` seeds persistent data so unwired sockets carry your default. |

## Custom icons

Ship your own PNG as an embedded resource and reference it via `IconResource`:

```xml
<EmbeddedResource Include="Resources\my_icon.png" />
```

```csharp
[GrasshopperMethod("Foo", IconResource = "my_icon.png")]
double Foo(double x);
```

The runtime matches `IconResource` against any embedded resource name **ending with** the supplied string, so the namespace prefix isn't required. Your PNG is rendered verbatim — no tinting, no overlay text.

## License

Mozilla Public License 2.0 — see the repository for details. File-level copyleft: referencing this package leaves your plugin code unaffected; only modifications to Betta's own source files must stay open.
