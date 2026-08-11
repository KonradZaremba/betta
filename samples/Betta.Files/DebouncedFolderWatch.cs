// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Betta.Files
{
    /// <summary>
    /// Shared FileSystemWatcher + per-path debounce, used by both watch
    /// observables. A single save fires a burst of raw events (Create + several
    /// Changed); this coalesces the burst, per path, into ONE
    /// <c>onSettled(path, kind)</c> callback after a quiet window — the fix for
    /// the well-known GH_FileWatcher "multiple recomputes" problem. Disposing
    /// tears the watcher and all pending timers down; Betta's streaming runtime
    /// calls Dispose when inputs change or the component leaves the canvas.
    /// </summary>
    internal sealed class DebouncedFolderWatch : IDisposable
    {
        private readonly int _debounceMs;
        private readonly Action<string, string> _onSettled;
        private readonly FileSystemWatcher _watcher;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Timer> _pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lastKind = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public DebouncedFolderWatch(string folder, string filter, bool recursive, int debounceMs, Action<string, string> onSettled)
        {
            _debounceMs = debounceMs;
            _onSettled = onSettled;
            _watcher = new FileSystemWatcher(folder, string.IsNullOrWhiteSpace(filter) ? "*.*" : filter)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += (_, e) => Bump(e.FullPath, "Created");
            _watcher.Changed += (_, e) => Bump(e.FullPath, "Changed");
            _watcher.Deleted += (_, e) => Bump(e.FullPath, "Deleted");
            _watcher.Renamed += (_, e) => Bump(e.FullPath, "Renamed");
            _watcher.EnableRaisingEvents = true;
        }

        // (Re)start a per-path timer on every raw event; emit only once it goes
        // quiet. Last kind seen in the window wins.
        private void Bump(string path, string kind)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _lastKind[path] = kind;
                if (_pending.TryGetValue(path, out var timer))
                {
                    timer.Change(_debounceMs, Timeout.Infinite);
                }
                else
                {
                    _pending[path] = new Timer(_ => Fire(path), null, _debounceMs, Timeout.Infinite);
                }
            }
        }

        private void Fire(string path)
        {
            string kind;
            lock (_gate)
            {
                if (_disposed) return;
                if (_pending.TryGetValue(path, out var timer))
                {
                    timer.Dispose();
                    _pending.Remove(path);
                }
                if (!_lastKind.TryGetValue(path, out kind)) kind = "Changed";
                _lastKind.Remove(path);
            }
            _onSettled(path, kind);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                foreach (var timer in _pending.Values) timer.Dispose();
                _pending.Clear();
                _lastKind.Clear();
            }
        }
    }

    /// <summary>Disposes a set of disposables together (initial-snapshot timer + watcher).</summary>
    internal sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items = new();
        private bool _disposed;

        public void Add(IDisposable item)
        {
            if (_disposed) { item?.Dispose(); return; }
            _items.Add(item);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var item in _items) item?.Dispose();
            _items.Clear();
        }
    }
}
