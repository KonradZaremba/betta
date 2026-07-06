// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Betta.Services
{
    /// <summary>
    /// Global lookup so any code path with only a component Guid can reach the
    /// descriptor that produced it (tests, diagnostics, fallback rehydration).
    /// Populated once by ComponentRegistry during PriorityLoad. Mutated by the
    /// hot-reload path when a plugin DLL is replaced — see
    /// <see cref="RemoveByAssembly(Assembly)"/>.
    /// </summary>
    public static class DescriptorCache
    {
        private static readonly ConcurrentDictionary<Guid, ComponentDescriptor> _byGuid = new();

        public static void Add(ComponentDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            _byGuid[descriptor.Guid] = descriptor;
        }

        public static bool TryGet(Guid guid, out ComponentDescriptor descriptor)
            => _byGuid.TryGetValue(guid, out descriptor);

        public static ICollection<ComponentDescriptor> All => _byGuid.Values;

        /// <summary>
        /// Drop every descriptor whose <see cref="ComponentDescriptor.ServiceType"/>
        /// lives in <paramref name="asm"/>. Used by the hot-reload path before
        /// the old AssemblyLoadContext is unloaded so the cache does not
        /// re-resolve dangling types after the unload.
        /// </summary>
        public static int RemoveByAssembly(Assembly asm)
        {
            if (asm == null) return 0;
            var toRemove = _byGuid
                .Where(kv => kv.Value.ServiceType?.Assembly == asm)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var g in toRemove) _byGuid.TryRemove(g, out _);
            return toRemove.Count;
        }
    }
}
