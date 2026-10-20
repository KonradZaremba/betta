using System;
using Grasshopper.Kernel;

namespace BenchPlugin
{
    public class AddComponent : GH_Component
    {
        public AddComponent()
            : base("Add", "Add", "Adds two numbers", "Bench", "Math")
        {
        }

        public override Guid ComponentGuid => new Guid("a3c1f2d4-7b58-4e19-8d36-0c2e5f9a4b71");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("A", "A", "First number", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("B", "B", "Second number", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Sum", "S", "A + B", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double a = 0, b = 0;
            if (!DA.GetData(0, ref a)) return;
            if (!DA.GetData(1, ref b)) return;
            DA.SetData(0, a + b);
        }
    }
}
