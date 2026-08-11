// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using Betta.Rendering;
using Betta.Services;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// IconProvider is GH-free (System.Drawing + embedded resources), so the whole
    /// fish pipeline is testable headlessly.
    ///
    /// These lock user-visible invariants that fail *silently*: if the GUID→fish
    /// mapping drifts, every component quietly changes icon and nothing throws.
    ///
    /// Bitmaps returned by GetOrCreate are shared process-wide (one per fish) —
    /// tests must never dispose them.
    /// </summary>
    public class TestIconProvider
    {
        // Real values via InternalsVisibleTo — mirrored consts drift when the
        // geometry gets retuned (24/48/96 have all been tried). The literal
        // user-facing "icons are 24×24" contract is asserted exactly once, in
        // Icon_IsTwentyFourSquare.
        private const int ContentSize = IconProvider.IconSize - 2 * IconProvider.Padding;

        /// <summary>
        /// Build a Guid whose first ToByteArray() byte is <paramref name="first"/> —
        /// that byte alone selects the fish. The Guid(byte[]) ctor round-trips
        /// byte 0 through ToByteArray, so this controls the pick exactly.
        /// </summary>
        private static Guid GuidWithFirstByte(byte first)
        {
            var bytes = new byte[16];
            bytes[0] = first;
            return new Guid(bytes);
        }

        private static ComponentDescriptor Descriptor(byte firstGuidByte) =>
            new ComponentDescriptor { Guid = GuidWithFirstByte(firstGuidByte) };

        /// <summary>
        /// Independent reference implementation of the non-transparent bounding
        /// box, so the expected crop is computed here rather than trusting the
        /// private CropToContent. LockBits + one alpha-channel sweep instead of
        /// GetPixel — the sources are 256×256 and GetPixel would cost ~1s/run.
        /// </summary>
        private static (int X, int Y, int W, int H) ContentBounds(Bitmap bmp)
        {
            var data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var buf = new byte[Math.Abs(data.Stride) * bmp.Height];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
                for (int y = 0; y < bmp.Height; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (buf[row + x * 4 + 3] == 0) continue; // alpha byte of BGRA
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
                return maxX < 0 ? (0, 0, 0, 0) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        [Fact]
        public void FishLibrary_HasTheSixExpectedSilhouettes()
        {
            Assert.Equal(6, SessionFish.Count);
            Assert.Equal(6, SessionFish.All.Count);
            Assert.Equal(
                new[] { "Amber", "Aqua", "Cosmic", "Forest", "Lime", "Rose" },
                SessionFish.Names.ToArray());
            Assert.All(SessionFish.All, Assert.NotNull);
        }

        [Fact]
        public void NullDescriptor_ReturnsNull()
        {
            Assert.Null(IconProvider.GetOrCreate(null));
        }

        [Fact]
        public void Icon_IsTwentyFourSquare()
        {
            // 24×24 is the Grasshopper standard; 48 overlapped parameter labels.
            var icon = IconProvider.GetOrCreate(Descriptor(0));

            Assert.NotNull(icon);
            // Deliberately the literal, not IconProvider.IconSize: 24×24 is the
            // user-facing Grasshopper contract, and this is its one guardian.
            Assert.Equal(24, icon.Width);
            Assert.Equal(24, icon.Height);
        }

        [Theory]
        [InlineData(4, 4)]    // identical GUID → identical fish (session/machine stability)
        [InlineData(0, 6)]
        [InlineData(1, 7)]
        [InlineData(2, 254)]  // 254 % 6 == 2
        [InlineData(5, 11)]
        public void PickWrapsModuloFishCount(byte left, byte right)
        {
            // The pick is guid[0] % 6, so bytes congruent mod 6 share the same
            // Bitmap instance. Covers same-GUID determinism (row 4,4) and the
            // modulo wrap in one mechanism.
            Assert.Same(
                IconProvider.GetOrCreate(Descriptor(left)),
                IconProvider.GetOrCreate(Descriptor(right)));
        }

        [Fact]
        public void EveryFishIsReachable_AndDistinct()
        {
            // Bytes 0..5 must map onto all six distinct rendered bitmaps: a
            // mapping bug that collapsed the range would show up here.
            var icons = Enumerable.Range(0, 6)
                .Select(i => IconProvider.GetOrCreate(Descriptor((byte)i)))
                .ToList();

            Assert.Equal(6, icons.Distinct(ReferenceEqualityComparer.Instance).Count());
        }

        [Fact]
        public void DifferentFish_RenderDifferentPixels()
        {
            // Reference-distinct is not enough — the six PNGs should actually
            // differ visually. Compares raw pixels of two fish.
            var a = IconProvider.GetOrCreate(Descriptor(0));
            var b = IconProvider.GetOrCreate(Descriptor(1));

            bool anyDifference = false;
            for (int y = 0; y < a.Height && !anyDifference; y++)
                for (int x = 0; x < a.Width && !anyDifference; x++)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) anyDifference = true;

            Assert.True(anyDifference, "Two different fish rendered identical pixels.");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void Icon_IsLetterboxedInsidePaddedBox_NotStretched(int fishIndex)
        {
            // One pass over each fish asserting both geometry contracts:
            // aspect ratio preserved (letterboxed, not stretched) AND the fish
            // scaled to exactly the padded content box.
            var icon = IconProvider.GetOrCreate(Descriptor((byte)fishIndex));
            var source = SessionFish.All[fishIndex];

            var src = ContentBounds(source);
            var drawn = ContentBounds(icon);

            Assert.True(drawn.W > 0 && drawn.H > 0, "Rendered icon was fully transparent.");

            // Aspect bound deliberately tight. The sources crop to 223×188
            // (aspect 1.186) and land at 18×15 (aspect 1.200) — a ratio of ~1.01.
            // Stretching to fill the tile would give 18×18, i.e. ratio ~0.84, so
            // a loose bound here would pass the very bug this test exists to
            // catch. Rounding at 18px is the only slack that needs absorbing.
            var expectedAspect = (double)src.W / src.H;
            var actualAspect = (double)drawn.W / drawn.H;
            Assert.InRange(actualAspect / expectedAspect, 0.95, 1.10);

            // The longer edge is scaled to exactly ContentSize, and the bicubic
            // pass does not bleed past it (SourceCopy compositing, fully
            // transparent surround) — so this is an equality, not a tolerance.
            Assert.Equal(ContentSize, Math.Max(drawn.W, drawn.H));
        }

        [Fact]
        public void IconResource_BypassesTheFishSystem()
        {
            // A plugin shipping its own PNG opts out entirely: the image is used
            // verbatim, not cropped/resampled into a 24×24 fish tile.
            var d = new ComponentDescriptor
            {
                Guid = GuidWithFirstByte(0),
                ServiceType = typeof(ComponentDescriptor), // any type in the Betta assembly
                IconResource = "Fish_Amber.png",           // embedded as Betta.Resources.Fish_Amber.png
            };

            var icon = IconProvider.GetOrCreate(d);

            Assert.NotNull(icon);
            Assert.NotSame(IconProvider.GetOrCreate(Descriptor(0)), icon);
            // Loaded verbatim, so it keeps the source PNG's dimensions.
            Assert.True(icon.Width > IconProvider.IconSize && icon.Height > IconProvider.IconSize,
                $"Expected the raw resource, got a {icon.Width}×{icon.Height} bitmap.");
        }

        [Fact]
        public void MissingIconResource_FallsBackToAFish()
        {
            var d = new ComponentDescriptor
            {
                Guid = GuidWithFirstByte(2),
                ServiceType = typeof(ComponentDescriptor),
                IconResource = "NoSuchImage.png",
            };

            var icon = IconProvider.GetOrCreate(d);

            Assert.NotNull(icon);
            Assert.Equal(IconProvider.IconSize, icon.Width);
            Assert.Same(IconProvider.GetOrCreate(Descriptor(2)), icon);
        }

        [Fact]
        public void NullServiceType_WithIconResource_FallsBackToAFish()
        {
            // Guards the asm == null branch — a descriptor without a service type
            // must degrade to a fish rather than throw.
            var d = new ComponentDescriptor
            {
                Guid = GuidWithFirstByte(1),
                ServiceType = null,
                IconResource = "Fish_Amber.png",
            };

            var icon = IconProvider.GetOrCreate(d);

            Assert.NotNull(icon);
            Assert.Equal(IconProvider.IconSize, icon.Width);
        }
    }
}
