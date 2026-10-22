using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace BenchPlugin
{
    public class FacadePanelsInfo : GH_AssemblyInfo
    {
        public override string Name => "Facade Panels (regular)";
        public override Bitmap Icon => null;
        public override string Description => "Facade panel fabrication, hand-written GH_Components";
        public override Guid Id => new Guid("5d0b7c1e-3a44-4c52-9a0e-1f6b2d8e7a10");
        public override string AuthorName => "Betta use case";
        public override string AuthorContact => "";
    }
}
