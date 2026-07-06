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
using System.Runtime.Loader;
using Betta.Interfaces;
using Grasshopper;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Betta.Services
{
    /// <summary>
    /// Scans <c>%AppData%/Grasshopper/Libraries/Betta/</c> for plugin DLLs,
    /// loads each into its own collectible <see cref="AssemblyLoadContext"/>
    /// (so the DLL on disk isn't locked and so it can be unloaded at hot-reload
    /// time), and registers their <see cref="IBettaCollection"/> types with the
    /// shared <see cref="ComponentRegistry"/>.
    ///
    /// After startup scan, optionally watches the same folder via a
    /// <see cref="FileSystemWatcher"/> and reacts to drops:
    ///   - new path → byte-load into a fresh ALC, discover, register, refresh toolbar.
    ///   - existing path with a newer file → unload the old ALC, drop the
    ///     descriptors/proxies/factories that came from it, then byte-load
    ///     the replacement into a fresh ALC and re-publish.
    ///
    /// Known limitations of the hot-reload path (documented honestly):
    /// <list type="bullet">
    /// <item><description><b>Pinned closed generics.</b>
    /// <c>Param_BettaGoo&lt;T&gt;</c> for an opaque type <c>T</c> from the old
    /// plugin stays alive as long as the old <c>T</c> is reachable. The proxy
    /// catalog in GH may still hold references through previously-published
    /// param proxies — even after our explicit removal — and the closed-generic
    /// type itself roots a static GUID field. New components from the reloaded
    /// plugin use the NEW closed generics; memory leaks slowly across many
    /// reload cycles. Acceptable for v0.5.</description></item>
    /// <item><description><b>Live document references.</b> If a user wires a
    /// value of an opaque type into a panel/wire, the GH document holds a
    /// strong reference to the plugin's type system. ALC unload is lazy:
    /// <see cref="AssemblyLoadContext.Unload"/> succeeds, but actual memory
    /// release waits for the user to delete the wire. Acceptable.</description></item>
    /// <item><description><b>In-flight Tasks.</b> A method that returned an
    /// uncompleted Task may have continuations referencing the now-unloaded
    /// types. We do not cancel per-component CTSes from here — the components
    /// themselves manage that on solve. If a reload happens mid-solve the
    /// continuation may throw on completion. Best-effort.</description></item>
    /// </list>
    /// </summary>
    public sealed class PluginLoader : IDisposable
    {
        private readonly ComponentRegistry _registry;
        private readonly IServiceCollection _services;
        private readonly string _folder;
        private readonly ILogger<PluginLoader> _logger;

        // Path → loaded Assembly. Mirrors what was previously here; kept so
        // ResolveSiblingDependency can short-circuit re-entrant probes.
        private readonly ConcurrentDictionary<string, Assembly> _loadedByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Assembly> _loadedByName = new(StringComparer.OrdinalIgnoreCase);

        // Simple name → companion DEPENDENCY assembly resolved into the shared,
        // NON-collectible Default context (never a per-plugin collectible ALC). See
        // ResolveSiblingDependency for why this distinction is load-bearing.
        private readonly ConcurrentDictionary<string, Assembly> _sharedByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sharedLoadLock = new object();

        // Path → ALC that owns the assembly. One ALC per plugin DLL — that's
        // the granularity at which we can unload.
        private readonly ConcurrentDictionary<string, AssemblyLoadContext> _alcByPath = new(StringComparer.OrdinalIgnoreCase);

        // Reload debounce: FileSystemWatcher fires Changed multiple times for a
        // single rebuild (truncate + write + close). Drop events for the same
        // path inside this window.
        private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(750);

        private FileSystemWatcher _watcher;

        private readonly PluginTrustPolicy _trust;

        public PluginLoader(
            ComponentRegistry registry,
            IServiceCollection services,
            string folder,
            ILogger<PluginLoader> logger = null,
            PluginTrustPolicy trust = null)
        {
            _registry = registry;
            _services = services;
            _folder = folder;
            _logger = logger ?? NullLogger<PluginLoader>.Instance;
            // Off by default — loading behavior unchanged for existing installs.
            _trust = trust ?? PluginTrustPolicy.Off();

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
        /// Begin watching the plugin folder for DLL drops. New arrivals are
        /// loaded, their descriptors discovered and published, and GH's toolbar
        /// is asked to refresh. An overwritten DLL triggers the hot-reload path:
        /// previous ALC unloaded, descriptors purged, new bytes loaded into a
        /// fresh ALC, components re-published.
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
            // Debounce: a single rebuild often fires Changed several times.
            var now = DateTime.UtcNow;
            var last = _lastSeen.GetOrAdd(e.FullPath, DateTime.MinValue);
            if (now - last < DebounceWindow) return;
            _lastSeen[e.FullPath] = now;

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
            // Classify WITHOUT loading into the executing runtime. A dependency DLL must never be
            // byte-loaded into a collectible ALC here: that copy is only lazily unloadable, and a
            // plugin scanned later binds its reference straight to it → "Resolving to a collectible
            // assembly is not supported" (0x80131515). Non-plugins are materialized on demand into
            // the shared, non-collectible Default context by ResolveSiblingDependency instead.
            if (!PluginClassifier.IsPluginDll(path, _folder, _logger))
            {
                _logger.LogDebug("Skipping {Dll}: not a Betta plugin (dependency — resolves into Default on demand)", path);
                return;
            }

            Assembly asm;
            try
            {
                asm = LoadViaBytes(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin load raised for {Path} — skipping", path);
                try { Rhino.RhinoApp.WriteLine($"[Betta] Plugin load raised for {Path.GetFileName(path)}: {ex.Message}"); } catch { }
                return;
            }
            if (asm == null) return;

            bool hasCollection;
            try
            {
                hasCollection = ContainsBettaCollection(asm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IBettaCollection scan raised for {Path} — skipping", path);
                try { Rhino.RhinoApp.WriteLine($"[Betta] IBettaCollection scan failed for {Path.GetFileName(path)}: {ex.Message}"); } catch { }
                return;
            }
            if (!hasCollection)
            {
                // Reached only via PluginClassifier's fail-open path (it errored and returned true
                // for a DLL that is actually a dependency). Release the throwaway collectible copy
                // as a best-effort fallback; the primary defense — never byte-loading dependencies —
                // is the IsPluginDll gate above. (Unload() is lazy, so this alone is not sufficient,
                // which is exactly why the gate exists.)
                _logger.LogDebug("Skipping {Dll}: no IBettaCollection types (dependency; releasing collectible fallback copy)", path);
                ReleaseNonPluginCopy(path, asm);
                return;
            }

            try
            {
                var descriptors = _registry.DiscoverFromAssembly(asm);
                _registry.AutoRegisterServices(_services, asm);
                _logger.LogInformation("Loaded plugin {Asm} with {Count} components",
                    asm.GetName().Name, descriptors.Count);
            }
            catch (Exception ex)
            {
                // One bad plugin must not abort the whole scan. Log loud, keep going —
                // other plugins (and the framework itself) still come up. This mirrors
                // the module-loader's per-module resiliency.
                _logger.LogError(ex, "Discovery raised for {Path} — components from this plugin will NOT be published",
                    path);
                try
                {
                    Rhino.RhinoApp.WriteLine(
                        $"[Betta] Plugin discovery failed for {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}. Other plugins still loading.");
                }
                catch { }
            }
        }

        private void LoadOneAtRuntime(string path)
        {
            // Hot-reload path: same DLL path already loaded → user rebuilt and
            // overwrote. Unload the old ALC, drop its descriptors/proxies, then
            // re-load fresh.
            if (_loadedByPath.TryGetValue(path, out var existing))
            {
                var name = Path.GetFileName(path);
                _logger.LogInformation("Detected replaced plugin {Path} — beginning hot reload", path);

                var oldAsm = existing;
                var removed = _registry.UnregisterAssembly(oldAsm);
                var droppedFactories = ParamRegistry.UnregisterAssembly(oldAsm);

                if (_alcByPath.TryRemove(path, out var oldAlc))
                {
                    try
                    {
                        oldAlc.Unload();
                        _logger.LogInformation("Unloaded ALC for {Asm} ({Removed} descriptors, {Factories} param factories evicted)",
                            oldAsm.GetName().Name, removed, droppedFactories);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "ALC unload threw for {Asm} — continuing with reload",
                            oldAsm.GetName().Name);
                    }
                }

                _loadedByPath.TryRemove(path, out _);
                _loadedByName.TryRemove(oldAsm.GetName().Name ?? "", out _);

                // We do NOT block on a GC pass here — ALC unload is lazy and the
                // CLR won't free until the next collection anyway. Synchronously
                // pumping GC blocks the Rhino UI thread for no benefit.

                var fresh = LoadViaBytes(path);
                if (fresh == null || !ContainsBettaCollection(fresh))
                {
                    _logger.LogWarning("Reload of {Path} produced no IBettaCollection types — toolbar will be empty for this plugin", path);
                    SafeUpdateRibbon();
                    return;
                }

                var newDescriptors = _registry.DiscoverFromAssembly(fresh);
                foreach (var d in newDescriptors)
                    _registry.RegisterDescriptor(d);

                _logger.LogInformation("Registering {Count} components from new {Asm}",
                    newDescriptors.Count, fresh.GetName().Name);

                SafeUpdateRibbon();
                return;
            }

            // First-time drop at runtime. Same guard as startup: never collectible-load a
            // dependency DLL dropped into the folder — classify by metadata first.
            if (!PluginClassifier.IsPluginDll(path, _folder, _logger))
            {
                _logger.LogDebug("Runtime drop {Dll}: not a Betta plugin — ignoring", path);
                return;
            }
            var asm = LoadViaBytes(path);
            if (asm == null || !ContainsBettaCollection(asm)) return;

            var descriptors = _registry.DiscoverFromAssembly(asm);
            foreach (var d in descriptors)
                _registry.RegisterDescriptor(d);

            SafeUpdateRibbon();

            _logger.LogInformation("Runtime-loaded plugin {Asm} with {Count} new components",
                asm.GetName().Name, descriptors.Count);
        }

        private void SafeUpdateRibbon()
        {
            try
            {
                GH_ComponentServer.UpdateRibbonUI();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Toolbar refresh failed (components still registered)");
            }
        }

        private Assembly LoadViaBytes(string path)
        {
            try
            {
                if (_loadedByPath.TryGetValue(path, out var cached)) return cached;

                // Signature check — off by default. In Enforce mode a failure
                // returns null, dropping the DLL entirely; in WarnOnly the DLL
                // still loads but a warning is logged. Runs once per path;
                // subsequent hits use the _loadedByPath cache above.
                var verdict = PluginTrustVerifier.Verify(path, _trust);
                if (!verdict.Trusted)
                {
                    _logger.LogWarning(
                        "Plugin trust: {File} rejected — {Reason} (mode={Mode})",
                        Path.GetFileName(path), verdict.Reason, _trust.Mode);
                    if (_trust.Mode == PluginTrustMode.Enforce)
                        return null;
                }

                var bytes = File.ReadAllBytes(path);
                var pdbPath = Path.ChangeExtension(path, ".pdb");
                var pdbBytes = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;

                // Per-plugin collectible ALC. The name carries the file so log
                // dumps and AppDomain.GetAssemblies traces can tell them apart.
                var alcName = $"Betta.Plugin:{Path.GetFileName(path)}";
                var alc = new AssemblyLoadContext(alcName, isCollectible: true);
                _alcByPath[path] = alc;

                Assembly asm;
                using (var pe = new MemoryStream(bytes))
                {
                    if (pdbBytes != null)
                    {
                        using var pdb = new MemoryStream(pdbBytes);
                        asm = alc.LoadFromStream(pe, pdb);
                    }
                    else
                    {
                        asm = alc.LoadFromStream(pe);
                    }
                }

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
        /// Byte-stream loading does not inherit the LoadFrom directory probe.
        /// If a plugin DLL sits next to its own dependencies in our folder,
        /// the CLR asks us to resolve them on demand. We answer from the
        /// folder, loading each dependency into a fresh collectible ALC too
        /// (so siblings unload with the plugin that owns them — first-touch
        /// wins).
        /// </summary>
        private Assembly ResolveSiblingDependency(object sender, ResolveEventArgs args)
        {
            var simpleName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(simpleName)) return null;

            if (_sharedByName.TryGetValue(simpleName, out var already)) return already;

            var candidate = Path.Combine(_folder, simpleName + ".dll");
            if (!File.Exists(candidate)) return null;

            // CRITICAL: resolve a plugin's companion dependency into the SHARED,
            // NON-collectible Default context — NOT a per-file collectible ALC (as
            // LoadViaBytes would). This handler is hooked to the process-wide
            // AppDomain.AssemblyResolve, so whatever it returns may be handed to ANY
            // requester: another plugin's collectible ALC, Grasshopper's own
            // (non-collectible) .gha loader, or an unrelated third-party plugin. Returning
            // a *collectible* assembly to a non-collectible / different-context request is
            // illegal — the runtime throws NotSupportedException, surfaced as
            // "Could not load file or assembly '…'. Operation is not supported. (0x80131515)".
            // That single mistake previously broke every plugin shipping companion DLLs
            // (its own Abstractions/Core, a shared Betta.Preview, etc.) AND poisoned
            // unrelated .gha files whose dependency merely shared a name with a DLL in this
            // folder. A Default-context dependency is safe to return to anyone.
            return LoadDependencyShared(candidate, simpleName);
        }

        /// <summary>
        /// Load a plugin's companion dependency DLL into the shared, non-collectible
        /// <see cref="AssemblyLoadContext.Default"/> via a byte stream (file stays unlocked,
        /// so the dev rebuild/xcopy loop keeps working). Reuses an instance already present
        /// in the Default context — e.g. one Grasshopper's own loader pulled in for a
        /// classic <c>.gha</c> that shipped the same DLL — so a given dependency unifies to
        /// ONE type identity across Betta plugins and classic plugins alike. Trade-off:
        /// Default-context assemblies do not unload on plugin hot-reload, so a changed
        /// dependency needs a full Rhino restart (its owning plugin still hot-reloads).
        /// </summary>
        private Assembly LoadDependencyShared(string path, string simpleName)
        {
            lock (_sharedLoadLock)
            {
                if (_sharedByName.TryGetValue(simpleName, out var already)) return already;

                var existing = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
                    string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    _sharedByName[simpleName] = existing;
                    return existing;
                }

                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var pdbPath = Path.ChangeExtension(path, ".pdb");

                    Assembly asm;
                    using (var pe = new MemoryStream(bytes))
                    {
                        if (File.Exists(pdbPath))
                        {
                            using var pdb = new MemoryStream(File.ReadAllBytes(pdbPath));
                            asm = AssemblyLoadContext.Default.LoadFromStream(pe, pdb);
                        }
                        else
                        {
                            asm = AssemblyLoadContext.Default.LoadFromStream(pe);
                        }
                    }

                    _sharedByName[simpleName] = asm;
                    _logger.LogDebug("Resolved dependency {Name} into shared Default context from {Path}",
                        simpleName, path);
                    return asm;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load dependency {Path} into shared Default context", path);
                    return null;
                }
            }
        }

        /// <summary>
        /// Drop the throwaway collectible copy of a DLL that turned out to have no
        /// <see cref="IBettaCollection"/> types (a library dependency, not a plugin) and unload
        /// its ALC, so a plugin depending on it later binds to a shared, non-collectible copy
        /// (served by <see cref="ResolveSiblingDependency"/>) instead of this collectible one —
        /// which the runtime refuses to resolve to ("Resolving to a collectible assembly is not
        /// supported", 0x80131515).
        /// </summary>
        private void ReleaseNonPluginCopy(string path, Assembly asm)
        {
            var name = asm?.GetName().Name;
            _loadedByPath.TryRemove(path, out _);
            if (!string.IsNullOrEmpty(name) &&
                _loadedByName.TryGetValue(name, out var cached) && ReferenceEquals(cached, asm))
            {
                _loadedByName.TryRemove(name, out _);
            }
            if (_alcByPath.TryRemove(path, out var alc))
            {
                try { alc.Unload(); }
                catch (Exception ex) { _logger.LogDebug(ex, "Unload of dependency ALC for {Path} failed (non-fatal)", path); }
            }
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

        /// <summary>
        /// All plugin assemblies loaded by this PluginLoader so far. Used at
        /// startup to invoke IBettaModule.ConfigureServices on each plugin
        /// before BuildServiceProvider.
        /// </summary>
        public IEnumerable<Assembly> LoadedAssemblies => _loadedByPath.Values;
    }
}
