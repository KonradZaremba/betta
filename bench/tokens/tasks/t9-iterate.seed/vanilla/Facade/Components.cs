using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BenchPlugin.Facade
{
    public class PanelizeSurfaceComponent : FacadeComponent
    {
        public PanelizeSurfaceComponent()
            : base("Panelize Surface", "Panelize", "Cuts a surface into a grid of four-sided panels", "Panels") { }

        public override Guid ComponentGuid => new Guid("1b6f3a10-2c4d-4e85-a7f1-0d93b5c2e801");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddSurfaceParameter("Surface", "S", "Surface to divide", GH_ParamAccess.item);
            pManager.AddIntegerParameter("U Count", "U", "Panels in U direction", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("V Count", "V", "Panels in V direction", GH_ParamAccess.item, 6);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            AddPanelOutput(pManager, "Panels", "P", "Panels", GH_ParamAccess.list);
            pManager.AddCurveParameter("Outlines", "O", "Closed panel outlines", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Surface surface = null;
            int u = 10, v = 6;
            if (!DA.GetData(0, ref surface) || surface == null) return;
            if (!DA.GetData(1, ref u)) return;
            if (!DA.GetData(2, ref v)) return;
            if (u < 1 || v < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "U Count and V Count must be at least 1.");
                return;
            }

            Interval du = surface.Domain(0), dv = surface.Domain(1);
            var grid = new Point3d[u + 1, v + 1];
            for (int i = 0; i <= u; i++)
                for (int j = 0; j <= v; j++)
                    grid[i, j] = surface.PointAt(du.ParameterAt((double)i / u), dv.ParameterAt((double)j / v));

            var panels = new List<GH_Panel>();
            var outlines = new List<Curve>();
            int index = 0;
            for (int i = 0; i < u; i++)
                for (int j = 0; j < v; j++)
                {
                    var panel = new Panel(new[] { grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1] }, index++);
                    panels.Add(new GH_Panel(panel));
                    outlines.Add(panel.Outline.DuplicateCurve());
                }

            DA.SetDataList(0, panels);
            DA.SetDataList(1, outlines);
        }
    }

    public class PanelInfoComponent : FacadeComponent
    {
        public PanelInfoComponent()
            : base("Panel Info", "Info", "Reads the properties of a panel", "Panels") { }

        public override Guid ComponentGuid => new Guid("2e7a4b21-3d5e-4f96-b8a2-1ea4c6d3f902");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            AddPanelInput(pManager, "Panel", "P", "Panel", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Corners", "C", "Four corners in order", GH_ParamAccess.list);
            pManager.AddCurveParameter("Outline", "O", "Closed outline", GH_ParamAccess.item);
            pManager.AddNumberParameter("Area", "A", "Panel area", GH_ParamAccess.item);
            pManager.AddPointParameter("Center", "Ce", "Average of the corners", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Index", "I", "Index in the grid", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Panel goo = null;
            if (!DA.GetData(0, ref goo) || goo == null || goo.Value == null) return;
            Panel p = goo.Value;
            DA.SetDataList(0, p.Corners);
            DA.SetData(1, p.Outline.DuplicateCurve());
            DA.SetData(2, p.Area);
            DA.SetData(3, p.Center);
            DA.SetData(4, p.Index);
        }
    }

    public class PlanarityComponent : FacadeComponent
    {
        public PlanarityComponent()
            : base("Planarity", "Planarity", "Measures how flat each panel is", "Analysis") { }

        public override Guid ComponentGuid => new Guid("3f8b5c32-4e6f-4aa7-89b3-2fb5d7e4a013");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            AddPanelInput(pManager, "Panels", "P", "Panels", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tolerance", "T", "Maximum deviation for a flat panel", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Deviation", "D", "Planarity deviation per panel", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Flat", "F", "True when deviation is within tolerance", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Flat Count", "N", "Number of flat panels", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var panels = GetPanels(DA, 0);
            double tol = 0.5;
            if (panels == null) return;
            if (!DA.GetData(1, ref tol)) return;

            var dev = panels.Select(p => p.Deviation).ToList();
            var flat = dev.Select(d => d <= tol).ToList();
            int count = flat.Count(f => f);
            int notFlat = panels.Count - count;
            if (notFlat * 2 > panels.Count)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{notFlat} of {panels.Count} panels are not flat.");

            DA.SetDataList(0, dev);
            DA.SetDataList(1, flat);
            DA.SetData(2, count);
        }
    }

    public class ClassifyPanelsComponent : FacadeComponent
    {
        public ClassifyPanelsComponent()
            : base("Classify Panels", "Classify", "Sorts panels into flat, cold-bent and custom", "Analysis") { }

        public override Guid ComponentGuid => new Guid("4a9c6d43-5f70-4bb8-9ac4-3ac6e8f5b124");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            AddPanelInput(pManager, "Panels", "P", "Panels", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tolerance", "T", "Maximum deviation for a flat panel", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Type", "Ty", "flat, cold-bent or custom per panel", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Flat Count", "F", "Number of flat panels", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Cold Bent Count", "C", "Number of cold-bent panels", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Custom Count", "Cu", "Number of custom panels", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var panels = GetPanels(DA, 0);
            double tol = 0.5;
            if (panels == null) return;
            if (!DA.GetData(1, ref tol)) return;

            var types = panels.Select(p => p.Deviation <= tol ? "flat" : p.Deviation <= 10 * tol ? "cold-bent" : "custom").ToList();
            DA.SetDataList(0, types);
            DA.SetData(1, types.Count(t => t == "flat"));
            DA.SetData(2, types.Count(t => t == "cold-bent"));
            DA.SetData(3, types.Count(t => t == "custom"));
        }
    }

    public class PanelStatsComponent : FacadeComponent
    {
        public PanelStatsComponent()
            : base("Panel Stats", "Stats", "Area statistics of a set of panels", "Analysis") { }

        public override Guid ComponentGuid => new Guid("5bad7e54-6081-4cc9-abd5-4bd7f906c235");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            AddPanelInput(pManager, "Panels", "P", "Panels", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Total Area", "T", "Sum of areas", GH_ParamAccess.item);
            pManager.AddNumberParameter("Min Area", "Mn", "Smallest area", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Area", "Mx", "Largest area", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mean Area", "Av", "Mean area", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var panels = GetPanels(DA, 0);
            if (panels == null || panels.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No panels.");
                return;
            }
            var areas = panels.Select(p => p.Area).ToList();
            DA.SetData(0, areas.Sum());
            DA.SetData(1, areas.Min());
            DA.SetData(2, areas.Max());
            DA.SetData(3, areas.Average());
        }
    }

    public class GroupIdenticalPanelsComponent : FacadeComponent
    {
        public GroupIdenticalPanelsComponent()
            : base("Group Identical Panels", "Group", "Groups panels with matching dimensions", "Fabrication") { }

        public override Guid ComponentGuid => new Guid("6cbe8f65-7192-4dda-bce6-5ce8a017d346");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            AddPanelInput(pManager, "Panels", "P", "Panels", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tolerance", "T", "Maximum length difference", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Group", "G", "Group number per panel", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Group Count", "N", "Number of groups", GH_ParamAccess.item);
            AddPanelOutput(pManager, "Representatives", "R", "First panel of each group", GH_ParamAccess.list);
        }

        static double[] Signature(Panel p)
        {
            var e = p.EdgeLengths(); Array.Sort(e);
            var d = p.DiagonalLengths(); Array.Sort(d);
            return e.Concat(d).ToArray();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var panels = GetPanels(DA, 0);
            double tol = 1.0;
            if (panels == null) return;
            if (!DA.GetData(1, ref tol)) return;

            var sigs = new List<double[]>();
            var reps = new List<GH_Panel>();
            var groups = new List<int>();
            foreach (var p in panels)
            {
                var s = Signature(p);
                int g = sigs.FindIndex(r => r.Zip(s, (a, b) => Math.Abs(a - b) <= tol).All(x => x));
                if (g < 0)
                {
                    g = sigs.Count;
                    sigs.Add(s);
                    reps.Add(new GH_Panel(p));
                }
                groups.Add(g);
            }

            DA.SetDataList(0, groups);
            DA.SetData(1, sigs.Count);
            DA.SetDataList(2, reps);
        }
    }

    public class CutListComponent : FacadeComponent
    {
        public CutListComponent()
            : base("Cut List", "CutList", "Builds a CSV cut list and optionally writes it to disk", "Fabrication") { }

        public override Guid ComponentGuid => new Guid("8ed0a187-93b4-4ffc-8e08-7e0ac239f568");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            AddPanelInput(pManager, "Panels", "P", "Panels", GH_ParamAccess.list);
            pManager.AddTextParameter("Path", "Pa", "Output file path", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Write", "W", "Write the file", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("CSV", "CSV", "Cut list", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Written", "W", "True when the file was written", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var panels = GetPanels(DA, 0);
            string path = "";
            bool write = false;
            if (panels == null) return;
            DA.GetData(1, ref path);
            DA.GetData(2, ref write);

            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("index,width,height,area,deviation\n");
            foreach (var p in panels)
                sb.Append(p.Index.ToString(ci)).Append(',')
                  .Append(p.Width.ToString("0.###", ci)).Append(',')
                  .Append(p.Height.ToString("0.###", ci)).Append(',')
                  .Append(p.Area.ToString("0.###", ci)).Append(',')
                  .Append(p.Deviation.ToString("0.###", ci)).Append('\n');
            string csv = sb.ToString();

            bool written = false;
            if (write)
            {
                if (string.IsNullOrWhiteSpace(path))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Path is empty.");
                else
                {
                    try { File.WriteAllText(path, csv); written = true; }
                    catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not write file: " + ex.Message); }
                }
            }

            DA.SetData(0, csv);
            DA.SetData(1, written);
        }
    }
}
