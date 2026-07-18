// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        private const int IconSize = 24;   // IconProvider.IconSize
        private const int Padding = 3;     // IconProvider.Padding
        private const int ContentSize = IconSize - 2 * Padding;

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
        /// private CropToContent.
        /// </summary>
        private static (int X, int Y, int W, int H) ContentBounds(Bitmap bmp, int alphaThreshold = 0)
        {
            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, y).A <= alphaThreshold) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX < 0 ? (0, 0, 0, 0) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        [Fact]
        public void GuidHelper_ControlsTheSelectingByte()
        {
            // Guards the tests below: if Guid(byte[]) ever stopped round-tripping
            // byte 0, every determinism assertion here would be vacuous.
            for (byte b = 0; b < 12; b++)
                Assert.Equal(b, GuidWithFirstByte(b).ToByteArray()[0]);
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
            Assert.Equal(IconSize, icon.Width);
            Assert.Equal(IconSize, icon.Height);
        }

        [Fact]
        public void SameDescriptor_IsCachedAndReturnsSameInstance()
        {
            var d = Descriptor(3);

            var first = IconProvider.GetOrCreate(d);
            var second = IconProvider.GetOrCreate(d);

            Assert.Same(first, second);
        }

        [Fact]
        public void SameGuid_PicksSameFish_AcrossDescriptors()
        {
            // Two independently-built descriptors that hash to the same GUID must
            // land on the same fish — this is what makes the pick stable across
            // sessions and machines.
            var a = IconProvider.GetOrCreate(Descriptor(4));
            var b = IconProvider.GetOrCreate(Descriptor(4));

            Assert.Same(a, b);
        }

        [Theory]
        [InlineData(0, 6)]
        [InlineData(1, 7)]
        [InlineData(2, 254)]  // 254 % 6 == 2
        [InlineData(5, 11)]
        public void PickWrapsModuloFishCount(byte left, byte right)
        {
            // The pick is guid[0] % 6, so bytes congruent mod 6 share a fish.
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
            for (int y = 0; y < IconSize && !anyDifference; y++)
                for (int x = 0; x < IconSize && !anyDifference; x++)
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
        public void Icon_IsLetterboxed_NotStretched(int fishIndex)
        {
            // The fish must keep its natural aspect ratio inside the tile. If the
            // resample ever stretched to fill, the drawn content would go square
            // (aspect 1.0) — this compares against the aspect of the source PNG's
            // own content box, computed independently below.
            var icon = IconProvider.GetOrCreate(Descriptor((byte)fishIndex));
            var source = SessionFish.All[fishIndex];

            var src = ContentBounds(source);
            var drawn = ContentBounds(icon);

            Assert.True(drawn.W > 0 && drawn.H > 0, "Rendered icon was fully transparent.");

            var expectedAspect = (double)src.W / src.H;
            var actualAspect = (double)drawn.W / drawn.H;

            // Bound deliberately tight. The sources crop to 223×188 (aspect 1.186)
            // and land at 18×15 (aspect 1.200) — a ratio of ~1.01. Stretching to
            // fill the tile would give 18×18, i.e. ratio ~0.84, so a loose bound
            // here would pass the very bug this test exists to catch. Rounding at
            // 18px is the only slack that needs absorbing.
            Assert.InRange(actualAspect / expectedAspect, 0.95, 1.10);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(5)]
        public void Icon_FitsInsideThePaddedContentBox(int fishIndex)
        {
            // Padding keeps the fish off the tile edges; the longer drawn edge
            // should reach about ContentSize and never the full 24.
            var icon = IconProvider.GetOrCreate(Descriptor((byte)fishIndex));
            var drawn = ContentBounds(icon);

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
            Assert.True(icon.Width > IconSize && icon.Height > IconSize,
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
            Assert.Equal(IconSize, icon.Width);
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
            Assert.Equal(IconSize, icon.Width);
        }
    }
}
