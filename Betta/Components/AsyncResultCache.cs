// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Betta.Components
{
    /// <summary>
    /// Result store for async service methods: a bounded LRU of delivered results plus
    /// an in-flight registry that dedupes concurrent solves with the same key. GH-free so
    /// it is testable headlessly; <see cref="BettaComponent"/> owns one per instance and
    /// does the Grasshopper side (launching the task, runtime messages, ExpireSolution).
    /// </summary>
    internal sealed class AsyncResultCache
    {
        internal enum Lookup { Hit, InFlight, Miss }

        private readonly object _lock = new();
        private readonly LinkedList<KeyValuePair<string, object>> _order = new();
        private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, object>>> _results = new();
        private readonly Dictionary<string, Task> _inFlight = new();
        private readonly int _max;

        public AsyncResultCache(int max) { _max = max; }

        public int Count { get { lock (_lock) return _results.Count; } }

        /// <summary>
        /// Hit returns the stored result and moves it to the most-recently-used end.
        /// InFlight means the same compute is already running — let its continuation
        /// expire the solution. Miss means the caller should launch the task.
        /// </summary>
        public Lookup TryGet(string key, out object result)
        {
            lock (_lock)
            {
                if (_results.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddLast(node);
                    result = node.Value.Value;
                    return Lookup.Hit;
                }
                result = null;
                return _inFlight.ContainsKey(key) ? Lookup.InFlight : Lookup.Miss;
            }
        }

        public void MarkInFlight(string key, Task task)
        {
            lock (_lock) { _inFlight[key] = task; }
        }

        /// <summary>Called first in the task's continuation, faulted or not.</summary>
        public void ClearInFlight(string key)
        {
            lock (_lock) { _inFlight.Remove(key); }
        }

        /// <summary>
        /// Stores a completed result; the first result for a key wins. Evicts the
        /// least-recently-used entries past the bound.
        /// </summary>
        public void Store(string key, object result)
        {
            lock (_lock)
            {
                if (_results.ContainsKey(key)) return;
                _results[key] = _order.AddLast(new KeyValuePair<string, object>(key, result));
                while (_results.Count > _max)
                {
                    var oldest = _order.First;
                    _order.RemoveFirst();
                    _results.Remove(oldest.Value.Key);
                }
            }
        }

        /// <summary>
        /// Drops delivered results. See BettaComponent.EvictAsyncResults for why a
        /// delivered result must not outlive the solve that read it.
        /// </summary>
        public void Evict(IEnumerable<string> keys)
        {
            lock (_lock)
            {
                foreach (var key in keys)
                {
                    if (_results.TryGetValue(key, out var node))
                    {
                        _order.Remove(node);
                        _results.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Structural cache key for a method call. Primitives, strings,
        /// enums, decimals, and Rhino.Geometry value types use their stable
        /// ToString. Collections recurse element-wise. Other reference types
        /// fall back to TypeName + RuntimeHelpers.GetHashCode (identity-ish)
        /// — better than naive ToString collisions, but documented as a
        /// best-effort key for non-value reference types.
        /// </summary>
        public static string KeyFor(object[] args)
        {
            var sb = new System.Text.StringBuilder(args.Length * 16);
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append('|');
                AppendArgKey(sb, args[i]);
            }
            return sb.ToString();
        }

        private static void AppendArgKey(System.Text.StringBuilder sb, object a)
        {
            if (a == null) { sb.Append('n'); return; }
            var t = a.GetType();

            // Synthetic per-solve params are NOT real inputs: the runtime hands
            // the method a fresh CancellationToken and a fresh IProgress<T> sink
            // on EVERY solve. Their instance identity therefore changes each
            // pass — and if they leak into the key (the reference/boxed-struct
            // fallback below hashes instance identity), the async cache never
            // hits, so every solve relaunches the task while BeforeSolveInstance
            // cancels the prior one: an infinite "computing" livelock. Fold them
            // to a constant so the key depends only on the real wired inputs.
            if (a is CancellationToken) { sb.Append("ct"); return; }
            if (a is Betta.Interfaces.IBettaReport) { sb.Append("rp"); return; }
            if (t.GetInterfaces().Any(i => i.IsGenericType &&
                                           i.GetGenericTypeDefinition() == typeof(IProgress<>)))
            { sb.Append("pg"); return; }

            if (t.IsPrimitive || a is string || a is DateTime || a is decimal)
            { sb.Append(t.Name); sb.Append(':'); sb.Append(a); return; }

            if (t.IsEnum)
            { sb.Append("e:"); sb.Append(t.Name); sb.Append(':'); sb.Append(Convert.ToInt64(a)); return; }

            if (t.Namespace == "Rhino.Geometry")
            { sb.Append('r'); sb.Append(t.Name); sb.Append(':'); sb.Append(a); return; }

            if (a is System.Collections.IEnumerable enumerable && a is not string)
            {
                sb.Append('[');
                foreach (var item in enumerable) { AppendArgKey(sb, item); sb.Append(','); }
                sb.Append(']'); return;
            }

            // Value-type fallback (structs not special-cased above: Guid, Color,
            // Interval, Transform, BoundingBox…). Use their ToString — it is
            // deterministic for a given value, unlike the boxed instance's
            // identity hash, which changes every solve and would defeat the
            // cache the same way the synthetic params do.
            if (t.IsValueType)
            { sb.Append('v'); sb.Append(t.FullName); sb.Append(':'); sb.Append(a); return; }

            // Reference-type fallback: type name + identity hash. Stable for the
            // same object, collides only for genuinely identical references —
            // not great for mutable value-bag classes, but doesn't silently
            // merge unrelated instances the way bare ToString did.
            sb.Append('o'); sb.Append(t.FullName); sb.Append(':');
            sb.Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a));
        }
    }
}
