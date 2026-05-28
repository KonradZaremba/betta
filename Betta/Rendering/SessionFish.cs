// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Betta.Rendering
{
    /// <summary>
    /// The embedded betta silhouettes that ship with Betta.gha.
    /// Loaded once on first access (Lazy) and shared for the rest of the
    /// process; callers MUST NOT dispose the returned bitmaps.
    ///
    /// Each fish PNG has a transparent background and is drawn in the same pose
    /// (fan tail on the left, head facing right). They all share the same
    /// silhouette family, so picking a different fish per component still
    /// satisfies "same silhouette" — the only thing that varies between
    /// components is the artist's coloration.
    ///
    /// Use <see cref="All"/> + an index for per-component pick (typical),
    /// or <see cref="Names"/>/<see cref="Count"/> for diagnostics.
    /// </summary>
    public static class SessionFish
    {
        private static readonly string[] _resourceNames =
        {
            "Betta.Resources.Fish_Amber.png",
            "Betta.Resources.Fish_Aqua.png",
            "Betta.Resources.Fish_Cosmic.png",
            "Betta.Resources.Fish_Forest.png",
            "Betta.Resources.Fish_Lime.png",
            "Betta.Resources.Fish_Rose.png",
        };

        private static readonly string[] _displayNames =
        {
            "Amber", "Aqua", "Cosmic", "Forest", "Lime", "Rose"
        };

        private static readonly Lazy<Bitmap[]> _all = new(LoadAll);

        public static IReadOnlyList<Bitmap> All => _all.Value;
        public static IReadOnlyList<string> Names => _displayNames;
        public static int Count => _resourceNames.Length;

        private static Bitmap[] LoadAll()
        {
            var asm = typeof(SessionFish).Assembly;
            var bitmaps = new Bitmap[_resourceNames.Length];
            for (int i = 0; i < _resourceNames.Length; i++)
                bitmaps[i] = LoadEmbedded(asm, _resourceNames[i]);
            return bitmaps;
        }

        private static Bitmap LoadEmbedded(Assembly asm, string resourceName)
        {
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
            // Copy through a memory stream so the assembly stream can close
            // without invalidating the bitmap.
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
    }
}
