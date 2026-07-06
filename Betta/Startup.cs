// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Reflection;
using Betta.Commands;
using Betta.Interfaces;
using Betta.Rendering;
using Betta.Services;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Betta
{
    public class Startup : GH_AssemblyPriority
    {
        private static readonly object _lock = new object();
        private static IServiceProvider _serviceProvider;
        private static PluginLoader _pluginLoader;
        private static IBettaLicenseGate _licenseGate;

        public static IServiceProvider ServiceProvider
        {
            get { lock (_lock) return _serviceProvider; }
            private set { lock (_lock) _serviceProvider = value; }
        }

        /// <summary>
        /// The DI-registered license gate, or null if no plugin registered
        /// one. Consumers must null-check — a null gate means licensing is
        /// off (the free OSS mode) and every check should pass. Cached
        /// here at startup so hot paths (RegisterDescriptor, SolveInstance)
        /// don't have to resolve from the ServiceProvider on every call.
        /// </summary>
        public static IBettaLicenseGate LicenseGate
        {
            get { lock (_lock) return _licenseGate; }
            private set { lock (_lock) _licenseGate = value; }
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(b =>
            {
                b.SetMinimumLevel(LogLevel.Debug);
                b.AddProvider(new FileLoggerProvider());
            });
        }

        public override GH_LoadingInstruction PriorityLoad()
        {
            try
            {
                var assembly = typeof(Startup).Assembly;
                var services = new ServiceCollection();
                ConfigureServices(services);

                // Bootstrap logger — a temporary Microsoft.Extensions.Logging
                // factory that writes to the same Betta.log file as the DI-backed
                // one below. Needed for the pre-BuildServiceProvider phase
                // (PluginLoader.LoadExisting + ModuleLoader) so we get visibility
                // into which plugins loaded and which IBettaModules ran. Without
                // it those calls silently write to NullLogger and we're blind
                // when something goes wrong.
                using var bootstrapFactory = LoggerFactory.Create(b =>
                {
                    b.SetMinimumLevel(LogLevel.Debug);
                    b.AddProvider(new FileLoggerProvider());
                });

                var registry = new ComponentRegistry();
                registry.Logger = bootstrapFactory.CreateLogger<ComponentRegistry>();
                // Fast path: if an assembly ships a generator-emitted bootstrap
                // (Betta.Generated.BettaPluginBootstrap), use it instead of the
                // reflection scan. Betta itself doesn't currently ship one but
                // the same code path is exercised against every plugin
                // assembly below.
                if (!TryAddFromBootstrap(registry, assembly, "Betta"))
                    registry.DiscoverFromAssembly(assembly);
                registry.AutoRegisterServices(services, assembly);

                // Plugin folder: scan every *.dll there, byte-stream-load it,
                // collect descriptors + auto-register any IBettaCollection
                // services. Plugins can thus ship their own services.
                //
                // Trust policy is loaded from
                // %AppData%\Grasshopper\Libraries\Betta\trust.json — missing
                // file ⇒ mode Off, no signature checks, existing installs
                // load exactly as before. Users configure via
                // Betta_Trust or the GH menu → Betta → Trusted publishers.
                var pluginFolder = PluginLoader.DefaultPluginFolder();
                var trust = PluginTrustPolicy.LoadOrOff();
                _pluginLoader = new PluginLoader(
                    registry, services, pluginFolder,
                    bootstrapFactory.CreateLogger<PluginLoader>(),
                    trust: trust);
                _pluginLoader.LoadExisting();

                // Bootstrap fast-path sweep for plugin assemblies. PluginLoader
                // performs the reflection scan inline, so a re-add via the
                // bootstrap is deduped by the registry's GUID set (no doubles).
                // The bootstrap call is therefore informational here — it
                // proves the generator emitted a usable bootstrap. A follow-on
                // PluginLoader change will let it skip DiscoverFromAssembly
                // when the bootstrap is present and the speedup will land.
                foreach (var pluginAsm in _pluginLoader.LoadedAssemblies)
                    TryAddFromBootstrap(registry, pluginAsm, pluginAsm.GetName().Name);

                // IBettaModule hook: walk own assembly + every loaded plugin
                // assembly and let each module register its own DI services
                // before BuildServiceProvider. Modules are discovered by
                // reflection (parameterless ctor + ConfigureServices(services)).
                // Runtime-dropped plugins (added after this point) do NOT get a
                // module pass — see IBettaModule XML doc + PluginLoader.
                var mlLogger = bootstrapFactory.CreateLogger("ModuleLoader");
                ModuleLoader.InvokeFromAssembly(assembly, services, mlLogger);
                ModuleLoader.InvokeFromAssemblies(_pluginLoader.LoadedAssemblies, services, mlLogger);

                ServiceProvider = services.BuildServiceProvider();

                registry.Logger = ServiceProvider.GetService<ILogger<ComponentRegistry>>();

                // Resolve the license gate ONCE — cached in Startup.LicenseGate.
                // If no IBettaModule registered one, the property stays null
                // and every entitlement check no-ops (OSS mode). If Betta.Pro
                // (or another gate) is installed, it's used from here on.
                LicenseGate = ServiceProvider.GetService<IBettaLicenseGate>();
                if (LicenseGate != null)
                {
                    try
                    {
                        Rhino.RhinoApp.WriteLine(
                            $"[Betta] License gate active: {LicenseGate.GetType().FullName}. Assemblies and entitlements will be enforced.");
                    }
                    catch { }
                }

                registry.RegisterWithGrasshopper();

                // Each component picks one of the embedded fish silhouettes
                // deterministically from its descriptor GUID, so
                // there's no single "fish of the day" — log the library size
                // instead so anyone tailing the log can confirm the resources
                // loaded.
                ServiceProvider.GetService<ILogger<Startup>>()
                    ?.LogInformation("Fish library loaded: {Count} silhouettes ({Names})",
                        SessionFish.Count,
                        string.Join(", ", SessionFish.Names));

                // Now that DI is up, swap the loader's logger and start
                // watching the folder for runtime plugin drops. Trust policy
                // rides along so runtime-dropped DLLs pass the same signature
                // gate as startup ones.
                _pluginLoader.Dispose(); // unhook the startup AssemblyResolve listener
                _pluginLoader = new PluginLoader(
                    registry,
                    services,
                    pluginFolder,
                    ServiceProvider.GetService<ILogger<PluginLoader>>(),
                    trust);
                _pluginLoader.StartWatching();

                // Add "Betta" entry to the GH main-window menu so users can
                // reach the trust and secrets dialogs without knowing the
                // Rhino command names. DocumentEditor is initialized lazily
                // by GH — hook CanvasCreated to inject when it's up.
                TryWireGhMenu();

                return GH_LoadingInstruction.Proceed;
            }
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine($"[Betta ERROR] Startup failed: {ex}");
                return GH_LoadingInstruction.Abort;
            }
        }

        /// <summary>
        /// Inject a "Betta" top-level menu with "Trusted publishers…" and
        /// "Secrets…" entries into the GH main window. Called after
        /// PriorityLoad completes; delayed via Instances.CanvasCreated because
        /// the DocumentEditor isn't populated until GH's canvas comes up.
        /// Wrapped in a try because the GH menu API evolves between releases —
        /// missing MenuStrip should not abort startup.
        /// </summary>
        private static void TryWireGhMenu()
        {
            try
            {
                Grasshopper.Instances.CanvasCreated += canvas =>
                {
                    try
                    {
                        var editor = Grasshopper.Instances.DocumentEditor;
                        if (editor?.MainMenuStrip == null) return;

                        var top = new System.Windows.Forms.ToolStripMenuItem("Betta");
                        var trust = new System.Windows.Forms.ToolStripMenuItem("Trusted publishers…");
                        trust.Click += (_, __) => BettaTrustCommand.ShowDialog();
                        var secrets = new System.Windows.Forms.ToolStripMenuItem("Secrets…");
                        secrets.Click += (_, __) => BettaSecretsCommand.ShowDialog();

                        top.DropDownItems.Add(trust);
                        top.DropDownItems.Add(secrets);
                        editor.MainMenuStrip.Items.Add(top);
                    }
                    catch (Exception ex)
                    {
                        try { Rhino.RhinoApp.WriteLine($"[Betta] Could not inject GH menu: {ex.Message}"); } catch { }
                    }
                };
            }
            catch (Exception ex)
            {
                try { Rhino.RhinoApp.WriteLine($"[Betta] CanvasCreated hook failed: {ex.Message}"); } catch { }
            }
        }

        /// <summary>
        /// Look for <c>Betta.Generated.BettaPluginBootstrap</c> on
        /// <paramref name="assembly"/>. When present, materialise its
        /// blueprints into <see cref="ComponentDescriptor"/>s and push them
        /// into <paramref name="registry"/>. Returns <c>true</c> when the
        /// bootstrap fired so the caller can skip the reflection scan.
        /// Emits a tagged log line so operators can grep Betta.log for
        /// proof the fast path engaged.
        /// </summary>
        private static bool TryAddFromBootstrap(ComponentRegistry registry, Assembly assembly, string assemblyTag)
        {
            try
            {
                var descriptors = ComponentDescriptorBlueprint.TryLoadFromAssembly(assembly);
                if (descriptors == null) return false;

                var added = registry.AddDescriptors(descriptors);
                // The registry logger isn't online yet at this point so write
                // through Rhino's console; FileLoggerProvider also tails to
                // Betta.log via the logger pipeline once DI is up, but at
                // PriorityLoad time stdout is the only sink we control.
                try
                {
                    Rhino.RhinoApp.WriteLine(
                        $"[Betta] Bootstrap: {added.Count} descriptors from {assemblyTag} (generator fast-path).");
                }
                catch { /* RhinoApp not available in tests */ }
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    Rhino.RhinoApp.WriteLine($"[Betta] Bootstrap fast-path failed for {assemblyTag}: {ex.Message}");
                }
                catch { }
                return false;
            }
        }
    }
}
