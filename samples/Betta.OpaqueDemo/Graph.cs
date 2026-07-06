// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using Betta.Interfaces;
using Betta.Preview;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Betta.OpaqueDemo
{
    /// <summary>
    /// A tiny domain object: a graph of points and the lines between them.
    /// Demonstrates the full Betta opt-in stack on one class:
    ///   IBettaValue        — opaque, single typed wire (no property explosion)
    ///   IBettaPreview      — draws in the Rhino viewport
    ///   IBettaBakeable     — right-click → bake produces real Rhino lines
    ///   IBettaSerializable — round-trips through GH save/reload
    ///   IBettaDefault      — unwired inputs get a fresh empty Graph
    /// </summary>
    public class Graph : IBettaValue, IBettaPreview, IBettaBakeable, IBettaSerializable, IBettaDefault
    {
        public List<Point3d> Nodes { get; } = new();
        public List<Line> Edges { get; } = new();

        public BoundingBox ClippingBox
        {
            get
            {
                var bb = BoundingBox.Empty;
                foreach (var p in Nodes) bb.Union(p);
                foreach (var e in Edges) bb.Union(e.BoundingBox);
                return bb;
            }
        }

        public void DrawWires(IGH_PreviewArgs args)
        {
            var color = args.WireColour_Selected;
            foreach (var e in Edges) args.Display.DrawLine(e, color, 2);
        }

        public void DrawMeshes(IGH_PreviewArgs args)
        {
            // Render nodes as small markers so the graph reads on the canvas.
            foreach (var p in Nodes) args.Display.DrawPoint(p, PointStyle.RoundSimple, 4, args.WireColour_Selected);
        }

        // ---- IBettaBakeable: right-click → bake adds Rhino lines + points to the doc.
        public void Bake(Rhino.RhinoDoc doc, ObjectAttributes att, List<System.Guid> obj_ids)
        {
            foreach (var line in Edges) obj_ids.Add(doc.Objects.AddLine(line, att));
            foreach (var node in Nodes) obj_ids.Add(doc.Objects.AddPoint(node, att));
        }

        // ---- IBettaSerializable: persist nodes + edges through GH save/reload.
        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Nodes.Count);
            foreach (var p in Nodes) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }
            w.Write(Edges.Count);
            foreach (var e in Edges)
            {
                w.Write(e.FromX); w.Write(e.FromY); w.Write(e.FromZ);
                w.Write(e.ToX);   w.Write(e.ToY);   w.Write(e.ToZ);
            }
            return ms.ToArray();
        }

        public void LoadFromBytes(byte[] bytes)
        {
            Nodes.Clear(); Edges.Clear();
            if (bytes == null || bytes.Length == 0) return;
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            int nodeCount = r.ReadInt32();
            for (int i = 0; i < nodeCount; i++)
                Nodes.Add(new Point3d(r.ReadDouble(), r.ReadDouble(), r.ReadDouble()));
            int edgeCount = r.ReadInt32();
            for (int i = 0; i < edgeCount; i++)
            {
                var from = new Point3d(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
                var to   = new Point3d(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
                Edges.Add(new Line(from, to));
            }
        }
    }
}
