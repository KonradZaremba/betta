// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Betta.Goo;
using Grasshopper.Kernel;

namespace Betta.Services
{
    /// <summary>
    /// Process-wide registry of opaque-type → Param_BettaGoo&lt;T&gt; factories.
    /// ComponentRegistry walks each discovered method's parameter and return
    /// types and calls RegisterOpaque(T) for any type declared opaque (via
    /// attribute or marker), so ParamVector can look up the typed param at
    /// component-build time. Idempotent — re-registering a type is a no-op.
    /// </summary>
    public static class ParamRegistry
    {
        private static readonly ConcurrentDictionary<Type, Func<IGH_Param>> _factories = new();

        public static void RegisterOpaque(Type t)
        {
            if (t == null) return;
            // GH_BettaGoo<T> / Param_BettaGoo<T> both constrain T to class,
            // so reference types only — silently ignore value types.
            if (t.IsValueType) return;
            _factories.GetOrAdd(t, tt =>
            {
                var paramType = typeof(Param_BettaGoo<>).MakeGenericType(tt);
                return () => (IGH_Param)Activator.CreateInstance(paramType);
            });
        }

        public static bool TryGetFactory(Type t, out Func<IGH_Param> factory) =>
            _factories.TryGetValue(t, out factory);

        public static bool IsRegistered(Type t) => t != null && _factories.ContainsKey(t);

        /// <summary>
        /// Drop every factory whose key Type lives in <paramref name="asm"/>.
        /// Called by the hot-reload path before an AssemblyLoadContext unload.
        ///
        /// Note: the closed-generic Param_BettaGoo&lt;T&gt; types themselves
        /// live in the Betta runtime ALC (the default context), so they
        /// outlive the plugin. The factory removal only drops the
        /// Activator.CreateInstance reference — the closed generic stays
        /// rooted by GH_ComponentServer's proxy catalog for any param
        /// instances that were already published. See ROADMAP / hot-reload
        /// limitations.
        /// </summary>
        public static int UnregisterAssembly(Assembly asm)
        {
            if (asm == null) return 0;
            var keys = _factories.Keys.Where(t => t.Assembly == asm).ToList();
            foreach (var k in keys) _factories.TryRemove(k, out _);
            return keys.Count;
        }
    }
}
