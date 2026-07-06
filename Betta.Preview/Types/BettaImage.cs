// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Betta.Attributes;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Betta.Preview
{
    /// <summary>
    /// First-class image value that flows through Betta as a single typed
    /// wire (no property explosion) and knows how to <b>bake to a Rhino
    /// PictureFrame</b> and <b>serialize to PNG</b>. Plugin authors returning
    /// generated / loaded / processed images should return <c>BettaImage</c>
    /// rather than raw <c>System.Drawing.Bitmap</c> — that gets them a stable
    /// display, deterministic serialization, and one-click bake for free.
    ///
    /// <para>
    /// Marked <c>[GrasshopperOpaque]</c> so Betta's runtime auto-generates a
    /// <c>Param_BettaGoo&lt;BettaImage&gt;</c> and passes it as a single
    /// typed wire between components. Implements <see cref="IBettaBakeable"/>
    /// so right-click → Bake places a PictureFrame in the Rhino doc.
    /// </para>
    ///
    /// <para>
    /// Deliberately does <b>not</b> implement <see cref="IBettaPreview"/> in
    /// v0.6 — Rhino's viewport draws are not the right surface for a raster
    /// preview (best done via a canvas thumbnail on the component itself,
    /// which is a v0.7 concern once <c>IBettaInteractive</c> lands). Users
    /// still see the value in Panels via <see cref="ToString"/>.
    /// </para>
    /// </summary>
    [GrasshopperOpaque]
    public class BettaImage : IBettaBakeable, IDisposable
    {
        /// <summary>Underlying pixel buffer. Never null once constructed.</summary>
        public Bitmap Bitmap { get; }

        /// <summary>
        /// Optional display name — surfaced by <see cref="ToString"/> and
        /// stamped onto the baked PictureFrame's object name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Bake location. Defaults to the world XY plane at the origin,
        /// sized so 1 pixel = 1 unit. Set to override for real-world sizing
        /// or a different viewing plane.
        /// </summary>
        public Plane? BakePlane { get; set; }

        /// <summary>
        /// Real-world width for the PictureFrame in Rhino units. Null means
        /// "use the bitmap's pixel width" (1 px = 1 unit).
        /// </summary>
        public double? RealWorldWidth { get; set; }

        /// <summary>
        /// Real-world height. Null means "preserve aspect ratio against
        /// <see cref="RealWorldWidth"/>" or "use the bitmap's pixel height"
        /// if width is also null.
        /// </summary>
        public double? RealWorldHeight { get; set; }

        public BettaImage(Bitmap bitmap, string name = null)
        {
            Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
            Name = name;
        }

        /// <summary>
        /// Load a BettaImage from raw PNG bytes. Useful for round-tripping
        /// through storage or as the receiving side of an HTTP response body.
        /// </summary>
        public static BettaImage FromPng(byte[] data, string name = null)
        {
            if (data == null || data.Length == 0) return null;
            using var ms = new MemoryStream(data);
            // new Bitmap(Stream) keeps the stream open; copy through a new
            // Bitmap so callers can dispose the source safely.
            using var loaded = new Bitmap(ms);
            var copy = new Bitmap(loaded);
            return new BettaImage(copy, name);
        }

        /// <summary>Serialize the wrapped bitmap as PNG.</summary>
        public byte[] ToPng()
        {
            using var ms = new MemoryStream();
            Bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        public void Bake(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
        {
            if (doc == null || Bitmap == null) return;

            // Persist the pixels to a temp file — Rhino's PictureFrame API
            // takes a file path, not an in-memory bitmap. Kept in the user's
            // temp folder so bake output survives session shutdown until the
            // OS reclaims it.
            var tempDir = Path.Combine(Path.GetTempPath(), "Betta", "images");
            Directory.CreateDirectory(tempDir);
            var fileName = SafeFileName(Name) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
            var path = Path.Combine(tempDir, fileName);
            Bitmap.Save(path, ImageFormat.Png);

            var plane = BakePlane ?? Plane.WorldXY;
            double w = RealWorldWidth ?? Bitmap.Width;
            double h = RealWorldHeight ?? (RealWorldWidth.HasValue
                ? RealWorldWidth.Value * Bitmap.Height / Bitmap.Width
                : Bitmap.Height);

            // PictureFrame constructor takes a plane + width + height +
            // image path. Rhino handles the material and mesh generation.
            var id = doc.Objects.AddPictureFrame(plane, path, false, w, h, false, false);
            if (id != Guid.Empty)
            {
                if (!string.IsNullOrEmpty(Name))
                {
                    var obj = doc.Objects.FindId(id);
                    if (obj != null)
                    {
                        var effective = att ?? doc.CreateDefaultAttributes();
                        effective.Name = Name;
                        doc.Objects.ModifyAttributes(obj, effective, true);
                    }
                }
                obj_ids?.Add(id);
            }
        }

        private static string SafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "betta_image";
            foreach (var ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch, '_');
            return s.Length > 64 ? s.Substring(0, 64) : s;
        }

        public override string ToString()
        {
            var name = string.IsNullOrEmpty(Name) ? "image" : Name;
            return $"{name} ({Bitmap.Width}×{Bitmap.Height})";
        }

        public void Dispose() => Bitmap?.Dispose();
    }
}
