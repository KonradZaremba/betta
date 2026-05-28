// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
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

        public static IServiceProvider ServiceProvider
        {
            get { lock (_lock) return _serviceProvider; }
            private set { lock (_lock) _serviceProvider = value; }
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

                var registry = new ComponentRegistry();
                registry.DiscoverFromAssembly(assembly);
                registry.AutoRegisterServices(services, assembly);

                // Plugin folder: scan every *.dll there, byte-stream-load it,
                // collect descriptors + auto-register any IBettaCollection
                // services. Plugins can thus ship their own services.
                var pluginFolder = PluginLoader.DefaultPluginFolder();
                _pluginLoader = new PluginLoader(registry, services, pluginFolder);
                _pluginLoader.LoadExisting();

                ServiceProvider = services.BuildServiceProvider();

                registry.Logger = ServiceProvider.GetService<ILogger<ComponentRegistry>>();
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
                // watching the folder for runtime plugin drops.
                _pluginLoader.Dispose(); // unhook the startup AssemblyResolve listener
                _pluginLoader = new PluginLoader(
                    registry,
                    services,
                    pluginFolder,
                    ServiceProvider.GetService<ILogger<PluginLoader>>());
                _pluginLoader.StartWatching();

                return GH_LoadingInstruction.Proceed;
            }
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine($"[Betta ERROR] Startup failed: {ex}");
                return GH_LoadingInstruction.Abort;
            }
        }
    }
}
