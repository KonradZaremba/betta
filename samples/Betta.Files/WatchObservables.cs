// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Betta.Files
{
    /// <summary>
    /// Watches a whole folder. On subscribe it emits an initial snapshot of ALL
    /// matching files (Changed/Kind/When empty); on each settled change it
    /// re-emits the full folder listing with the changed file flagged. So
    /// downstream always sees the complete folder plus what just changed.
    /// </summary>
    internal sealed class FolderStateObservable : IObservable<FolderState>
    {
        private readonly string _folder;
        private readonly string _filter;
        private readonly bool _recursive;
        private readonly int _debounceMs;

        public FolderStateObservable(string folder, string filter, bool recursive, int debounceMs)
        {
            _folder = folder;
            _filter = string.IsNullOrWhiteSpace(filter) ? "*.*" : filter;
            _recursive = recursive;
            _debounceMs = debounceMs;
        }

        public IDisposable Subscribe(IObserver<FolderState> observer)
        {
            var composite = new CompositeDisposable();

            // Initial full snapshot, deferred one tick so it doesn't fire
            // synchronously inside Subscribe (which runs during a solve).
            composite.Add(new Timer(_ => observer.OnNext(Snapshot(string.Empty, string.Empty, string.Empty)),
                null, 1, Timeout.Infinite));

            // Watch for changes only when the folder actually exists; an unset or
            // missing path still gets the (empty) initial snapshot above.
            if (!string.IsNullOrWhiteSpace(_folder) && Directory.Exists(_folder))
            {
                composite.Add(new DebouncedFolderWatch(_folder, _filter, _recursive, _debounceMs,
                    (path, kind) => observer.OnNext(Snapshot(path, kind, DateTime.Now.ToString("O")))));
            }

            return composite;
        }

        private FolderState Snapshot(string changed, string kind, string when)
            => new FolderState(ListFiles(), changed, kind, when);

        private List<string> ListFiles()
            => Directory.Exists(_folder)
                ? Directory.EnumerateFiles(
                    _folder, _filter,
                    _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList()
                : new List<string>();
    }

    /// <summary>
    /// Watches a single file. Emits its current state on subscribe (Kind =
    /// "Existing") if it's there, then a FileChange each time it settles.
    /// Implemented by watching the parent directory filtered to the file name.
    /// </summary>
    internal sealed class FileChangeObservable : IObservable<FileChange>
    {
        private readonly string _file;
        private readonly int _debounceMs;

        public FileChangeObservable(string file, int debounceMs)
        {
            _file = file;
            _debounceMs = debounceMs;
        }

        public IDisposable Subscribe(IObserver<FileChange> observer)
        {
            var composite = new CompositeDisposable();

            composite.Add(new Timer(_ =>
            {
                if (File.Exists(_file))
                    observer.OnNext(new FileChange(_file, "Existing", DateTime.Now.ToString("O")));
            }, null, 1, Timeout.Infinite));

            var dir = Path.GetDirectoryName(_file);
            var name = Path.GetFileName(_file);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                composite.Add(new DebouncedFolderWatch(dir, name, false, _debounceMs,
                    (path, kind) => observer.OnNext(new FileChange(path, kind, DateTime.Now.ToString("O")))));
            }

            return composite;
        }
    }
}
