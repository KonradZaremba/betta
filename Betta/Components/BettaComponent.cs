// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Betta.Services;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rhino.Geometry;

namespace Betta.Components
{
    // GH_Component already implements IGH_PreviewObject; we just override the
    // virtual surfaces (IsPreviewCapable, ClippingBox, DrawViewportWires,
    // DrawViewportMeshes) when our descriptor signals preview-capable output.
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
        // we kick off the task, cache the result by structural input hash, and
        // call ExpireSolution(true) so the re-solve reads the cached value.
        // SolveInstance never blocks the solver thread on I/O.
        //
        // The cache is a small bounded LRU; a separate in-flight registry
        // dedupes concurrent solves with the same key so a fast successive
        // expire doesn't double-spawn the same task.
        private bool _isAsync;
        private readonly object _asyncLock = new();
        private readonly LinkedList<KeyValuePair<string, object>> _asyncOrder = new();
        private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, object>>> _asyncCache = new();
        private readonly Dictionary<string, Task> _asyncInFlight = new();
        private const int AsyncCacheMax = 64;

        // Preview / bake support: when descriptor.HasPreview or HasBakeable
        // is true, SolveInstance stashes the emitted values here so the
        // IGH_PreviewObject and IGH_BakeAwareObject overrides below can
        // forward Draw/Bake calls via reflection. Cleared in
        // BeforeSolveInstance; zero overhead when neither flag is set.
        private readonly List<object> _opaqueValues = new();

        private const string PreviewInterfaceFullName = "Betta.Preview.IBettaPreview";
        private const string BakeableInterfaceFullName = "Betta.Preview.IBettaBakeable";

        // Per-solve cancellation. Refreshed in BeforeSolveInstance; cancelled
        // when a new solve starts so long-running methods (that took a
        // CancellationToken parameter) can quit stale work instead of running
        // to completion for inputs that have already changed.
        private CancellationTokenSource _solveCts;

        // Per-instance menu state — values for parameters tagged
        // [GrasshopperMenuState]. Persisted in the .gh via Read/Write so the
        // user's right-click choice survives save/reload.
        private readonly Dictionary<string, object> _menuState = new();

        // Trigger state — set to true when the user clicks the "Run" menu
        // item on a component with [GrasshopperTrigger] parameters, then
        // consumed by exactly one solve pass and reset to false. Between
        // triggers, the component still runs SolveInstance but early-returns
        // with a "Awaiting run" message so downstream wires don't get stale
        // partial data.
        private bool _runRequested;

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

