// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Betta.Files
{
    /// <summary>
    /// One settled filesystem change emitted by the Watch components. A plain
    /// class with simple string properties, so Betta's class-return explosion
    /// turns it into three output pins automatically — Path / Kind / When —
    /// with no opaque wrapper and no hand-written GH_Param.
    /// </summary>
    public sealed class FileChange
    {
        /// <summary>Full path of the file that changed.</summary>
        public string Path { get; }

        /// <summary>What happened: Created, Changed, Deleted or Renamed.</summary>
        public string Kind { get; }

        /// <summary>Round-trip ("O") timestamp of the settled change.</summary>
        public string When { get; }

        public FileChange(string path, string kind, string when)
        {
            Path = path;
            Kind = kind;
            When = when;
        }
    }
}
