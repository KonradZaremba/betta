using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;

namespace BenchPlugin
{
    // GH_TaskCapableComponent runs the tasks off the UI thread and cancels them when the solution expires.
    public class AsyncStatsComponent : GH_TaskCapableComponent<AsyncStatsComponent.SolveResult>
    {
        public class SolveResult
        {
            public double Mean;
            public double StdDev;
        }

        public AsyncStatsComponent()
            : base("Async Stats", "AStats", "Mean and standard deviation, computed asynchronously", "Bench", "Data")
        {
        }

        public override Guid ComponentGuid => new Guid("d3a6c9e4-74fb-4b25-88da-5f9e1a2b3c44");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Values", "V", "Numbers", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Mean", "M", "Mean", GH_ParamAccess.item);
            pManager.AddNumberParameter("StdDev", "SD", "Standard deviation", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (InPreSolve)
            {
                var values = new List<double>();
                if (!DA.GetDataList(0, values)) return;
                TaskList.Add(Task.Run(() => Compute(values, CancelToken), CancelToken));
                return;
            }

            if (!GetSolveResults(DA, out var result))
            {
                // Task capability unavailable (e.g. headless solve): compute inline.
                var values = new List<double>();
                if (!DA.GetDataList(0, values)) return;
                result = Compute(values, CancellationToken.None);
            }

            if (result == null) return;
            DA.SetData(0, result.Mean);
            DA.SetData(1, result.StdDev);
        }

        private static SolveResult Compute(List<double> values, CancellationToken ct)
        {
            if (values.Count == 0) return new SolveResult();
            var mean = values.Average();
            double sumSq = 0;
            foreach (var v in values)
            {
                ct.ThrowIfCancellationRequested();
                sumSq += (v - mean) * (v - mean);
            }
            return new SolveResult { Mean = mean, StdDev = Math.Sqrt(sumSq / values.Count) };
        }
    }
}
