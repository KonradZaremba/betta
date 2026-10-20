using System.Collections.Generic;
using Betta.Attributes;
using Betta.Interfaces;
using Rhino.Geometry;

namespace BenchPlugin
{
    [GrasshopperCollection("Bench", "Geometry")]
    public class DivideCurveStatsPack : IBettaCollection
    {
        [GrasshopperMethod("Divide Curve Stats", "Divides a curve into N equal-length segments")]
        public (List<Point3d> Points, double Length) DivideCurveStats(
            [GrasshopperParameter("Curve")] Curve curve,
            [GrasshopperParameter("Count", DefaultValue = 10)] int count)
        {
            var points = new List<Point3d>();
            if (curve == null || count < 1) return (points, 0);
            var parameters = curve.DivideByCount(count, true);
            if (parameters != null)
                foreach (var t in parameters) points.Add(curve.PointAt(t));
            return (points, curve.GetLength());
        }
    }
}
