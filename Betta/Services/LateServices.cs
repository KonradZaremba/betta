// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Betta.Services
{
    /// <summary>
    /// Service instances for plugins dropped into the Betta folder while Rhino runs.
    /// The ServiceProvider is built once at startup, so a hot-loaded plugin's
    /// components find no binding for their service type. <see cref="Resolve"/> then
    /// creates the implementation <see cref="ComponentRegistry.AutoRegisterServices"/>
    /// would have bound, with constructor injection from the provider, and keeps it
    /// as a singleton. A plugin's IBettaModule still doesn't run mid-session, so an
    /// implementation that needs services only its module registers stays unresolved
    /// until Rhino restarts. GH-free.
    /// </summary>
    public static class LateServices
    {
        private static readonly ConcurrentDictionary<Type, object> _instances = new();
        private static readonly object _createLock = new();

        /// <summary>
        /// The provider's instance of <paramref name="serviceType"/>, or else one created
        /// from its implementation. Null when there is none, or when creating it fails
        /// (reported through <paramref name="onFailure"/>).
        /// </summary>
        public static object Resolve(IServiceProvider provider, Type serviceType, Action<Exception> onFailure = null)
        {
            if (serviceType == null) return null;
            var service = provider?.GetService(serviceType);
            if (service != null || _instances.TryGetValue(serviceType, out service)) return service;

            lock (_createLock)
            {
                if (_instances.TryGetValue(serviceType, out service)) return service;
                var impl = FindImplementation(serviceType, LoadableTypes(serviceType.Assembly));
                if (impl == null) return null;
                try
                {
                    service = provider != null
                        ? ActivatorUtilities.CreateInstance(provider, impl)
                        : Activator.CreateInstance(impl);
                }
                catch (Exception ex)
                {
                    onFailure?.Invoke(ex);
                    return null;
                }
                _instances[serviceType] = service;
                return service;
            }
        }

        /// <summary>
        /// The concrete type a service binds to: the first non-abstract implementer
        /// among <paramref name="candidates"/>; a class service type is its own.
        /// </summary>
        public static Type FindImplementation(Type serviceType, Type[] candidates) =>
            candidates.FirstOrDefault(t => t != null && !t.IsAbstract && !t.IsInterface && serviceType.IsAssignableFrom(t));

        /// <summary>
        /// Drop every instance whose service type lives in <paramref name="asm"/>.
        /// Called by the hot-reload path before an AssemblyLoadContext unload.
        /// </summary>
        public static int UnregisterAssembly(Assembly asm)
        {
            if (asm == null) return 0;
            var keys = _instances.Keys.Where(t => t.Assembly == asm).ToList();
            foreach (var k in keys) _instances.TryRemove(k, out _);
            return keys.Count;
        }

        private static Type[] LoadableTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
        }
    }
}
