using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Betta.Interfaces;
using Rhino.Geometry;

namespace BenchPlugin.Facade
{
    public class FacadePack : IFacadePack
    {
        public (List<Panel> Panels, List<Curve> Outlines) PanelizeSurface(Surface surface, int uCount, int vCount)
        {
            if (surface == null) throw new ArgumentException("Surface is missing.");
            if (uCount < 1 || vCount < 1) throw new ArgumentException("U Count and V Count must be at least 1.");

            var du = surface.Domain(0);
            var dv = surface.Domain(1);
            var points = new Point3d[uCount + 1, vCount + 1];
            for (int i = 0; i <= uCount; i++)
                for (int j = 0; j <= vCount; j++)
                    points[i, j] = surface.PointAt(du.ParameterAt((double)i / uCount), dv.ParameterAt((double)j / vCount));

            var panels = new List<Panel>();
            var outlines = new List<Curve>();
            for (int i = 0; i < uCount; i++)
            {
                for (int j = 0; j < vCount; j++)
                {
                    var panel = new Panel(points[i, j], points[i + 1, j], points[i + 1, j + 1], points[i, j + 1], i * vCount + j);
                    panels.Add(panel);
                    outlines.Add(panel.Outline);
                }
            }
            return (panels, outlines);
        }

        public (List<Point3d> Corners, Curve Outline, double Area, Point3d Center, int Index) PanelInfo(Panel panel)
        {
            if (panel == null) throw new ArgumentException("Panel is missing.");
            return (panel.Corners.ToList(), panel.Outline, panel.Area, panel.Center, panel.Index);
        }

        public (List<double> Deviation, List<bool> Flat, int FlatCount) Planarity(
            List<Panel> panels, double tolerance, IBettaReport report)
        {
            panels = panels ?? new List<Panel>();
            var deviation = panels.Select(p => p.Deviation).ToList();
            var flat = deviation.Select(d => d <= tolerance).ToList();
            int flatCount = flat.Count(f => f);
            int notFlat = panels.Count - flatCount;
            if (notFlat * 2 > panels.Count)
                report.Warning($"{notFlat} of {panels.Count} panels are not flat.");
            return (deviation, flat, flatCount);
        }

        public (List<string> Type, int FlatCount, int ColdBentCount, int CustomCount) ClassifyPanels(
            List<Panel> panels, double tolerance)
        {
            var types = (panels ?? new List<Panel>()).Select(p =>
                p.Deviation <= tolerance ? "flat" :
                p.Deviation <= 10 * tolerance ? "cold-bent" : "custom").ToList();
            return (types, types.Count(t => t == "flat"), types.Count(t => t == "cold-bent"), types.Count(t => t == "custom"));
        }

        public (double TotalArea, double MinArea, double MaxArea, double MeanArea) PanelStats(List<Panel> panels)
        {
            if (panels == null || panels.Count == 0) throw new ArgumentException("There are no panels.");
            var areas = panels.Select(p => p.Area).ToList();
            return (areas.Sum(), areas.Min(), areas.Max(), areas.Average());
        }

        public (List<int> Group, int GroupCount, List<Panel> Representatives) GroupIdenticalPanels(
            List<Panel> panels, double tolerance)
        {
            var group = new List<int>();
            var representatives = new List<Panel>();
            var signatures = new List<double[]>();
            foreach (var panel in panels ?? new List<Panel>())
            {
                var signature = Signature(panel);
                int found = signatures.FindIndex(s => Same(s, signature, tolerance));
                if (found < 0)
                {
                    found = signatures.Count;
                    signatures.Add(signature);
                    representatives.Add(panel);
                }
                group.Add(found);
            }
            return (group, representatives.Count, representatives);
        }

        // Sorted edge lengths followed by sorted diagonal lengths.
        private static double[] Signature(Panel p)
        {
            var edges = new[] { p.Edge(0), p.Edge(1), p.Edge(2), p.Edge(3) };
            Array.Sort(edges);
            var diagonals = new[] { p.Corners[0].DistanceTo(p.Corners[2]), p.Corners[1].DistanceTo(p.Corners[3]) };
            Array.Sort(diagonals);
            return edges.Concat(diagonals).ToArray();
        }

        private static bool Same(double[] a, double[] b, double tolerance)
        {
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > tolerance) return false;
            return true;
        }

