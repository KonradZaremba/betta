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
| `[GrasshopperOpaque]` | class / interface / method / `[return:]` | Opt the return type out of "explode into properties" — Betta ships it as a single typed wire via an auto-generated `Param_BettaGoo<T>` (deterministic ComponentGuid = MD5 of `T.FullName`). |
| `[GrasshopperRange(min, max)]` | parameter | Numeric range check; out-of-range surfaces a Warning + skips the invocation. |
| `[GrasshopperNotEmpty]` | parameter | Rejects null/whitespace strings and empty collections. |
| `[GrasshopperValidation(typeof(MyValidator))]` | parameter | Hands the value to your `IBettaValidator` implementation. |
| `[GrasshopperMenuState]` | parameter | Per-instance right-click menu state, not a wired input. Enum + bool get a UI editor in v0.5. |
| `[GrasshopperSecret("service.key")]` | `string` parameter | Not a wired input. Value read from the OS credential store at solve time. Set via the `Betta_Secrets` Rhino command / GH menu. |
| `[GrasshopperTrigger]` | `bool` parameter | Not a wired input. Component adds a "Run" menu item; the method only fires when clicked. Method body can read the bool to distinguish manual runs from cache re-solves. |
| `[GrasshopperValueList("a", "b", ...)]` | parameter | Normal wired input, but Betta auto-attaches a `GH_ValueList` seeded with those items when the component is placed on the canvas. Skips inputs the user has already wired. |
| `[GrasshopperRequiresEntitlement("k")]` | method / class | Inert annotation. Enforced only when a plugin registers `IBettaLicenseGate` (see [Betta.Pro](https://github.com/KonradZaremba/betta.pro)); otherwise the method runs freely (OSS mode). |

## Markers and module hook

| Type | Purpose |
|---|---|
| `IBettaCollection` | The opt-in marker for plugin types — required on the interface (or the class for the no-interface style). |
| `IBettaValue` | Equivalent to `[GrasshopperOpaque]` on the class — marks a type as opaque so it passes through Betta wires as a single typed value. |
| `IBettaModule` | Optional hook: implement `void ConfigureServices(IServiceCollection)` to register your plugin's own DI services. Betta discovers + invokes every module before `BuildServiceProvider` at startup. (Runtime-dropped plugins do not get a module pass.) |
| `IBettaDefault` | Unwired opaque inputs get a fresh `new T()` instead of `null` when `T` implements this. |
| `IBettaSerializable` | Round-trip the opaque value through `.gh` save/reload via `byte[] ToBytes()` / `void LoadFromBytes(byte[])`. |
| `IBettaValidator` | Custom rule for `[GrasshopperValidation]`. Return null on success, error message on failure. |
| `IBettaLicenseGate` | Extension hook for entitlement enforcement. Register via `IBettaModule.ConfigureServices` — Betta consults it before loading each plugin assembly and before every `[GrasshopperRequiresEntitlement]`-gated solve. Ships in Betta.Pro; absent from a stock Betta install (OSS mode = every plugin runs freely). |

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
