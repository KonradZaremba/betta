using System.Collections.Generic;
using Betta.Attributes;
using Betta.Interfaces;
using Betta.Preview;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace MyBettaPlugin
{
    /// <summary>
    /// Opaque domain type (single typed wire) that ALSO draws itself in the
    /// Rhino viewport. The wrapping component picks up IBettaPreview at
    /// discovery and forwards Draw/Clipping calls automatically.
    /// </summary>
    [GrasshopperOpaque]
    public class Graph : IBettaPreview
    {
        public List<Point3d> Nodes { get; } = new List<Point3d>();
        public List<Line> Edges { get; } = new List<Line>();

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
            foreach (var p in Nodes)
                args.Display.DrawPoint(p, PointStyle.RoundSimple, 4, args.WireColour_Selected);
        }
    }

    [GrasshopperCollection("MY_BETTA_CATEGORY", "Graph")]
    public interface IGraphCollection : IBettaCollection
    {
        [GrasshopperMethod("Load Graph", "Build a regular polygon graph")]
        Graph Load([GrasshopperParameter("Sides", DefaultValue = 4)] int sides);

        [GrasshopperMethod("Deconstruct Graph", "Pull nodes and edges back out")]
        (List<Point3d> Nodes, List<Line> Edges) Deconstruct(
            [GrasshopperParameter("Graph")] Graph graph);
    }

    public class GraphCollection : IGraphCollection
    {
        public Graph Load(int sides)
        {
            var g = new Graph();
            if (sides < 3) sides = 3;
            for (int i = 0; i < sides; i++)
            {
                var t = (System.Math.PI * 2 * i) / sides;
                g.Nodes.Add(new Point3d(System.Math.Cos(t), System.Math.Sin(t), 0));
            }
            for (int i = 0; i < g.Nodes.Count; i++)
                g.Edges.Add(new Line(g.Nodes[i], g.Nodes[(i + 1) % g.Nodes.Count]));
            return g;
        }

        public (List<Point3d> Nodes, List<Line> Edges) Deconstruct(Graph graph)
        {
            return (graph?.Nodes ?? new List<Point3d>(), graph?.Edges ?? new List<Line>());
        }
    }
}