            // Entitlement gate: null LicenseGate = OSS mode, always allowed.
            // A non-null gate is consulted per descriptor.RequiredEntitlement;
            // a denial surfaces as a Warning so the user sees why the
            // component isn't running and skips the invocation.
            if (!string.IsNullOrEmpty(_descriptor?.RequiredEntitlement) &&
                Startup.LicenseGate != null &&
                !Startup.LicenseGate.IsEntitlementGranted(_descriptor.RequiredEntitlement))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Requires the '{_descriptor.RequiredEntitlement}' entitlement.");
                Message = "unlicensed";
                return;
            }

            // Trigger gate: [GrasshopperTrigger] methods do not fire on every
            // input change. Users click the "Run" menu item to fire exactly
            // once. Between clicks the solve exits early with a message so
            // downstream wires know the state is intentional, not a bug.
            bool triggered = false;
            if (_descriptor?.HasTrigger == true)
            {
                if (!_runRequested)
                {
                    Message = "awaiting run";
                    return;
                }
                triggered = true;
                _runRequested = false;
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
                        // Cancellation: hand the current CTS token. Progress:
                        // wrap a sink that updates the component's Message so
                        // a method's IProgress<string>.Report or IProgress<int>.Report
                        // surfaces as the GH "computing 35 %" tag. Menu state:
                        // the persisted per-instance dictionary populates any
                        // [GrasshopperMenuState] params.
                        object[] args;
                        try
                        {
                            args = _paramInjector.BuildArguments(
                                batch,
                                _solveCts?.Token ?? CancellationToken.None,
                                value => { try { Message = value?.ToString(); } catch { } },
                                _menuState,
                                triggered);
                        }
                        catch (BettaValidationException bv)
                        {
                            // Missing secret / synthetic-param validation
                            // failure — Warning, skip this batch, keep going.
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, bv.Message);
                            _logger.LogWarning("{Name}: {Msg}", _descriptor.Name, bv.Message);
                            continue;
                        }

                        var validationMsg = ParamValidator.Validate(_paramInjector.Inputs, args);
                        if (validationMsg != null)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, validationMsg);
                            _logger.LogWarning("{Name}: validation skipped invocation — {Msg}",
                                _descriptor.Name, validationMsg);
                            continue;
                        }

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

                if (_descriptor?.HasPreview == true || _descriptor?.HasBakeable == true)
                    foreach (var r in results) CollectOpaque(r);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Component execution failed: {ex.Message}");
            }
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            // Clear previous solve's cached opaque values — a re-solve may
            // produce new geometry, and stale draws/bakes confuse users.
            _opaqueValues.Clear();

            // Cancel any in-flight long-running method from the prior solve.
            // Methods that took a CancellationToken parameter receive the new
            // token; the old one fires Cancel so they can quit early.
            var oldCts = _solveCts;
            _solveCts = new CancellationTokenSource();
            try { oldCts?.Cancel(); oldCts?.Dispose(); }
            catch { /* disposed-twice etc. — ignore */ }
        }

        public override bool IsPreviewCapable => _descriptor?.HasPreview == true || base.IsPreviewCapable;

        public override BoundingBox ClippingBox
        {
            get
            {
                if (_opaqueValues.Count == 0) return base.ClippingBox;
                var bb = base.ClippingBox;
                foreach (var v in _opaqueValues)
                    bb = BoundingBox.Union(bb, GetPreviewClippingBox(v));
                return bb;
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            foreach (var v in _opaqueValues) InvokeOnInterface(v, PreviewInterfaceFullName, "DrawWires", args);
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            foreach (var v in _opaqueValues) InvokeOnInterface(v, PreviewInterfaceFullName, "DrawMeshes", args);
        }

        // ---- IGH_BakeAwareObject ----------------------------------------
        // GH_Component declares IsBakeCapable/BakeGeometry virtual; for
        // values that implement IBettaBakeable the cached opaque values
        // own the actual bake.

        public override bool IsBakeCapable =>
            _descriptor?.HasBakeable == true ? _opaqueValues.Count > 0 : base.IsBakeCapable;

        public override void BakeGeometry(Rhino.RhinoDoc doc, List<Guid> obj_ids)
        {
            if (_descriptor?.HasBakeable != true) { base.BakeGeometry(doc, obj_ids); return; }
            BakeGeometry(doc, doc.CreateDefaultAttributes(), obj_ids);
        }

        public override void BakeGeometry(Rhino.RhinoDoc doc, Rhino.DocObjects.ObjectAttributes att, List<Guid> obj_ids)
        {
            if (_descriptor?.HasBakeable != true) { base.BakeGeometry(doc, att, obj_ids); return; }
            foreach (var v in _opaqueValues)
                InvokeOnInterface(v, BakeableInterfaceFullName, "Bake", doc, att, obj_ids);
        }

        // Recurse into lists / tuples / arrays so a method returning
        // List<DomainGraph> or (Graph a, Graph b) still surfaces every
        // opaque value that supports preview or bake.
        private void CollectOpaque(object value)
        {
            if (value == null) return;
            if (ImplementsAnyHook(value)) { _opaqueValues.Add(value); return; }
            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable) CollectOpaque(item);
                return;
            }
            // Tuples: read Item1..Item8.
            var t = value.GetType();
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition().FullName;
                if (def != null && (def.StartsWith("System.Tuple") || def.StartsWith("System.ValueTuple")))
                {
                    int arity = t.GetGenericArguments().Length;
                    bool isValueTuple = def.StartsWith("System.ValueTuple");
                    for (int i = 0; i < arity; i++)
                    {
                        var name = $"Item{i + 1}";
                        var member = isValueTuple
                            ? (object)t.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value)
                            : t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);
                        CollectOpaque(member);
                    }
                }
            }
        }

        private static bool ImplementsAnyHook(object value) =>
            GetInterfaceByName(value, PreviewInterfaceFullName) != null ||
            GetInterfaceByName(value, BakeableInterfaceFullName) != null;

        private static Type GetInterfaceByName(object value, string interfaceFullName)
        {
            if (value == null) return null;
            var t = value.GetType();
            if (t.FullName == interfaceFullName) return t;
            foreach (var iface in t.GetInterfaces())
                if (iface.FullName == interfaceFullName) return iface;
            return null;
        }

        private static void InvokeOnInterface(object value, string interfaceFullName, string methodName, params object[] args)
        {
            var iface = GetInterfaceByName(value, interfaceFullName);
            if (iface == null) return;
            try { iface.GetMethod(methodName)?.Invoke(value, args); }
            catch
            {
                // A single faulty Draw/Bake implementation must not abort the
                // rest of the pipeline.
            }
        }

        private static BoundingBox GetPreviewClippingBox(object value)
        {
            var iface = GetInterfaceByName(value, PreviewInterfaceFullName);
            if (iface == null) return BoundingBox.Empty;
            try
            {
                var prop = iface.GetProperty("ClippingBox");
                var bb = prop?.GetValue(value);
                return bb is BoundingBox b ? b : BoundingBox.Empty;
            }
            catch { return BoundingBox.Empty; }
        }

        /// <summary>
        /// Cache-or-schedule for async service methods. On cache hit returns
        /// true + the stored result (and touches LRU). On miss, launches the
        /// task off the solver thread; its ContinueWith stores the result,
        /// evicts oldest if the cache is full, and marshals
        /// ExpireSolution(true) onto the UI thread so GH re-solves and the
        /// next pass takes the cache-hit branch.
        ///
        /// Concurrent solves with the same key dedupe via _asyncInFlight: only
        /// one task runs per (component, key) pair at a time.
        /// </summary>
        private bool TryGetAsyncResult(object[] args, out object result)
        {
            var key = AsyncKey(args);
            lock (_asyncLock)
            {
                if (_asyncCache.TryGetValue(key, out var node))
                {
                    // LRU touch — move to MRU end.
                    _asyncOrder.Remove(node);
                    _asyncOrder.AddLast(node);
                    result = node.Value.Value;
                    return true;
                }
                if (_asyncInFlight.ContainsKey(key))
                {
                    // Same compute already running — let it finish; its
                    // continuation will ExpireSolution and we'll cache-hit.
                    result = null;
                    return false;
                }
            }

            object invocation;
            try
            {
                invocation = _paramInjector.Method.Invoke(_service, args);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Async invoke failed: {ex.Message}");
                _logger.LogError(ex, "{Name}: async invoke threw", _descriptor.Name);
                result = null;
                return false;
            }

            var task = AsTask(invocation);
            if (task == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Async method did not return a Task");
                result = null;
                return false;
            }

            lock (_asyncLock) { _asyncInFlight[key] = task; }

            task.ContinueWith(t =>
            {
                lock (_asyncLock) { _asyncInFlight.Remove(key); }

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
                var taskResult = resultProp?.GetValue(t);

                lock (_asyncLock)
                {
                    if (!_asyncCache.ContainsKey(key))
                    {
                        var node = _asyncOrder.AddLast(new KeyValuePair<string, object>(key, taskResult));
                        _asyncCache[key] = node;
                        while (_asyncCache.Count > AsyncCacheMax)
                        {
                            var oldest = _asyncOrder.First;
                            _asyncOrder.RemoveFirst();
                            _asyncCache.Remove(oldest.Value.Key);
                        }
                    }
                }

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

        /// <summary>
        /// Structural cache key for a method call. Primitives, strings,
        /// enums, decimals, and Rhino.Geometry value types use their stable
        /// ToString. Collections recurse element-wise. Other reference types
        /// fall back to TypeName + RuntimeHelpers.GetHashCode (identity-ish)
        /// — better than naive ToString collisions, but documented as a
        /// best-effort key for non-value reference types.
        /// </summary>
        private static string AsyncKey(object[] args)
        {
            var sb = new System.Text.StringBuilder(args.Length * 16);
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append('|');
                AppendArgKey(sb, args[i]);
            }
            return sb.ToString();
        }

        private static void AppendArgKey(System.Text.StringBuilder sb, object a)
        {
            if (a == null) { sb.Append('n'); return; }
            var t = a.GetType();

            // Synthetic per-solve params are NOT real inputs: the runtime hands
            // the method a fresh CancellationToken and a fresh IProgress<T> sink
            // on EVERY solve. Their instance identity therefore changes each
            // pass — and if they leak into the key (the reference/boxed-struct
            // fallback below hashes instance identity), the async cache never
            // hits, so every solve relaunches the task while BeforeSolveInstance
            // cancels the prior one: an infinite "computing" livelock. Fold them
            // to a constant so the key depends only on the real wired inputs.
            if (a is CancellationToken) { sb.Append("ct"); return; }
            if (t.GetInterfaces().Any(i => i.IsGenericType &&
                                           i.GetGenericTypeDefinition() == typeof(IProgress<>)))
            { sb.Append("pg"); return; }

            if (t.IsPrimitive || a is string || a is DateTime || a is decimal)
            { sb.Append(t.Name); sb.Append(':'); sb.Append(a); return; }

            if (t.IsEnum)
            { sb.Append("e:"); sb.Append(t.Name); sb.Append(':'); sb.Append(Convert.ToInt64(a)); return; }

            if (t.Namespace == "Rhino.Geometry")
            { sb.Append('r'); sb.Append(t.Name); sb.Append(':'); sb.Append(a); return; }

            if (a is System.Collections.IEnumerable enumerable && a is not string)
            {
                sb.Append('[');
                foreach (var item in enumerable) { AppendArgKey(sb, item); sb.Append(','); }
                sb.Append(']'); return;
            }

            // Value-type fallback (structs not special-cased above: Guid, Color,
            // Interval, Transform, BoundingBox…). Use their ToString — it is
            // deterministic for a given value, unlike the boxed instance's
            // identity hash, which changes every solve and would defeat the
            // cache the same way the synthetic params do.
            if (t.IsValueType)
            { sb.Append('v'); sb.Append(t.FullName); sb.Append(':'); sb.Append(a); return; }

            // Reference-type fallback: type name + identity hash. Stable for the
            // same object, collides only for genuinely identical references —
            // not great for mutable value-bag classes, but doesn't silently
            // merge unrelated instances the way bare ToString did.
            sb.Append('o'); sb.Append(t.FullName); sb.Append(':');
            sb.Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a));
        }

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

        // ---- Right-click menu state ------------------------------------
        //
        // Parameters tagged [GrasshopperMenuState] surface as right-click
        // menu entries instead of wired inputs. v0.5 supports enum-typed and
        // bool-typed menu state out of the box; other types are accepted as
        // pass-through (the persisted value is used at solve time but no UI
        // editor is generated — author can add one later).

        public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            if (_descriptor?.Method == null) return;

            // Trigger: append a top-of-menu "Run" (or attribute-supplied
            // label) item that fires the component exactly once. Multiple
            // [GrasshopperTrigger] params on the same method share the same
            // Run item — they always agree.
            if (_descriptor.HasTrigger)
            {
                var triggerAttr = _descriptor.Method.GetParameters()
                    .Select(p => p.GetCustomAttribute<Betta.Attributes.GrasshopperTriggerAttribute>())
                    .FirstOrDefault(a => a != null);
                var label = triggerAttr?.Label ?? "Run";
                Grasshopper.Kernel.GH_DocumentObject.Menu_AppendItem(
                    menu, label,
                    (sender, e) => { _runRequested = true; ExpireSolution(true); });
                Grasshopper.Kernel.GH_DocumentObject.Menu_AppendSeparator(menu);
            }

            foreach (var p in _descriptor.Method.GetParameters())
            {
                var attr = p.GetCustomAttribute<Betta.Attributes.GrasshopperMenuStateAttribute>();
                if (attr == null) continue;

                var displayName = attr.Name
                    ?? p.GetCustomAttribute<Betta.Attributes.GrasshopperParameterAttribute>()?.Name
                    ?? p.Name;

                var current = _menuState.TryGetValue(p.Name, out var v) ? v : null;

                if (p.ParameterType.IsEnum)
                {
                    var sub = Grasshopper.Kernel.GH_DocumentObject.Menu_AppendItem(menu, displayName);
                    sub.DropDownItems.Clear();
                    foreach (var name in Enum.GetNames(p.ParameterType))
                    {
                        var value = Enum.Parse(p.ParameterType, name);
                        var item = Grasshopper.Kernel.GH_DocumentObject.Menu_AppendItem(
                            sub.DropDown, name,
                            (sender, e) =>
                            {
                                _menuState[p.Name] = value;
                                ExpireSolution(true);
                            },
                            true,
                            current != null && current.Equals(value));
                    }
                }
                else if (p.ParameterType == typeof(bool))
                {
                    bool isOn = current is bool b && b;
                    Grasshopper.Kernel.GH_DocumentObject.Menu_AppendItem(
                        menu, displayName,
                        (sender, e) =>
                        {
                            _menuState[p.Name] = !isOn;
                            ExpireSolution(true);
                        },
                        true,
                        isOn);
                }
                // Other types: not editable from the menu in v0.5. The
                // persisted value (or the [GrasshopperParameter] default) is
                // still passed to the method body.
            }
        }

        /// <summary>
        /// When the component is dropped onto the canvas, auto-attach a
        /// <c>GH_ValueList</c> to every input whose CLR parameter is decorated
        /// with <c>[GrasshopperValueList]</c>. Skips inputs the user has
        /// already wired so re-placement (or opening a saved .gh) doesn't
        /// clobber their existing sources. Runs once at add time; users are
        /// free to delete or rewire afterwards.
        /// </summary>
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);

            if (_descriptor?.Method == null || document == null) return;

            var pars = _descriptor.Method.GetParameters();
            int inputIdx = 0;
            for (int i = 0; i < pars.Length; i++)
            {
                var p = pars[i];
                if (IsSyntheticOrHidden(p)) continue;

                if (inputIdx >= Params.Input.Count) break;

                var attr = p.GetCustomAttribute<Betta.Attributes.GrasshopperValueListAttribute>();
                if (attr?.Items != null && attr.Items.Length > 0)
                    AttachValueList(document, Params.Input[inputIdx], attr.Items, p.ParameterType);

                inputIdx++;
            }
        }

        private static bool IsSyntheticOrHidden(ParameterInfo p) =>
            ParamInjector.IsSyntheticParameter(p.ParameterType) ||
            ParamInjector.IsMenuStateParameter(p) ||
            ParamInjector.IsSecretParameter(p) ||
            ParamInjector.IsTriggerParameter(p);

        private static void AttachValueList(GH_Document document, IGH_Param input, string[] items, Type targetType)
        {
            if (input == null || input.SourceCount > 0) return;

            var vl = new Grasshopper.Kernel.Special.GH_ValueList();
            vl.ListItems.Clear();
            foreach (var raw in items)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var expr = FormatValueListExpression(raw, targetType);
                vl.ListItems.Add(new Grasshopper.Kernel.Special.GH_ValueListItem(raw, expr));
            }
            vl.NewInstanceGuid();
            vl.CreateAttributes();
            // Offset to the upper left of the input's pin so the wire lands
            // naturally on the target parameter without overlapping the
            // component body.
            vl.Attributes.Pivot = new System.Drawing.PointF(
                input.Attributes.Pivot.X - 220,
                input.Attributes.Pivot.Y - 15);

            document.AddObject(vl, false);
            input.AddSource(vl);
            // Ensure the first item is selected so the value list wire carries
            // something meaningful without the user having to open the dropdown.
            vl.SelectItem(0);
        }

        private static string FormatValueListExpression(string raw, Type targetType)
        {
            var s = raw.Trim();

            // Non-string primitive targets: prefer raw numeric / bool literals
            // so GH expression parsing lands on the right primitive type.
            if (targetType == typeof(bool) &&
                (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("false", StringComparison.OrdinalIgnoreCase)))
                return s.ToLowerInvariant();

            if (targetType != typeof(string))
            {
                if (int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                    return s;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                    return s;
            }

            // Default: quoted string, escape internal quotes.
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            // Persist menu state as typed slots so Read can reconstruct.
            int idx = 0;
            foreach (var kv in _menuState)
            {
                writer.SetString($"betta_ms_key_{idx}", kv.Key);
                var type = kv.Value?.GetType();
                if (type == null) { writer.SetString($"betta_ms_kind_{idx}", "null"); }
                else if (type.IsEnum)
                {
                    writer.SetString($"betta_ms_kind_{idx}", "enum");
                    writer.SetString($"betta_ms_etype_{idx}", type.AssemblyQualifiedName ?? type.FullName);
                    writer.SetInt64($"betta_ms_val_{idx}", Convert.ToInt64(kv.Value));
                }
                else if (kv.Value is bool bv) { writer.SetString($"betta_ms_kind_{idx}", "bool"); writer.SetBoolean($"betta_ms_val_{idx}", bv); }
                else if (kv.Value is int iv) { writer.SetString($"betta_ms_kind_{idx}", "int"); writer.SetInt32($"betta_ms_val_{idx}", iv); }
                else if (kv.Value is double dv) { writer.SetString($"betta_ms_kind_{idx}", "double"); writer.SetDouble($"betta_ms_val_{idx}", dv); }
                else if (kv.Value is string sv) { writer.SetString($"betta_ms_kind_{idx}", "string"); writer.SetString($"betta_ms_val_{idx}", sv); }
                else { writer.SetString($"betta_ms_kind_{idx}", "skip"); }
                idx++;
            }
            writer.SetInt32("betta_ms_count", _menuState.Count);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            _menuState.Clear();
            if (reader.ItemExists("betta_ms_count"))
            {
                int count = reader.GetInt32("betta_ms_count");
                for (int idx = 0; idx < count; idx++)
                {
                    var key = reader.ItemExists($"betta_ms_key_{idx}") ? reader.GetString($"betta_ms_key_{idx}") : null;
                    var kind = reader.ItemExists($"betta_ms_kind_{idx}") ? reader.GetString($"betta_ms_kind_{idx}") : "null";
                    if (string.IsNullOrEmpty(key) || kind == "null" || kind == "skip") continue;
                    try
                    {
                        switch (kind)
                        {
                            case "enum":
                                var tn = reader.GetString($"betta_ms_etype_{idx}");
                                var et = Type.GetType(tn, throwOnError: false);
                                if (et != null) _menuState[key] = Enum.ToObject(et, reader.GetInt64($"betta_ms_val_{idx}"));
                                break;
                            case "bool":   _menuState[key] = reader.GetBoolean($"betta_ms_val_{idx}"); break;
                            case "int":    _menuState[key] = reader.GetInt32($"betta_ms_val_{idx}"); break;
                            case "double": _menuState[key] = reader.GetDouble($"betta_ms_val_{idx}"); break;
                            case "string": _menuState[key] = reader.GetString($"betta_ms_val_{idx}"); break;
                        }
                    }
                    catch { /* skip a single malformed entry rather than fail the whole read */ }
                }
            }
            return base.Read(reader);
        }
    }
}
