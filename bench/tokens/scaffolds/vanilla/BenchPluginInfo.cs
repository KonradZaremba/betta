using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace BenchPlugin
{
    public class BenchPluginInfo : GH_AssemblyInfo
    {
        public override string Name => "BenchPlugin";
        public override Bitmap Icon => null;
        public override string Description => "Benchmark plugin";
        public override Guid Id => new Guid("5d0b7c1e-3a44-4c52-9a0e-1f6b2d8e7a10");
        public override string AuthorName => "Bench";
        public override string AuthorContact => "";
    }
}
