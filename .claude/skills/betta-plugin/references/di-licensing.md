# Plugin DI (IBettaModule) and licensing hooks

## Plugin DI services via `IBettaModule`

A plugin can register its own DI services (geometry providers, options, configuration, ...) for collection ctors to consume:

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

Betta discovers every `IBettaModule` implementor across own + loaded plugin assemblies and calls `ConfigureServices` on each (parameterless ctor required) **before** `BuildServiceProvider`. Modules are invoked best-effort: a throwing module is logged and skipped; other modules and the rest of Betta still come up.

**Plugins dropped in while Rhino runs:** the ServiceProvider is built once at startup (rebuilding it would orphan resolved singletons). Betta still creates a hot-added plugin's service from its implementation, as a singleton with constructor injection from that provider, so its components work. Its `IBettaModule` does **not** run until Rhino restarts, so a service whose constructor needs something only the module registers fails with "could not be created" until then.

## Licensing hooks

`[GrasshopperRequiresEntitlement("k")]` on a method or class is an inert annotation unless a plugin registers `IBettaLicenseGate`; then the component Warns + skips when the entitlement isn't granted. (Licensing ships separately as Betta.Pro; the Betta repo has only the hooks.)
