// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Betta.Attributes;
using Betta.Interfaces;

namespace Betta.Files
{
    /// <summary>
    /// The streaming half of Betta.Files, in its own subcategory
    /// (<b>Files › Watch</b>) so the live watchers sit apart from the plain
    /// read/write IO utilities. Both components return an
    /// <see cref="IObservable{T}"/>, so Betta drives them as live components:
    /// it subscribes, marshals each emission's re-solve to the Rhino UI thread,
    /// and disposes (tearing the watcher down) when the component leaves the
    /// canvas.
    /// </summary>
    [GrasshopperCollection("Files", "Watch")]
    public class FileWatchers : IBettaCollection
    {
        [GrasshopperMethod("Watch Folder",
            "Track a whole folder live. Outputs every matching file (AllPaths) plus the one that just changed. Starts with a full snapshot, then re-solves on each change.")]
        public IObservable<FolderState> WatchFolder(
            [GrasshopperParameter("Folder", "D", "Folder to watch"), GrasshopperNotEmpty] string folder,
            [GrasshopperParameter("Filter", "F", "Glob filter, e.g. *.txt", DefaultValue = "*.*")] string filter,
            [GrasshopperParameter("Recursive", "R", "Include sub-folders", DefaultValue = false)] bool recursive,
            [GrasshopperParameter("Debounce", "ms", "Quiet window before emitting, ms", DefaultValue = 250)] int debounceMs)
            => new FolderStateObservable(folder, filter, recursive, Math.Max(0, debounceMs));

        [GrasshopperMethod("Watch File",
            "Stream a FileChange each time this single file settles. Emits its current state on start.")]
        public IObservable<FileChange> WatchFile(
            [GrasshopperParameter("File", "P", "File to watch"), GrasshopperNotEmpty] string file,
            [GrasshopperParameter("Debounce", "ms", "Quiet window before emitting, ms", DefaultValue = 250)] int debounceMs)
            => new FileChangeObservable(file, Math.Max(0, debounceMs));
    }
}
