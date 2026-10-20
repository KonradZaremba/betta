using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BenchPlugin
{
    public class DivideCurveStatsComponent : GH_Component
    {
        public DivideCurveStatsComponent()
            : base("Divide Curve Stats", "DivStats", "Divides a curve into N equal-length segments", "Bench", "Geometry")
        {
        }

        public override Guid ComponentGuid => new Guid("c2f5b8d3-63ea-4a14-b7c9-4e8d0f1a2b33");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Curve to divide", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Count", "N", "Number of segments", GH_ParamAccess.item, 10);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Division points", GH_ParamAccess.list);
            pManager.AddNumberParameter("Length", "L", "Total curve length", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve curve = null;
            int count = 10;
            if (!DA.GetData(0, ref curve)) return;
            if (!DA.GetData(1, ref count)) return;
            if (curve == null || count < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Need a curve and Count >= 1");
                return;
            }

            var points = new List<Point3d>();
            var parameters = curve.DivideByCount(count, true);
            if (parameters != null)
                foreach (var t in parameters) points.Add(curve.PointAt(t));

            DA.SetDataList(0, points);
            DA.SetData(1, curve.GetLength());
        }
    }
}
