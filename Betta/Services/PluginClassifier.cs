// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Extensions.Logging;

namespace Betta.Services
{
    /// <summary>
    /// Decides whether a DLL in the plugin folder is a Betta PLUGIN (declares an
    /// <c>IBettaCollection</c> type) or merely a library DEPENDENCY — WITHOUT loading it into
    /// the executing runtime. This is load-bearing: the folder scan must never byte-load a
    /// dependency into a collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/>,
    /// because that copy is only lazily unloadable and, once created, the runtime binds a
    /// dependent plugin's reference straight to it and throws "Resolving to a collectible
    /// assembly is not supported" (0x80131515). Dependencies must instead be materialized on
    /// demand into the shared, non-collectible Default context (see
    /// <c>PluginLoader.ResolveSiblingDependency</c> → <c>LoadDependencyShared</c>).
    ///
    /// Two steps, cheapest first:
    ///   A. <b>PEReader over the assembly-ref table</b> — a real plugin MUST reference
    ///      <c>Betta.Abstractions</c> (home of <c>IBettaCollection</c> and every
    ///      <c>[Grasshopper*]</c> attribute). No such ref ⇒ definitely a dependency. No ALC,
    ///      no type load, file opened shared so nothing is locked.
    ///   B. <b>MetadataLoadContext confirm</b> (reflection-only) — some type implements
    ///      <c>"Betta.Interfaces.IBettaCollection"</c> by full-name string. Only reached for the
    ///      few DLLs that pass A. The context memory-maps the file, so it is created and
    ///      disposed within the call to avoid holding a lock over the watched folder.
    ///
    /// Fail-open: any error ⇒ return <c>true</c> (treat as a plugin), so the caller falls back
    /// to the legacy collectible-load + <c>ContainsBettaCollection</c> path. A classifier bug
    /// can therefore never make plugins silently vanish — worst case is the prior behavior.
    /// </summary>
    internal static class PluginClassifier
    {
        private const string MarkerInterface = "Betta.Interfaces.IBettaCollection";
        private const string AbstractionsAssembly = "Betta.Abstractions";

        public static bool IsPluginDll(string path, string pluginFolder, ILogger logger)
        {
            try
            {
                // Step A — cheap gate. A Betta plugin always references Betta.Abstractions.
                if (!ReferencesAbstractions(path))
                    return false;

                // Step B — confirm it actually declares an IBettaCollection type.
                return ImplementsCollection(path, pluginFolder);
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Plugin classification failed for {Path}; treating as plugin (fail-open)", path);
                return true;
            }
        }

        private static bool ReferencesAbstractions(string path)
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return false;

            var mr = pe.GetMetadataReader();
            foreach (var handle in mr.AssemblyReferences)
            {
                var name = mr.GetString(mr.GetAssemblyReference(handle).Name);
                if (string.Equals(name, AbstractionsAssembly, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool ImplementsCollection(string path, string pluginFolder)
        {
            var probe = new List<string>();
            if (Directory.Exists(pluginFolder))
                probe.AddRange(Directory.GetFiles(pluginFolder, "*.dll"));

            // Betta.Abstractions.dll sits in the Grasshopper Libraries root next to Betta.gha,
            // NOT in the plugin subfolder. Without it on the probe path MetadataLoadContext
            // cannot resolve IBettaCollection and every plugin is misclassified as a dependency.
            var librariesRoot = Path.GetDirectoryName(pluginFolder);
            if (!string.IsNullOrEmpty(librariesRoot) && Directory.Exists(librariesRoot))
                probe.AddRange(Directory.GetFiles(librariesRoot, "*.dll"));

            // The running .NET runtime dir (System.Private.CoreLib.dll etc.).
            var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            if (Directory.Exists(runtimeDir))
                probe.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));

            var resolver = new ToleratingResolver(new PathAssemblyResolver(probe.Distinct()));
            using var mlc = new MetadataLoadContext(resolver, coreAssemblyName: "System.Private.CoreLib");

            var asm = mlc.LoadFromAssemblyPath(path);
            foreach (var t in SafeGetTypes(asm))
            {
                if (t == null) continue;
                Type[] ifaces;
                try { ifaces = t.GetInterfaces(); }
                catch { continue; } // a base type/interface lives in an unresolvable dll — skip
                foreach (var i in ifaces)
                    if (i.FullName == MarkerInterface) return true;
            }
            return false;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }

            var result = new List<Type>();
            foreach (var t in types)
                if (t != null) result.Add(t);
            return result;
        }

        /// <summary>
        /// Wraps a <see cref="PathAssemblyResolver"/> so unresolved references return null
        /// instead of throwing — the marker check only needs Betta.Abstractions resolvable, so a
        /// plugin that also imports RhinoCommon/Grasshopper (not on the probe path) still reads.
        /// </summary>
        private sealed class ToleratingResolver : MetadataAssemblyResolver
        {
            private readonly PathAssemblyResolver _inner;
            public ToleratingResolver(PathAssemblyResolver inner) => _inner = inner;

            public override Assembly Resolve(MetadataLoadContext context, AssemblyName name)
            {
                try { return _inner.Resolve(context, name); }
                catch (FileNotFoundException) { return null; }
                catch (FileLoadException) { return null; }
            }
        }
    }
}
