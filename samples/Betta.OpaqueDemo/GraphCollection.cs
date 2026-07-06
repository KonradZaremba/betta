// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Betta.Attributes;
using Betta.Interfaces;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Betta.OpaqueDemo
{
    public enum GraphKind
    {
        Triangle = 0,
        Square = 1,
        Star = 2,
    }

    /// <summary>
    /// Demonstrates the four new Betta features end-to-end:
    ///   1. Opaque pass-through       (Graph as a single typed wire — Make / Deconstruct).
    ///   2. List-of-opaque outputs    (Batch returns List&lt;Graph&gt;).
    ///   3. Enum-typed input          (Kind is a GraphKind integer/dropdown).
    ///   4. Viewport preview          (Graph implements IBettaPreview).
    ///   5. Constructor DI            (INamingService injected via OpaqueDemoModule).
    /// </summary>
    [GrasshopperCollection("OpaqueDemo", "Graph")]
    public class GraphCollection : IBettaCollection
    {
        // Ctor-injected via Feature 2 — OpaqueDemoModule.ConfigureServices
        // registered INamingService before BuildServiceProvider, and Betta's
        // AutoRegisterServices treats GraphCollection itself as a singleton
        // (a class is assignable from itself), so the DI container can
        // satisfy this dependency.
        private readonly INamingService _naming;

        public GraphCollection(INamingService naming)
        {
            _naming = naming;
        }

        [GrasshopperMethod("Make", "Build a graph from a centerpoint and a kind")]
        public Graph Make(
            [GrasshopperParameter("Center")] Point3d center,
            [GrasshopperParameter("Kind")] GraphKind kind)
        {
            return Build(center, kind);
        }

        [GrasshopperMethod("Label", "Friendly name for a kind, via injected service")]
        public string Label([GrasshopperParameter("Kind")] GraphKind kind) =>
            _naming.LabelFor(kind);

        // Validation: Scale clamps to [0.1, 10]. An out-of-range slider value
        // surfaces a Warning and the method body is skipped — the consumer
        // sees an empty output, not a crash inside Build.
        [GrasshopperMethod("Scaled", "Build a scaled graph, with a validated scale")]
        public Graph Scaled(
            [GrasshopperParameter("Center")] Point3d center,
            [GrasshopperParameter("Kind")] GraphKind kind,
            [GrasshopperParameter("Scale", DefaultValue = 1.0)]
            [GrasshopperRange(0.1, 10.0)] double scale,
            [GrasshopperParameter("Label"), GrasshopperNotEmpty] string label)
        {
            var g = Build(center, kind);
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                var p = g.Nodes[i];
                g.Nodes[i] = new Point3d(center.X + (p.X - center.X) * scale,
                                         center.Y + (p.Y - center.Y) * scale,
                                         center.Z);
            }
            g.Edges.Clear();
            for (int i = 0; i < g.Nodes.Count; i++)
                g.Edges.Add(new Line(g.Nodes[i], g.Nodes[(i + 1) % g.Nodes.Count]));
            return g;
        }

        // Tree input demo: take a GH_Structure<GH_Number> of seeds (one branch
        // per polygon) and produce a list of graphs. The whole tree is handed
        // to the method body intact — no per-branch iteration in user code.
        [GrasshopperMethod("Batch From Tree", "One graph per branch of a number tree")]
        public List<Graph> BatchFromTree(
            [GrasshopperParameter("Seeds")] GH_Structure<GH_Number> seeds,
            [GrasshopperParameter("Kind")] GraphKind kind)
        {
            var result = new List<Graph>();
            if (seeds == null) return result;
            foreach (var path in seeds.Paths)
            {
                var branch = seeds.get_Branch(path);
                double avg = 0; int n = 0;
                foreach (var item in branch)
                {
                    if (item is GH_Number num) { avg += num.Value; n++; }
                }
                if (n == 0) continue;
                avg /= n;
                var c = new Point3d(avg, avg, 0);
                result.Add(Build(c, kind));
            }
            return result;
        }

        [GrasshopperMethod("Deconstruct", "Pull nodes and edges out of a graph")]
        public (List<Point3d> Nodes, List<Line> Edges) Deconstruct(
            [GrasshopperParameter("Graph")] Graph graph)
        {
            return (graph?.Nodes ?? new(), graph?.Edges ?? new());
        }

        [GrasshopperMethod("Batch", "Build one graph per centerpoint")]
        public List<Graph> Batch(
            [GrasshopperParameter("Centers")] List<Point3d> centers,
            [GrasshopperParameter("Kind")] GraphKind kind)
        {
            var result = new List<Graph>();
            if (centers == null) return result;
            foreach (var c in centers) result.Add(Build(c, kind));
            return result;
        }

        private static Graph Build(Point3d center, GraphKind kind)
        {
            var g = new Graph();
            switch (kind)
            {
                case GraphKind.Triangle:
                    AddPolygon(g, center, sides: 3, radius: 1.0);
                    break;
                case GraphKind.Square:
                    AddPolygon(g, center, sides: 4, radius: 1.0);
                    break;
                case GraphKind.Star:
                    AddPolygon(g, center, sides: 5, radius: 1.0, twistEveryOther: true);
                    break;
            }
            return g;
        }

        private static void AddPolygon(Graph g, Point3d c, int sides, double radius, bool twistEveryOther = false)
        {
            var nodes = new List<Point3d>();
            for (int i = 0; i < sides; i++)
            {
                var t = (System.Math.PI * 2 * i) / sides;
                var r = twistEveryOther && (i % 2 == 1) ? radius * 0.4 : radius;
                nodes.Add(new Point3d(c.X + r * System.Math.Cos(t), c.Y + r * System.Math.Sin(t), c.Z));
            }
            g.Nodes.AddRange(nodes);
            for (int i = 0; i < nodes.Count; i++)
                g.Edges.Add(new Line(nodes[i], nodes[(i + 1) % nodes.Count]));
        }
    }
}
