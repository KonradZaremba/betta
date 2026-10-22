// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Betta;
using Betta.Components;
using Betta.Interfaces;
using Betta.Services;
using Xunit;

namespace TestBetta
{
    // Not an IBettaCollection, so discovery scans of this assembly don't pick it up.
    public class AsyncNoResultSamples
    {
        public int Gate;

        public async Task Work(int n, CancellationToken ct, IBettaReport report)
        {
            Interlocked.Increment(ref Gate);
            report.Warning("working");
            await Task.Delay(20, ct);
        }

        public ValueTask Fire(int n) => new ValueTask(Task.Delay(20));

        public Task Boom(int n) => Task.FromException(new InvalidOperationException("boom"));

        public Task<int> WithResult(int n) => Task.FromResult(n * 2);

        public void Plain(int n) { }
    }

    /// <summary>
    /// A method returning non-generic Task / ValueTask runs on the async path and has no outputs,
    /// like void. Before: it was invoked synchronously and Task's own properties (Id, Status, ...)
    /// became outputs.
    /// </summary>
    public class TestAsyncNoResult
    {
        private static ComponentDescriptor Descriptor(string method)
        {
            var m = typeof(AsyncNoResultSamples).GetMethod(method);
            return new ComponentDescriptor
            {
                ServiceType = typeof(AsyncNoResultSamples),
                Method = m,
                Name = method, NickName = method, Description = method,
                Category = "Test", SubCategory = "Async",
                Guid = ComponentDescriptor.GenerateGuidFromMethod(typeof(AsyncNoResultSamples), m),
            };
        }

        private static BettaComponent Create(string method) =>
            (BettaComponent)new BettaComponentProxy(Descriptor(method)).CreateInstance();

        private static T Field<T>(object o, string name) =>
            (T)typeof(BettaComponent).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;

        [Theory]
        [InlineData(typeof(Task))]
        [InlineData(typeof(ValueTask))]
        public void Planner_gives_no_outputs_for_non_generic_task(Type returnType)
        {
            Assert.Empty(OutputPlanner.PlanOutputs(returnType, null));
        }

        [Fact]
        public void Planner_keeps_void_empty_and_a_plain_value_single()
        {
            Assert.Empty(OutputPlanner.PlanOutputs(typeof(void), null));
            Assert.Single(OutputPlanner.PlanOutputs(typeof(int), null));
        }

        [Trait("Requires", "Rhino")]
        [Theory]
        [InlineData("Work", true, 0)]
        [InlineData("Fire", true, 0)]
        [InlineData("Boom", true, 0)]
        [InlineData("WithResult", true, 1)]
        [InlineData("Plain", false, 0)]
        public void Component_is_async_and_has_the_right_outputs(string method, bool isAsync, int outputs)
        {
            var c = Create(method);
            Assert.Equal(isAsync, Field<bool>(c, "_isAsync"));
            Assert.Equal(outputs, c.Params.Output.Count);
        }

        [Trait("Requires", "Rhino")]
        [Fact]
        public void A_non_generic_task_completes_through_the_async_cache_with_a_null_result()
        {
            var c = Create("Work");
            var service = new AsyncNoResultSamples();
            typeof(BettaComponent).GetField("_service", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(c, service);
            // BeforeSolveInstance normally installs the CTS the method's token comes from.
            typeof(BettaComponent).GetField("_solveCts", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(c, new CancellationTokenSource());

            var report = new BettaReport();
            var args = new object[] { 3, CancellationToken.None, report };
            var tryGet = typeof(BettaComponent).GetMethod("TryGetAsyncResult", BindingFlags.NonPublic | BindingFlags.Instance)!;

            // First call launches the task and reports "still computing".
            var launch = new object[] { args, report, null };
            Assert.False((bool)tryGet.Invoke(c, launch)!);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            bool hit = false;
            object result = "unset";
            while (!hit && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
                var probe = new object[] { args, report, null };
                hit = (bool)tryGet.Invoke(c, probe)!;
                result = probe[2];
            }

            Assert.True(hit, "task never completed through the async cache");
            Assert.Null(result);
            Assert.Equal(1, service.Gate);
            // The method's IBettaReport message travelled with the result.
            Assert.Single(report.Messages);
        }

        // BettaComponent derives from GH_Component, so even reflecting on it needs Grasshopper.
        [Trait("Requires", "Rhino")]
        [Fact]
        public void TaskResultOf_is_null_for_non_generic_and_the_value_for_task_of_t()
        {
            var of = typeof(BettaComponent).GetMethod("TaskResultOf", BindingFlags.NonPublic | BindingFlags.Static)!;
            // An async Task method's real instance is a Task<VoidTaskResult>; it must still read as no result.
#pragma warning disable xUnit1031
            Task voidish = new AsyncNoResultSamples().Work(1, CancellationToken.None, new BettaReport());
            voidish.Wait();
#pragma warning restore xUnit1031
            Assert.Null(of.Invoke(null, new object[] { typeof(Task), voidish }));
            Assert.Equal(6, of.Invoke(null, new object[] { typeof(Task<int>), Task.FromResult(6) }));
        }
    }
}
