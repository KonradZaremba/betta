using System.Collections.Generic;
using Betta.Attributes;
using Betta.Interfaces;
using Rhino.Geometry;

namespace MyBettaPlugin
{
    /// <summary>
    /// Domain object that flows through Betta wires as a SINGLE typed value
    /// — Betta auto-generates a Param/Goo pair for the type. Mark with
    /// [GrasshopperOpaque] OR implement IBettaValue; either signals the same
    /// "don't explode into properties" rule.
    /// </summary>
    [GrasshopperOpaque]
    public class Graph
    {
        public List<Point3d> Nodes { get; } = new List<Point3d>();
        public List<Line> Edges { get; } = new List<Line>();
    }

    [GrasshopperCollection("MY_BETTA_CATEGORY", "Graph")]
    public interface IGraphCollection : IBettaCollection
    {
        [GrasshopperMethod("Load Graph", "Build a small demo graph")]
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
