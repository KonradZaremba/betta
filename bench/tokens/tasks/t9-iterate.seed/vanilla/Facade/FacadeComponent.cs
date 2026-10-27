using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;

namespace BenchPlugin.Facade
{
    public abstract class FacadeComponent : GH_Component
    {
        protected FacadeComponent(string name, string nick, string description, string subCategory)
            : base(name, nick, description, "Facade", subCategory)
        {
        }

        protected void AddPanelInput(GH_InputParamManager pManager, string name, string nick, string description, GH_ParamAccess access)
        {
            pManager.AddParameter(new Param_Panel(), name, nick, description, access);
        }

        protected void AddPanelOutput(GH_OutputParamManager pManager, string name, string nick, string description, GH_ParamAccess access)
        {
            pManager.AddParameter(new Param_Panel(), name, nick, description, access);
        }

        protected static List<Panel> GetPanels(IGH_DataAccess DA, int index)
        {
            var goos = new List<GH_Panel>();
            if (!DA.GetDataList(index, goos)) return null;
            return goos.Where(g => g != null && g.Value != null).Select(g => g.Value).ToList();
        }
    }
}
