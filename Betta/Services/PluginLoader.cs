// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Betta.Interfaces;
using Grasshopper;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Betta.Services
{
    /// <summary>
    /// Scans %AppData%/Grasshopper/Libraries/Betta/ for plugin DLLs, loads
    /// each via a byte-stream (so rebuilding the DLL during a Rhino session
    /// does not trip a file lock), and registers their IBettaCollection
    /// types with the shared ComponentRegistry.
    ///
    /// After startup scan, optionally watches the same folder via a
    /// FileSystemWatcher and hot-adds any newly dropped DLL. Note: removing
    /// or replacing an in-memory plugin is not supported in this pass —
    /// collectible AssemblyLoadContext would be required and is intentionally
    /// deferred.
    /// </summary>
    public sealed class PluginLoader : IDisposable
    {
        private readonly ComponentRegistry _registry;
        private readonly IServiceCollection _services;
        private readonly string _folder;
        private readonly ILogger<PluginLoader> _logger;

        private readonly ConcurrentDictionary<string, Assembly> _loadedByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Assembly> _loadedByName = new(StringComparer.OrdinalIgnoreCase);

        private FileSystemWatcher _watcher;

        public PluginLoader(
            ComponentRegistry registry,
            IServiceCollection services,
            string folder,
            ILogger<PluginLoader> logger = null)
        {
            _registry = registry;
            _services = services;
            _folder = folder;
            _logger = logger ?? NullLogger<PluginLoader>.Instance;

            AppDomain.CurrentDomain.AssemblyResolve += ResolveSiblingDependency;
        }

        /// <summary>
        /// Load every *.dll currently in the plugin folder and push its
        /// descriptors into the registry. Call from PriorityLoad BEFORE
        /// ServiceProvider is built so auto-DI picks up plugin services too.
        /// </summary>
        public void LoadExisting()
        {
            if (!Directory.Exists(_folder))
            {
                _logger.LogInformation("Plugin folder {Folder} does not exist; skipping", _folder);
                return;
            }

            _logger.LogInformation("Scanning {Folder} for plugins", _folder);
            foreach (var dll in Directory.EnumerateFiles(_folder, "*.dll"))
                LoadOneForStartup(dll);
        }

        /// <summary>
        /// Begin watching the plugin folder for new DLLs. New arrivals are
        /// loaded, their services auto-registered in a child scope, their
        /// descriptors discovered and published, and GH's toolbar is asked to
        /// refresh. Removal/replacement is intentionally ignored.
        /// </summary>
        public void StartWatching()
        {
            if (!Directory.Exists(_folder))
            {
                try { Directory.CreateDirectory(_folder); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not create {Folder}", _folder); return; }
            }

            _watcher = new FileSystemWatcher(_folder, "*.dll")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnDllAppeared;
            _watcher.Changed += OnDllAppeared;
            _logger.LogInformation("Watching {Folder} for runtime plugin drops", _folder);
        }

        private void OnDllAppeared(object sender, FileSystemEventArgs e)
        {
            // FileSystemWatcher fires on a worker thread; marshal to Rhino UI
            // thread before touching Instances.ComponentServer.
            Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try { LoadOneAtRuntime(e.FullPath); }
                catch (Exception ex) { _logger.LogError(ex, "Runtime load failed for {Path}", e.FullPath); }
            }));
        }

        private void LoadOneForStartup(string path)
        {
            var asm = LoadViaBytes(path);
            if (asm == null) return;
            if (!ContainsBettaCollection(asm))
            {
                _logger.LogDebug("Skipping {Dll}: no IBettaCollection types", path);
                return;
            }

            var descriptors = _registry.DiscoverFromAssembly(asm);
            _registry.AutoRegisterServices(_services, asm);
            _logger.LogInformation("Loaded plugin {Asm} with {Count} components",
                asm.GetName().Name, descriptors.Count);
        }

        private void LoadOneAtRuntime(string path)
        {
            if (_loadedByPath.ContainsKey(path))
            {
                _logger.LogDebug("Already loaded {Path}; ignoring", path);
                return;
            }

            var asm = LoadViaBytes(path);
            if (asm == null || !ContainsBettaCollection(asm)) return;

            // Runtime DI registration requires a fresh ServiceCollection + new
            // provider. Simpler path: skip DI for runtime plugins — they must
            // expose parameterless concrete classes OR already be registered.
            // For now, log a warning if the plugin needs services we can't
            // wire post-build.
            var descriptors = _registry.DiscoverFromAssembly(asm);
            foreach (var d in descriptors)
                _registry.RegisterDescriptor(d);

            try
            {
                GH_ComponentServer.UpdateRibbonUI();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Toolbar refresh failed (components still registered)");
            }

            _logger.LogInformation("Runtime-loaded plugin {Asm} with {Count} new components",
                asm.GetName().Name, descriptors.Count);
        }

        private Assembly LoadViaBytes(string path)
        {
            try
            {
                if (_loadedByPath.TryGetValue(path, out var cached)) return cached;

                var bytes = File.ReadAllBytes(path);
                var pdbPath = Path.ChangeExtension(path, ".pdb");
                var asm = File.Exists(pdbPath)
                    ? Assembly.Load(bytes, File.ReadAllBytes(pdbPath))
                    : Assembly.Load(bytes);

                _loadedByPath[path] = asm;
                _loadedByName[asm.GetName().Name ?? ""] = asm;
                return asm;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load {Path}", path);
                return null;
            }
        }

        private static bool ContainsBettaCollection(Assembly asm)
        {
            try
            {
                return asm.GetTypes().Any(t =>
                    t != null &&
                    typeof(IBettaCollection).IsAssignableFrom(t) &&
                    t != typeof(IBettaCollection));
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Any(t =>
                    t != null &&
                    typeof(IBettaCollection).IsAssignableFrom(t) &&
                    t != typeof(IBettaCollection));
            }
        }

        /// <summary>
        /// Byte-stream loading (Assembly.Load(byte[])) does not inherit the
        /// LoadFrom directory probe. If a plugin DLL sits next to its own
        /// dependencies in our folder, CLR asks us to resolve them on demand.
        /// </summary>
        private Assembly ResolveSiblingDependency(object sender, ResolveEventArgs args)
        {
            var simpleName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(simpleName)) return null;

            if (_loadedByName.TryGetValue(simpleName, out var already)) return already;

            var candidate = Path.Combine(_folder, simpleName + ".dll");
            if (!File.Exists(candidate)) return null;

            return LoadViaBytes(candidate);
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _watcher = null;
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveSiblingDependency;
        }

        public static string DefaultPluginFolder()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Grasshopper", "Libraries", "Betta");
        }
    }
}
