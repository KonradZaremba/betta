// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Betta.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Betta.Services
{
    /// <summary>
    /// Scans assemblies for concrete <see cref="IBettaModule"/> implementors
    /// and invokes their ConfigureServices method on the shared
    /// IServiceCollection — letting plugin authors register their own DI
    /// services before BuildServiceProvider runs.
    /// </summary>
    public static class ModuleLoader
    {
        /// <summary>
        /// Discover IBettaModule types in an assembly, instantiate each via
        /// parameterless ctor, and call ConfigureServices(services). A faulty
        /// module is logged but does not break startup.
        /// </summary>
        public static int InvokeFromAssembly(
            Assembly assembly,
            IServiceCollection services,
            ILogger logger = null)
        {
            logger ??= NullLogger.Instance;
            if (assembly == null) return 0;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            var moduleTypes = types
                .Where(t => t != null && t.IsClass && !t.IsAbstract &&
                            typeof(IBettaModule).IsAssignableFrom(t))
                .ToList();

            int invoked = 0;
            foreach (var moduleType in moduleTypes)
            {
                try
                {
                    var module = (IBettaModule)Activator.CreateInstance(moduleType);
                    module.ConfigureServices(services);
                    invoked++;
                    logger.LogInformation("  + module {Module} from {Asm}",
                        moduleType.FullName, assembly.GetName().Name);
                }
                catch (Exception ex)
                {
                    // One bad module must not kill startup. The other modules
                    // (and the rest of Betta) should still come up.
                    logger.LogError(ex, "Module {Module} from {Asm} failed in ConfigureServices",
                        moduleType.FullName, assembly.GetName().Name);
                    // Visible failure: file-only logs go unseen by plugin
                    // authors — dump to Rhino's console too so a broken
                    // ConfigureServices is impossible to miss. Guarded because
                    // RhinoApp isn't available under unit tests.
                    try
                    {
                        Rhino.RhinoApp.WriteLine(
                            $"[Betta] Module '{moduleType.FullName}' in {assembly.GetName().Name} failed: {ex.Message}. Component skipped its DI hook; see Betta.log for the full stack.");
                    }
                    catch { /* headless / no Rhino */ }
                }
            }
            return invoked;
        }

        /// <summary>
        /// Convenience: invoke modules across many assemblies in order.
        /// </summary>
        public static int InvokeFromAssemblies(
            IEnumerable<Assembly> assemblies,
            IServiceCollection services,
            ILogger logger = null)
        {
            int total = 0;
            foreach (var asm in assemblies)
                total += InvokeFromAssembly(asm, services, logger);
            return total;
        }
    }
}
