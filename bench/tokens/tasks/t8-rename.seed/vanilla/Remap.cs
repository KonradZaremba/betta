using System;
using Grasshopper.Kernel;

namespace BenchPlugin
{
    public class RemapComponent : GH_Component
    {
        public RemapComponent()
            : base("Remap", "Remap", "Remaps a number from a source range to a target range", "Bench", "Math")
        {
        }

        public override Guid ComponentGuid => new Guid("b1e4a7c2-52d9-4f03-a6b8-3d7c9e0f1a22");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Value", "V", "Value to remap", GH_ParamAccess.item);
            pManager.AddNumberParameter("Source Min", "SMin", "Source range start", GH_ParamAccess.item);
            pManager.AddNumberParameter("Source Max", "SMax", "Source range end", GH_ParamAccess.item);
            pManager.AddNumberParameter("Target Min", "TMin", "Target range start", GH_ParamAccess.item);
            pManager.AddNumberParameter("Target Max", "TMax", "Target range end", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Result", "R", "Remapped value", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double value = 0, sMin = 0, sMax = 0, tMin = 0, tMax = 0;
            if (!DA.GetData(0, ref value)) return;
            if (!DA.GetData(1, ref sMin)) return;
            if (!DA.GetData(2, ref sMax)) return;
            if (!DA.GetData(3, ref tMin)) return;
            if (!DA.GetData(4, ref tMax)) return;

            var t = sMax == sMin ? 0 : (value - sMin) / (sMax - sMin);
            DA.SetData(0, tMin + t * (tMax - tMin));
        }
    }
}
