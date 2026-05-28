// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Betta.Services
{
    /// <summary>
    /// Global lookup so any code path with only a component Guid can reach the
    /// descriptor that produced it (tests, diagnostics, fallback rehydration).
    /// Populated once by ComponentRegistry during PriorityLoad.
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
    }
}
