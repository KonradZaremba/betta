using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace BenchPlugin.Facade
{
    public class GH_Panel : GH_Goo<Panel>
    {
        public GH_Panel() { }
        public GH_Panel(Panel panel) : base(panel) { }

        public override bool IsValid => Value != null;
        public override string TypeName => "Panel";
        public override string TypeDescription => "A four-sided facade panel";
        public override IGH_Goo Duplicate() => new GH_Panel(Value);

        public override string ToString() => Value == null
            ? "Null panel"
            : $"Panel {Value.Index} (area {Value.Area:0.###}, deviation {Value.Deviation:0.###})";
    }

    public class Param_Panel : GH_Param<GH_Panel>
    {
        public Param_Panel()
            : base("Panel", "Panel", "A facade panel", "Facade", "Params", GH_ParamAccess.item)
        {
        }

        public override Guid ComponentGuid => new Guid("c0a1e5b2-6d37-4f80-9b14-72e3a8d5f601");
        public override GH_Exposure Exposure => GH_Exposure.hidden;
    }
}
