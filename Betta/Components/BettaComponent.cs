// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Betta.Services;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Betta.Components
{
    public class BettaComponent : GH_Component
    {
        /// <summary>
        /// Thread-static slot set by BettaComponentProxy.CreateInstance just
        /// before 'new BettaComponent()'. Needed because GH_Component's base
        /// ctor invokes RegisterInputParams/RegisterOutputParams BEFORE our
        /// derived ctor body runs — meaning an instance field cannot carry the
        /// descriptor into those overrides.
        /// </summary>
        [ThreadStatic]
        internal static ComponentDescriptor Pending;

        private ComponentDescriptor _descriptor;
        private object _service;
        private ParamInjector _paramInjector;
        private ILogger<BettaComponent> _logger = NullLogger<BettaComponent>.Instance;

        // Async support: when the service method returns Task<T>/ValueTask<T>,
        // we kick off the task, cache the result by input hash, and call
        // ExpireSolution(true) so the re-solve reads the cached value. This
        // keeps SolveInstance from blocking the GH solver thread on I/O.
        private bool _isAsync;
        private ConcurrentDictionary<string, object> _asyncCache;

        public BettaComponent()
            : base(
                Pending?.Name ?? "Betta",
                Pending?.NickName ?? "Bac",
                Pending?.Description ?? "Generated component",
                Pending?.Category ?? "Betta",
                Pending?.SubCategory ?? "Generated")
        {
            _descriptor = Pending;
            if (_descriptor != null && Startup.ServiceProvider != null)
            {
                _service = Startup.ServiceProvider.GetService(_descriptor.ServiceType);
                _logger = Startup.ServiceProvider.GetService<ILogger<BettaComponent>>() ?? _logger;
            }

            if (_descriptor != null && IsTaskType(_descriptor.Method.ReturnType))
            {
                _isAsync = true;
                _asyncCache = new ConcurrentDictionary<string, object>();
            }
        }

        private static bool IsTaskType(Type t)
        {
            if (t == null || !t.IsGenericType) return false;
            var gt = t.GetGenericTypeDefinition();
            return gt == typeof(Task<>) || gt == typeof(ValueTask<>);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            var d = _descriptor ?? Pending;
            if (d?.Method == null) return;

            _paramInjector = new ParamInjector(d.Method, Params);
            _paramInjector.GenerateInputs();
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            var d = _descriptor ?? Pending;
            if (d?.Method == null) return;

            _paramInjector ??= new ParamInjector(d.Method, Params);
            _paramInjector.GenerateOutputs();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (_paramInjector?.Method == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No method configured for this component");
                return;
            }

            if (_service == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Service '{_descriptor.ServiceType.Name}' not registered in DI container");
                return;
            }

            try
            {
                var batches = _paramInjector.GetItemData();
                if (batches == null || !batches.Any())
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No input data to process");
                    return;
                }

                batches = _paramInjector.GetListData(batches);

                var results = new List<object>();
                foreach (var batch in batches)
                {
                    try
                    {
                        var args = _paramInjector.BuildArguments(batch);

                        _logger.LogDebug("{Name}: invoking {Method}({Args})",
                            _descriptor.Name, _paramInjector.Method.Name,
                            string.Join(", ", args.Select(a => a?.ToString() ?? "null")));

                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            "args: " + string.Join(", ",
                                _paramInjector.Inputs.Zip(args, (p, a) => $"{p.Name}={Format(a)}")));

                        object result;
                        if (_isAsync)
                        {
                            if (!TryGetAsyncResult(args, out result))
                            {
                                // Task is in flight; when it completes, ExpireSolution
                                // fires and the re-solve takes the cache-hit branch.
                                Message = "computing";
                                return;
                            }
                            Message = null;
                        }
                        else
                        {
                            result = _paramInjector.Method.Invoke(_service, args);
                        }

                        _logger.LogDebug("{Name}: result = {Result}", _descriptor.Name, Format(result));

                        results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Method execution failed: {ex.Message}");
                        _logger.LogError(ex, "{Name}: invocation failed", _descriptor.Name);
                        return;
                    }
                }

                if (results.Count == 1)
                {
                    _paramInjector.SetOutputDataAdvanced(DA, results[0]);
                }
                else if (results.Count > 1)
                {
                    var firstResult = results[0];
                    if (firstResult != null && (_paramInjector.IsTupleType(firstResult.GetType()) ||
                        _paramInjector.IsComplexObject(firstResult.GetType())))
                    {
                        _paramInjector.SetOutputDataAdvanced(DA, results[0]);
                    }
                    else
                    {
                        DA.SetDataList(0, results);
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Component execution failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Cache-or-schedule for async service methods. On cache hit returns
        /// true + the stored result. On miss, launches the task off the solver
        /// thread; its ContinueWith stores the result and marshals an
        /// ExpireSolution(true) onto the UI thread so GH re-solves and this
        /// method returns the cached value next time. Returns false while the
        /// task is in flight (SolveInstance leaves the output empty).
        /// </summary>
        private bool TryGetAsyncResult(object[] args, out object result)
        {
            var key = AsyncKey(args);
            if (_asyncCache.TryGetValue(key, out result))
                return true;

            object invocation;
            try
            {
                invocation = _paramInjector.Method.Invoke(_service, args);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Async invoke failed: {ex.Message}");
                _logger.LogError(ex, "{Name}: async invoke threw", _descriptor.Name);
                return false;
            }

            var task = AsTask(invocation);
            if (task == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Async method did not return a Task");
                return false;
            }

            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            t.Exception?.InnerException?.Message ?? "task faulted");
                        _logger.LogError(t.Exception, "{Name}: task faulted", _descriptor.Name);
                        ExpireSolution(true);
                    }));
                    return;
                }

                // Task<T>.Result via reflection (can't cast the non-generic Task).
                var resultProp = t.GetType().GetProperty("Result");
                _asyncCache[key] = resultProp?.GetValue(t);

                Rhino.RhinoApp.InvokeOnUiThread(new Action(() => ExpireSolution(true)));
            }, TaskContinuationOptions.ExecuteSynchronously);

            result = null;
            return false;
        }

        /// <summary>
        /// Normalize Task&lt;T&gt; and ValueTask&lt;T&gt; into a plain Task the
        /// continuation can subscribe to. Returns null if the object isn't a
        /// recognized task type.
        /// </summary>
        private static Task AsTask(object invocation)
        {
            if (invocation == null) return null;
            if (invocation is Task t) return t;

            // ValueTask<T>.AsTask() via reflection (type is generic).
            var asTaskMethod = invocation.GetType().GetMethod("AsTask", Type.EmptyTypes);
            if (asTaskMethod != null)
                return asTaskMethod.Invoke(invocation, null) as Task;

            return null;
        }

        private static string AsyncKey(object[] args) =>
            string.Join("|", args.Select(a => a?.ToString() ?? "null"));

        private static string Format(object value)
        {
            if (value == null) return "null";
            if (value is System.Collections.IEnumerable en && value is not string)
            {
                var items = new List<string>();
                foreach (var item in en) items.Add(item?.ToString() ?? "null");
                return $"[{string.Join(", ", items)}]";
            }
            return value.ToString();
        }

        protected override System.Drawing.Bitmap Icon =>
            IconProvider.GetOrCreate(_descriptor ?? Pending);

        public override Guid ComponentGuid => (_descriptor ?? Pending)?.Guid ?? Guid.Empty;

        /// <summary>
        /// Hide the bare BettaComponent type from GH's automatic
        /// assembly-scan registration. Our IGH_ObjectProxy entries (one per
        /// service method) carry their own Exposure = primary, so descriptor-
        /// backed components still appear in the toolbar normally — only the
        /// generic placeholder GH would otherwise auto-publish is hidden.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.hidden;
    }
}