        public Task<(List<Curve> Outlines, List<int> Sheet, int SheetCount, double Utilization)> NestOnSheets(
            List<Panel> panels, double sheetWidth, double sheetHeight, double spacing,
            CancellationToken cancellationToken, IProgress<int> progress, IBettaReport report)
        {
            var parts = (panels ?? new List<Panel>()).Select(p => (w: p.Width, h: p.Height)).ToList();
            return Task.Run(() =>
            {
                var result = Nest(parts, sheetWidth, sheetHeight, spacing, cancellationToken, progress, out int unplaced);
                if (unplaced > 0)
                    report.Warning($"{unplaced} panels fit on no sheet.");
                return result;
            }, cancellationToken);
        }

        private static (List<Curve> Outlines, List<int> Sheet, int SheetCount, double Utilization) Nest(
            List<(double w, double h)> parts, double sheetW, double sheetH, double s,
            CancellationToken ct, IProgress<int> progress, out int unplaced)
        {
            var outlines = new List<Curve>();
            var sheets = new List<int>();
            unplaced = 0;

            int sheet = 0;
            bool sheetUsed = false;
            double x = s, shelfY = s, shelfH = 0;
            double placedArea = 0;

            void Place(double pw, double ph, double px, double py)
            {
                double ox = sheet * (sheetW + 100);
                outlines.Add(new PolylineCurve(new[]
                {
                    new Point3d(ox + px, py, 0), new Point3d(ox + px + pw, py, 0),
                    new Point3d(ox + px + pw, py + ph, 0), new Point3d(ox + px, py + ph, 0),
                    new Point3d(ox + px, py, 0),
                }));
                sheets.Add(sheet);
                placedArea += pw * ph;
                sheetUsed = true;
            }

            // Tries both orientations (as drawn first, then turned) for a part opening a new shelf at y.
            bool TryNewShelf(double w, double h, double y)
            {
                foreach (var (pw, ph) in new[] { (w, h), (h, w) })
                {
                    if (s + pw + s <= sheetW && y + ph + s <= sheetH)
                    {
                        Place(pw, ph, s, y);
                        shelfY = y; shelfH = ph; x = s + pw + s;
                        return true;
                    }
                }
                return false;
            }

            var order = parts.OrderByDescending(p => p.h).ToList();
            for (int n = 0; n < order.Count; n++)
            {
                ct.ThrowIfCancellationRequested();
                var (w, h) = order[n];
                bool placed = false;

                if (shelfH > 0)
                {
                    foreach (var (pw, ph) in new[] { (w, h), (h, w) })
                    {
                        if (x + pw + s <= sheetW && ph <= shelfH)
                        {
                            Place(pw, ph, x, shelfY);
                            x += pw + s;
                            placed = true;
                            break;
                        }
                    }
                }
                if (!placed)
                    placed = TryNewShelf(w, h, shelfH > 0 ? shelfY + shelfH + s : shelfY);
                if (!placed && sheetUsed)
                {
                    var saved = (x, shelfY, shelfH);
                    sheet++;
                    x = s; shelfY = s; shelfH = 0;
                    placed = TryNewShelf(w, h, shelfY);
                    if (!placed)
                    {
                        sheet--;
                        (x, shelfY, shelfH) = saved;
                    }
                }
                if (!placed) unplaced++;
                progress?.Report((n + 1) * 100 / order.Count);
            }

            int sheetCount = sheets.Count == 0 ? 0 : sheets.Max() + 1;
            double utilization = sheetCount == 0 ? 0 : placedArea / (sheetCount * sheetW * sheetH);
            return (outlines, sheets, sheetCount, utilization);
        }

        public (string CSV, bool Written) CutList(List<Panel> panels, string path, bool write)
        {
            if (write && string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is empty: set a file path to write the cut list.");

            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("Index,Width,Height,Area,Deviation\n");
            foreach (var p in panels ?? new List<Panel>())
                sb.Append(string.Join(",", p.Index.ToString(inv), p.Width.ToString(inv), p.Height.ToString(inv),
                    p.Area.ToString(inv), p.Deviation.ToString(inv))).Append('\n');
            var csv = sb.ToString();

            if (!write) return (csv, false);
            File.WriteAllText(path, csv);
            return (csv, true);
        }
    }
}
