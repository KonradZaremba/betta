// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Rendering
{
    /// <summary>
    /// One betta morph picked per Rhino session, applied to every generated
    /// component icon. Restart Rhino, get a different morph; the family
    /// silhouette stays the same, the individual is new.
    ///
    /// 1-in-200 chance per session of returning the off-palette Wild morph
    /// (see BettaPalette.Wild) — a small Easter egg.
    /// </summary>
    public static class SessionMorph
    {
        private static readonly Lazy<BettaMorph> _current = new(Pick);

        public static BettaMorph Current => _current.Value;

        private static BettaMorph Pick()
        {
            var rng = new Random();
            if (rng.Next(200) == 0) return BettaPalette.Wild;
            var palette = BettaPalette.Morphs;
            return palette[rng.Next(palette.Count)];
        }
    }
}
