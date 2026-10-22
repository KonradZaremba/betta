using System;
using Betta.Attributes;
using Rhino.Geometry;

namespace BenchPlugin.Facade
{
    /// <summary>One four-sided facade panel cut from a surface. Passed between components as a single value.</summary>
    [GrasshopperOpaque]
    public class Panel
    {
        public Panel(Point3d c0, Point3d c1, Point3d c2, Point3d c3, int index)
        {
            Corners = new[] { c0, c1, c2, c3 };
            Index = index;
            Outline = new PolylineCurve(new[] { c0, c1, c2, c3, c0 });

            var d1 = c2 - c0;
            var d2 = c3 - c1;
            Area = 0.5 * Vector3d.CrossProduct(d1, d2).Length;

            var normal = Vector3d.CrossProduct(c1 - c0, c2 - c0);
            Deviation = normal.Unitize() ? Math.Abs((c3 - c0) * normal) : 0.0;
        }

        public Point3d[] Corners { get; }
        public Curve Outline { get; }
        public double Area { get; }
        public double Deviation { get; }
        public int Index { get; }

        public Point3d Center => new Point3d(
            (Corners[0].X + Corners[1].X + Corners[2].X + Corners[3].X) / 4,
            (Corners[0].Y + Corners[1].Y + Corners[2].Y + Corners[3].Y) / 4,
            (Corners[0].Z + Corners[1].Z + Corners[2].Z + Corners[3].Z) / 4);

        /// <summary>Edge length i, running from corner i to corner i+1.</summary>
        public double Edge(int i) => Corners[i].DistanceTo(Corners[(i + 1) % 4]);

        /// <summary>Mean of the first and third edge lengths.</summary>
        public double Width => (Edge(0) + Edge(2)) / 2;

        /// <summary>Mean of the second and fourth edge lengths.</summary>
        public double Height => (Edge(1) + Edge(3)) / 2;

        public override string ToString() => $"Panel {Index}";
    }
}
