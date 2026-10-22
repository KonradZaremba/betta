using System;
using Rhino.Geometry;

namespace BenchPlugin.Facade
{
    /// <summary>One four-sided facade panel. Corners run around the panel.</summary>
    public class Panel
    {
        public Panel(Point3d[] corners, int index)
        {
            if (corners == null || corners.Length != 4)
                throw new ArgumentException("A panel needs exactly four corners.", nameof(corners));
            Corners = (Point3d[])corners.Clone();
            Index = index;
            Outline = new PolylineCurve(new[] { Corners[0], Corners[1], Corners[2], Corners[3], Corners[0] });
            Area = ComputeArea(Corners);
            Deviation = ComputeDeviation(Corners);
        }

        public Point3d[] Corners { get; }
        public int Index { get; }
        public PolylineCurve Outline { get; }
        public double Area { get; }
        public double Deviation { get; }

        public Point3d Center => new Point3d(
            (Corners[0].X + Corners[1].X + Corners[2].X + Corners[3].X) / 4.0,
            (Corners[0].Y + Corners[1].Y + Corners[2].Y + Corners[3].Y) / 4.0,
            (Corners[0].Z + Corners[1].Z + Corners[2].Z + Corners[3].Z) / 4.0);

        /// <summary>Edge lengths in order: 0-1, 1-2, 2-3, 3-0.</summary>
        public double[] EdgeLengths() => new[]
        {
            Corners[0].DistanceTo(Corners[1]),
            Corners[1].DistanceTo(Corners[2]),
            Corners[2].DistanceTo(Corners[3]),
            Corners[3].DistanceTo(Corners[0]),
        };

        public double[] DiagonalLengths() => new[]
        {
            Corners[0].DistanceTo(Corners[2]),
            Corners[1].DistanceTo(Corners[3]),
        };

        /// <summary>Mean of first and third edge lengths.</summary>
        public double Width { get { var e = EdgeLengths(); return (e[0] + e[2]) / 2.0; } }

        /// <summary>Mean of second and fourth edge lengths.</summary>
        public double Height { get { var e = EdgeLengths(); return (e[1] + e[3]) / 2.0; } }

        static double ComputeArea(Point3d[] c)
        {
            Vector3d d = c[2] - c[0];
            return 0.5 * Vector3d.CrossProduct(c[1] - c[0], d).Length
                 + 0.5 * Vector3d.CrossProduct(d, c[3] - c[0]).Length;
        }

        static double ComputeDeviation(Point3d[] c)
        {
            Vector3d n = Vector3d.CrossProduct(c[1] - c[0], c[2] - c[0]);
            if (n.Length < 1e-12)
                return c[3].DistanceTo(c[0]);
            n.Unitize();
            return Math.Abs((c[3] - c[0]) * n);
        }
    }
}
