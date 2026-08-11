// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using Betta.Rendering;

namespace Betta.Services
{
    /// <summary>
    /// Produces the toolbar/component icon for a descriptor. Resolution order:
    ///   1. Embedded resource named by [GrasshopperMethod(IconResource=...)]
    ///      looked up in the service type's own assembly. Plugins can ship
    ///      their own PNGs and opt out of the fish system entirely.
    ///   2. One of the embedded betta silhouettes, picked deterministically
    ///      from the descriptor's GUID. No recoloring — the source PNGs already
    ///      carry rich, hand-drawn gradients and line-art detail; tinting on top
    ///      would destroy them. Same silhouette family + each component its own
    ///      coloration, straight from the artist.
    ///
    /// Pipeline per fish (runs once at startup, results cached):
    ///   1. Crop the source PNG to its non-transparent bounding box. The raw
    ///      images carry transparent padding; cropping gives more pixels per
    ///      fish at the final icon size.
    ///   2. Two-step bicubic downscale, letterboxed into a square so the
    ///      fish's natural aspect ratio is preserved (no stretching).
    ///
    /// Output is 24×24 — the Grasshopper standard. The Mini source PNGs are
    /// designed with bold outlines and solid fills that hold up cleanly at this
    /// size, and 24×24 fits the icon slot of a typical component without
    /// overlapping the input/output parameter labels (48×48 was tried and
    /// pushed into the label area).
    /// </summary>
    public static class IconProvider
    {
        // IconSize and Padding are internal (not private) so TestBetta asserts
        // against the real values rather than mirroring them.
        internal const int IconSize = 24;
        private const int InterimSize = 64; // for the two-step downscale
        internal const int Padding = 3;     // margin inside the tile so the fish
                                            // sits at "regular" icon size rather
                                            // than filling edge-to-edge

        // Each fish is rendered once and shared across every descriptor that
        // lands on it — typical case is a few components per fish, so this
        // saves redundant resampling work.
        private static readonly Lazy<Bitmap[]> _rendered = new(RenderAll);

        public static Bitmap GetOrCreate(ComponentDescriptor descriptor)
        {
            if (descriptor == null) return null;
            if (descriptor.CachedIcon != null) return descriptor.CachedIcon;

            var icon = LoadFromResource(descriptor) ?? PickRendered(descriptor);
            descriptor.CachedIcon = icon;
            return icon;
        }

        private static Bitmap LoadFromResource(ComponentDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(descriptor.IconResource)) return null;
            try
            {
                var asm = descriptor.ServiceType?.Assembly;
                if (asm == null) return null;

                var full = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(descriptor.IconResource, StringComparison.OrdinalIgnoreCase));
                if (full == null) return null;

                using var stream = asm.GetManifestResourceStream(full);
                if (stream == null) return null;
                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Pick a pre-rendered fish icon deterministically from the descriptor's
        /// GUID. Stable across sessions and machines because the GUID is itself
        /// MD5 of the method signature.
        /// </summary>
        private static Bitmap PickRendered(ComponentDescriptor d)
        {
            var icons = _rendered.Value;
            var idx = d.Guid.ToByteArray()[0] % icons.Length;
            return icons[idx];
        }

        private static Bitmap[] RenderAll()
        {
            return SessionFish.All.Select(RenderOne).ToArray();
        }

        private static Bitmap RenderOne(Bitmap source)
        {
            // Step 1: crop transparent padding. Maximizes the pixel budget the
            // downscale has to work with.
            var cropped = CropToContent(source);

            // Step 2: two-step downscale with aspect-preserving letterbox into
            // the final IconSize×IconSize bitmap.
            var letterboxed = LetterboxResample(cropped, IconSize);

            if (cropped != source) cropped.Dispose();
            return letterboxed;
        }

        /// <summary>
        /// Returns a tightly cropped copy of <paramref name="source"/> covering
        /// only its non-transparent pixels. If the source has no padding (or is
        /// entirely transparent), returns the source itself.
        /// </summary>
        private static Bitmap CropToContent(Bitmap source)
        {
            int minX = source.Width;
            int minY = source.Height;
            int maxX = -1;
            int maxY = -1;

            // GetPixel is slow but only runs once per fish at startup — the
            // 250×250 sweep is ~62k ops, microseconds.
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (source.GetPixel(x, y).A == 0) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0) return source; // entirely transparent
            var w = maxX - minX + 1;
            var h = maxY - minY + 1;
            if (w == source.Width && h == source.Height) return source; // already tight

            var cropped = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(cropped);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(
                source,
                new Rectangle(0, 0, w, h),
                new Rectangle(minX, minY, w, h),
                GraphicsUnit.Pixel);
            return cropped;
        }

        /// <summary>
        /// Resample <paramref name="source"/> into a square IconSize bitmap,
        /// preserving aspect ratio with transparent letterbox padding. Uses a
        /// two-step bicubic downscale for cleaner edges than a single big jump.
        /// </summary>
        private static Bitmap LetterboxResample(Bitmap source, int targetSize)
        {
            // Fit the fish inside a padded content box (targetSize - 2*Padding)
            // and center it, so it renders at a regular icon size with a margin
            // instead of touching the tile edges.
            var contentSize = Math.Max(1, targetSize - 2 * Padding);
            var scale = (double)contentSize / Math.Max(source.Width, source.Height);
            var finalW = Math.Max(1, (int)Math.Round(source.Width * scale));
            var finalH = Math.Max(1, (int)Math.Round(source.Height * scale));
            var offsetX = (targetSize - finalW) / 2;
            var offsetY = (targetSize - finalH) / 2;

            // Interim size: roughly InterimSize on the longer edge.
            var interimScale = (double)InterimSize / Math.Max(source.Width, source.Height);
            interimScale = Math.Min(interimScale, 1.0); // never upscale
            var interimW = Math.Max(1, (int)Math.Round(source.Width * interimScale));
            var interimH = Math.Max(1, (int)Math.Round(source.Height * interimScale));

            using var interim = new Bitmap(interimW, interimH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(interim))
            {
                ConfigureHighQuality(g);
                g.DrawImage(source, new Rectangle(0, 0, interimW, interimH));
            }

            var output = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(output))
            {
                ConfigureHighQuality(g);
                g.DrawImage(interim, new Rectangle(offsetX, offsetY, finalW, finalH));
            }
            return output;
        }

        private static void ConfigureHighQuality(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.CompositingMode = CompositingMode.SourceCopy;
        }
    }
}
