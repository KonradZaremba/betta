using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;

namespace BenchPlugin.Facade
{
    public class NestOnSheetsComponent : FacadeComponent
    {
        class Key
        {
            public List<Panel> Panels;
            public double W, H, Spacing;

            public bool Matches(Key o) => o != null && W == o.W && H == o.H && Spacing == o.Spacing
                && Panels.Count == o.Panels.Count && Panels.Zip(o.Panels, ReferenceEquals).All(x => x);
        }

        class Result
        {
            public List<Curve> Outlines = new List<Curve>();
            public List<int> Sheets = new List<int>();
            public int SheetCount, Unplaced;
            public double Utilization;
        }

        readonly object _lock = new object();
        CancellationTokenSource _cts;
        Key _runningKey;
        Key _doneKey;
        Result _result;
        int _progress;

        public NestOnSheetsComponent()
            : base("Nest On Sheets", "Nest", "Nests panels as rectangles on sheets with a shelf algorithm", "Fabrication") { }

        public override Guid ComponentGuid => new Guid("7dcf9076-82a3-4eeb-9df7-6dd9b128e457");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            AddPanelInput(pManager, "Panels", "P", "Panels", GH_ParamAccess.list);
            pManager.AddNumberParameter("Sheet Width", "W", "Sheet width", GH_ParamAccess.item, 3000.0);
            pManager.AddNumberParameter("Sheet Height", "H", "Sheet height", GH_ParamAccess.item, 1500.0);
            pManager.AddNumberParameter("Spacing", "S", "Gap between parts and from the sheet edge", GH_ParamAccess.item, 10.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Outlines", "O", "Placed rectangles", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Sheet", "Sh", "Sheet index per placed panel", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Sheet Count", "N", "Number of sheets used", GH_ParamAccess.item);
            pManager.AddNumberParameter("Utilization", "U", "Placed area over sheet area", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var panels = GetPanels(DA, 0);
            double w = 3000, h = 1500, sp = 10;
            if (panels == null || !DA.GetData(1, ref w) || !DA.GetData(2, ref h) || !DA.GetData(3, ref sp))
            {
                Cancel();
                return;
            }
            var key = new Key { Panels = panels, W = w, H = h, Spacing = sp };

            Result ready = null;
            lock (_lock)
            {
                if (_result != null && key.Matches(_doneKey)) ready = _result;
                else if (_runningKey != null && key.Matches(_runningKey))
                {
                    Message = _progress + "%";
                    return;
                }
            }

            if (ready == null)
            {
                Start(key);
                return;
            }

            Message = "";
            if (ready.Unplaced > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{ready.Unplaced} panels did not fit on any sheet.");
            DA.SetDataList(0, ready.Outlines.Select(c => c.DuplicateCurve()));
            DA.SetDataList(1, ready.Sheets);
            DA.SetData(2, ready.SheetCount);
            DA.SetData(3, ready.Utilization);
        }

        void Cancel()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts = null;
                _runningKey = null;
                _doneKey = null;
                _result = null;
            }
            Message = "";
        }

        void Start(Key key)
        {
            CancellationTokenSource cts;
            lock (_lock)
            {
                _cts?.Cancel();
                _cts = cts = new CancellationTokenSource();
                _runningKey = key;
                _doneKey = null;
                _result = null;
                _progress = 0;
            }
            Message = "0%";

            Task.Run(() =>
            {
                Result result;
                try { result = Nest(key, cts.Token); }
                catch (OperationCanceledException) { return; }
                catch (Exception) { result = new Result { Unplaced = key.Panels.Count }; }

                lock (_lock)
                {
                    if (cts.IsCancellationRequested) return;
                    _runningKey = null;
                    _doneKey = key;
                    _result = result;
                }
                RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    if (!cts.IsCancellationRequested) ExpireSolution(true);
                }));
            });
        }

        void ReportProgress(int percent, CancellationToken token)
        {
            if (percent == _progress) return;
            _progress = percent;
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                if (token.IsCancellationRequested) return;
                Message = percent + "%";
                OnDisplayExpired(false);
            }));
        }

        Result Nest(Key key, CancellationToken token)
        {
            double sheetW = key.W, sheetH = key.H, sp = key.Spacing;
            var parts = key.Panels
                .Select(p => new { p.Width, p.Height })
                .OrderByDescending(p => p.Height)
                .ToList();

            var result = new Result();
            int sheet = -1;
            double x = 0, shelfY = 0, shelfH = 0;
            bool open = false; // a shelf exists on the current sheet
            double placedArea = 0;

            for (int n = 0; n < parts.Count; n++)
            {
                token.ThrowIfCancellationRequested();
                ReportProgress(100 * n / Math.Max(1, parts.Count), token);

                double pw = parts[n].Width, ph = parts[n].Height;
                var orientations = new[] { (pw, ph), (ph, pw) };
                double px = 0, py = 0, w = 0, h = 0;
                bool placed = false;

                // 1. current shelf
                if (open)
                    foreach (var (ow, oh) in orientations)
                        if (x + ow <= sheetW - sp && shelfY + oh <= sheetH - sp)
                        {
                            px = x; py = shelfY; w = ow; h = oh; placed = true;
                            x += ow + sp; shelfH = Math.Max(shelfH, oh);
                            break;
                        }

                // 2. new shelf on the current sheet
                if (!placed && open)
                    foreach (var (ow, oh) in orientations)
                    {
                        double ny = shelfY + shelfH + sp;
                        if (sp + ow <= sheetW - sp && ny + oh <= sheetH - sp)
                        {
                            shelfY = ny; shelfH = oh;
                            px = sp; py = shelfY; w = ow; h = oh; placed = true;
                            x = sp + ow + sp;
                            break;
                        }
                    }

                // 3. new sheet
                if (!placed)
                    foreach (var (ow, oh) in orientations)
                        if (sp + ow <= sheetW - sp && sp + oh <= sheetH - sp)
                        {
                            sheet++; open = true;
                            shelfY = sp; shelfH = oh;
                            px = sp; py = sp; w = ow; h = oh; placed = true;
                            x = sp + ow + sp;
                            break;
                        }

                if (!placed)
                {
                    result.Unplaced++;
                    continue;
                }

                double ox = sheet * (sheetW + 100) + px;
                result.Outlines.Add(new PolylineCurve(new[]
                {
                    new Point3d(ox, py, 0), new Point3d(ox + w, py, 0),
                    new Point3d(ox + w, py + h, 0), new Point3d(ox, py + h, 0), new Point3d(ox, py, 0),
                }));
                result.Sheets.Add(sheet);
                placedArea += w * h;
            }

            result.SheetCount = sheet + 1;
            result.Utilization = result.SheetCount == 0 ? 0 : placedArea / (result.SheetCount * sheetW * sheetH);
            return result;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            Cancel();
            base.RemovedFromDocument(document);
        }
    }
}
