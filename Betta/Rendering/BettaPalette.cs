// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Drawing;

namespace Betta.Rendering
{
    /// <summary>
    /// Curated palette of betta morphs — each entry takes its name and color
    /// cues from a real betta fish coloration variety. Keep entries here
    /// hand-tuned rather than algorithmically generated; the whole point is
    /// that each morph reads as intentional, not random.
    ///
    /// SessionMorph.Current picks one of these (or, 1-in-200, the Wild
    /// off-palette morph) once per Rhino session and applies it across every
    /// generated component icon.
    /// </summary>
    public static class BettaPalette
    {
        public static readonly IReadOnlyList<BettaMorph> Morphs = new[]
        {
            new BettaMorph("Halfmoon Red",  Color.FromArgb(176, 30, 36),  Color.FromArgb(220, 50, 55),  Color.FromArgb(245, 220, 100)),
            new BettaMorph("Koi",           Color.FromArgb(245, 240, 230), Color.FromArgb(240, 130, 60), Color.FromArgb(40, 40, 40)),
            new BettaMorph("Galaxy",        Color.FromArgb(35, 30, 70),    Color.FromArgb(140, 80, 200), Color.FromArgb(220, 230, 255)),
            new BettaMorph("Mustard Gas",   Color.FromArgb(60, 90, 50),    Color.FromArgb(220, 200, 60), Color.FromArgb(230, 230, 220)),
            new BettaMorph("Dragon Scale",  Color.FromArgb(180, 40, 50),   Color.FromArgb(240, 230, 220), Color.FromArgb(60, 60, 60)),
            new BettaMorph("Black Orchid",  Color.FromArgb(35, 30, 45),    Color.FromArgb(80, 50, 100),  Color.FromArgb(180, 100, 200)),
            new BettaMorph("Cellophane",    Color.FromArgb(245, 235, 220), Color.FromArgb(220, 200, 180), Color.FromArgb(200, 80, 80)),
            new BettaMorph("Marble",        Color.FromArgb(220, 220, 230), Color.FromArgb(50, 60, 130),   Color.FromArgb(200, 60, 80)),
            new BettaMorph("Copper",        Color.FromArgb(150, 90, 50),   Color.FromArgb(190, 130, 70), Color.FromArgb(60, 100, 80)),
            new BettaMorph("Salamander",    Color.FromArgb(170, 70, 50),   Color.FromArgb(200, 100, 70), Color.FromArgb(210, 180, 120)),
            new BettaMorph("Steel Blue",    Color.FromArgb(50, 90, 130),   Color.FromArgb(80, 130, 180), Color.FromArgb(220, 220, 230)),
            new BettaMorph("Avatar",        Color.FromArgb(40, 70, 130),   Color.FromArgb(70, 150, 200), Color.FromArgb(220, 230, 255)),
        };

        /// <summary>
        /// 1-in-200 Easter egg: an off-palette neon morph picked when the
        /// session-wide roll lands on it. Surfaces in the log line and tooltip
        /// so the user knows they got a rare one.
        /// </summary>
        public static readonly BettaMorph Wild =
            new BettaMorph("Wild", Color.FromArgb(255, 50, 200), Color.FromArgb(50, 255, 200), Color.FromArgb(255, 240, 60));
    }
}
