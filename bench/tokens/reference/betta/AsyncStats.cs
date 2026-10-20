using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Betta.Attributes;
using Betta.Interfaces;

namespace BenchPlugin
{
    [GrasshopperCollection("Bench", "Data")]
    public class AsyncStatsPack : IBettaCollection
    {
        // Betta runs the Task off the UI thread and cancels the token when inputs change.
        [GrasshopperMethod("Async Stats", "Mean and standard deviation, computed asynchronously")]
        public Task<(double Mean, double StdDev)> AsyncStats(
            [GrasshopperParameter("Values")] List<double> values,
            CancellationToken ct)
        {
            return Task.Run(() =>
            {
                if (values == null || values.Count == 0) return (0.0, 0.0);
                var mean = values.Average();
                double sumSq = 0;
                foreach (var v in values)
                {
                    ct.ThrowIfCancellationRequested();
                    sumSq += (v - mean) * (v - mean);
                }
                return (mean, Math.Sqrt(sumSq / values.Count));
            }, ct);
        }
    }
}
