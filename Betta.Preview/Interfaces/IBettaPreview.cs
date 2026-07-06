// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Betta.Preview
{
    /// <summary>
    /// Opt-in viewport-draw hook for opaque Betta domain values. When an
    /// opaque value returned from a Betta method implements this interface,
    /// the wrapping component implements IGH_PreviewData and forwards
    /// DrawViewportWires / DrawViewportMeshes / ClippingBox to the value.
    ///
    /// Implement on your domain type — the Betta runtime detects the
    /// interface by name string, so the Betta.gha assembly does not take a
    /// hard reference to this package (which would force every plugin to drag
    /// RhinoCommon transitively).
    /// </summary>
    public interface IBettaPreview
    {
        BoundingBox ClippingBox { get; }
        void DrawWires(IGH_PreviewArgs args);
        void DrawMeshes(IGH_PreviewArgs args);
    }

    /// <summary>
    /// Optional extension to <see cref="IBettaPreview"/> — implement to add
    /// right-click → Bake support so the user can drop your opaque value's
    /// geometry into the Rhino document as real objects. Detected
    /// independently of IBettaPreview, so a type can support bake without
    /// preview or vice versa.
    /// </summary>
    public interface IBettaBakeable
    {
        /// <summary>
        /// Append baked Guid(s) to <paramref name="obj_ids"/> for whatever
        /// geometry the value represents. The component layer marshals the
        /// active document and the attributes — the value owns what to bake.
        /// </summary>
        void Bake(RhinoDoc doc, ObjectAttributes att, List<System.Guid> obj_ids);
    }
}
