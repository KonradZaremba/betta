// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Betta.Files
{
    /// <summary>
    /// A live view of a watched folder. Betta's class-return explosion turns
    /// this into four output pins: <c>AllPaths</c> (a list of every matching
    /// file in the folder) plus the scalar <c>Changed</c> / <c>Kind</c> /
    /// <c>When</c> describing the file that triggered this emission. On the
    /// initial snapshot, Changed/Kind/When are empty — AllPaths alone lists
    /// everything already there.
    /// </summary>
    public sealed class FolderState
    {
        /// <summary>Every matching file currently in the folder.</summary>
        public List<string> AllPaths { get; }

        /// <summary>Path of the file that changed (empty on the initial snapshot).</summary>
        public string Changed { get; }

        /// <summary>Created / Changed / Deleted / Renamed (empty on the initial snapshot).</summary>
        public string Kind { get; }

        /// <summary>Round-trip ("O") timestamp of the change (empty on the initial snapshot).</summary>
        public string When { get; }

        public FolderState(List<string> allPaths, string changed, string kind, string when)
        {
            AllPaths = allPaths;
            Changed = changed;
            Kind = kind;
            When = when;
        }
    }
}
