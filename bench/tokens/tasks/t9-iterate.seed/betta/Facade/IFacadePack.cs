using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using Betta.Attributes;
using Betta.Interfaces;
using Rhino.Geometry;

namespace BenchPlugin.Facade
{
    [GrasshopperCollection("Facade", "Panels")]
    public interface IFacadePack : IBettaCollection
    {
        [GrasshopperMethod("Panelize Surface", "Divide a surface into four-sided panels", SubCategory = "Panels")]
        (List<Panel> Panels, List<Curve> Outlines) PanelizeSurface(
            [GrasshopperParameter("Surface")] Surface surface,
            [GrasshopperParameter("U Count", DefaultValue = 10)] int uCount,
            [GrasshopperParameter("V Count", DefaultValue = 6)] int vCount);

        [GrasshopperMethod("Panel Info", "Corners, outline, area, center and index of a panel", SubCategory = "Panels")]
        (List<Point3d> Corners, Curve Outline, double Area, Point3d Center, int Index) PanelInfo(
            [GrasshopperParameter("Panel")] Panel panel);

        [GrasshopperMethod("Planarity", "Planarity deviation of each panel", SubCategory = "Analysis")]
        (List<double> Deviation, List<bool> Flat, int FlatCount) Planarity(
            [GrasshopperParameter("Panels")] List<Panel> panels,
            [GrasshopperParameter("Tolerance", DefaultValue = 0.5)] double tolerance,
            IBettaReport report);

        [GrasshopperMethod("Classify Panels", "Classify panels as flat, cold-bent or custom", SubCategory = "Analysis")]
        (List<string> Type, int FlatCount, int ColdBentCount, int CustomCount) ClassifyPanels(
            [GrasshopperParameter("Panels")] List<Panel> panels,
            [GrasshopperParameter("Tolerance", DefaultValue = 0.5)] double tolerance);

        [GrasshopperMethod("Panel Stats", "Area statistics of the panels", SubCategory = "Analysis")]
        (double TotalArea, double MinArea, double MaxArea, double MeanArea) PanelStats(
            [GrasshopperParameter("Panels")] List<Panel> panels);

        [GrasshopperMethod("Group Identical Panels", "Group panels with the same edge and diagonal lengths", SubCategory = "Fabrication")]
        (List<int> Group, int GroupCount, List<Panel> Representatives) GroupIdenticalPanels(
            [GrasshopperParameter("Panels")] List<Panel> panels,
            [GrasshopperParameter("Tolerance", DefaultValue = 1.0)] double tolerance);

        [GrasshopperMethod("Nest On Sheets", "Nest panels as rectangles on sheets with a shelf algorithm", SubCategory = "Fabrication")]
        Task<(List<Curve> Outlines, List<int> Sheet, int SheetCount, double Utilization)> NestOnSheets(
            [GrasshopperParameter("Panels")] List<Panel> panels,
            [GrasshopperParameter("Sheet Width", DefaultValue = 3000.0)] double sheetWidth,
            [GrasshopperParameter("Sheet Height", DefaultValue = 1500.0)] double sheetHeight,
            [GrasshopperParameter("Spacing", DefaultValue = 10.0, Description = "Gap between parts and from the sheet edge")] double spacing,
            CancellationToken cancellationToken,
            IProgress<int> progress,
            IBettaReport report);

        [GrasshopperMethod("Cut List", "CSV cut list of the panels, optionally written to a file", SubCategory = "Fabrication")]
        (string CSV, bool Written) CutList(
            [GrasshopperParameter("Panels")] List<Panel> panels,
            [GrasshopperParameter("Path", DefaultValue = "")] string path,
            [GrasshopperParameter("Write", DefaultValue = false)] bool write);
    }
}
