// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Betta.Attributes;
using Betta.Goo;
using Betta.Interfaces;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Betta
{
    public class ParamInjector
    {
        public MethodInfo Method;
        public ExpandoObject MethodArgumentsExpando;
        public List<ParameterInfo> Inputs = new();
        public Type Outputs;
        private readonly GH_ComponentParamServer ParamServer;

        public ParamInjector(MethodInfo method, GH_ComponentParamServer paramsServer)
        {
            ParamServer = paramsServer;
            if (method != null)
            {
                Method = method;
                Outputs = method.ReturnType;
                Inputs = method.GetParameters().ToList();
                MethodArgumentsExpando = Inputs.ToExpando();
            }
        }

        public void GenerateInputs()
        {
            if (Inputs == null) return;
            foreach (var input in Inputs)
            {
                // Synthetic parameters — CancellationToken and IProgress<T> —
                // are not wired inputs. They're filled at solve time by the
                // component (with its own CTS and a Message-updating callback).
                if (IsSyntheticParameter(input.ParameterType)) continue;

                // Per-instance menu state — picked via right-click menu, not
                // wired. The component owns the dictionary of current values.
                if (IsMenuStateParameter(input)) continue;

                // [GrasshopperSecret] — value comes from the OS credential
                // store at solve time, keyed by the attribute's Service.
                if (IsSecretParameter(input)) continue;

                // [GrasshopperTrigger] — value is a per-solve bool set to
                // true only when the user clicked "Run" in the component's
                // menu. No input pin; menu-driven.
                if (IsTriggerParameter(input)) continue;

                var parameter = new ParamVector(input).ToGhParam();
                ParamServer.RegisterInputParam(parameter);
            }
        }

        public static bool IsSyntheticParameter(Type t) =>
            t == typeof(CancellationToken) || IsProgressType(t);

        public static bool IsProgressType(Type t) =>
            t != null && t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IProgress<>);

        public static bool IsMenuStateParameter(ParameterInfo p) =>
            p?.GetCustomAttribute<GrasshopperMenuStateAttribute>() != null;

        public static bool IsSecretParameter(ParameterInfo p) =>
            p?.GetCustomAttribute<GrasshopperSecretAttribute>() != null;

        public static bool IsTriggerParameter(ParameterInfo p) =>
            p?.GetCustomAttribute<GrasshopperTriggerAttribute>() != null;

        public void GenerateOutputs()
        {
            var declared = Outputs;

            // Task<T> / ValueTask<T> / IObservable<T>: unwrap so the output
            // param(s) reflect T, not the wrapper. The runtime's async and
            // streaming paths both hand the unwrapped T to SetOutputDataAdvanced.
            if (declared != null && declared.IsGenericType)
            {
                var gt = declared.GetGenericTypeDefinition();
                if (gt == typeof(System.Threading.Tasks.Task<>) ||
                    gt == typeof(System.Threading.Tasks.ValueTask<>) ||
                    gt == typeof(IObservable<>))
                {
                    declared = declared.GetGenericArguments()[0];
                }
            }

            foreach (var outpar in ParamVector.GetOutputs(declared, Method))
                ParamServer.RegisterOutputParam(outpar);
        }

        public List<ExpandoObject> GetItemData()
        {
            var inputs = ParamServer.Where(x => x.Kind == GH_ParamKind.input).ToList();
            // Item/list inputs are read as one logical, flattened list across ALL branches (in path order) —
            // a multi-branch tree collapses to a flat sequence rather than being rejected. Tree structure is
            // honoured only for tree-access params, which BuildArguments hands the whole GH_Structure.

            var itemInputs = inputs.Where(x => x.Access == GH_ParamAccess.item).ToList();
            var maxItemCount = itemInputs.Any() ? itemInputs.Max(x => x.VolatileData.DataCount) : 1;

            var expandoList = new List<ExpandoObject>();
            for (int i = 0; i < maxItemCount; i++)
            {
                var expandoObject = new ExpandoObject();
                var expandoDict = (IDictionary<string, dynamic>)expandoObject;

                for (int idx = 0; idx < inputs.Count; idx++)
                {
                    var input = inputs[idx];
                    if (input.Access != GH_ParamAccess.item) continue;

                    // Flatten every branch into one sequence; an empty/unwired optional input yields an empty
                    // list, so the arg is left absent and BuildArguments supplies its default.
                    var flat = FlattenBranches(input.VolatileData);
                    if (flat.Count == 0) continue;

                    // GH "repeat last item" semantics: shorter inputs clamp to their final element.
                    var index = i < flat.Count ? i : flat.Count - 1;
                    expandoDict[Inputs[idx].Name] = UnwrapGoo(flat[index]);
                }

                expandoList.Add(expandoObject);
            }

            return expandoList;
        }

        /// <summary>
        /// Flatten a GH data tree to a single ordered list of items across every branch (path order). Used to
        /// read item/list-access inputs as one logical list, so a multi-branch tree is accepted (collapsed)
        /// rather than rejected. The count matches <c>VolatileData.DataCount</c>, so item indexing stays aligned.
        /// </summary>
        private static List<object> FlattenBranches(IGH_Structure data)
        {
            var flat = new List<object>();
            if (data == null) return flat;
            for (int p = 0; p < data.PathCount; p++)
            {
                var branch = data.get_Branch(p);
                if (branch == null) continue;
                foreach (var item in branch) flat.Add(item);
            }
            return flat;
        }

        public List<ExpandoObject> GetListData(List<ExpandoObject> expandoObjects)
        {
            var inputs = ParamServer.Where(x => x.Kind == GH_ParamKind.input).ToList();
            // Same rule as GetItemData: a list-access input reads every branch flattened into one list, so a
            // multi-branch tree is accepted (collapsed) rather than rejected. Tree-access params keep their tree.

            for (int idx = 0; idx < inputs.Count; idx++)
            {
                var input = inputs[idx];
                if (input.Access != GH_ParamAccess.list) continue;

                // Empty/unwired optional list input: leave the arg absent so BuildArguments supplies its default.
                var flat = FlattenBranches(input.VolatileData);
                if (flat.Count == 0) continue;

                var paramValues = new List<dynamic>(flat.Count);
                foreach (var item in flat)
                    paramValues.Add(UnwrapGoo(item));

                foreach (var expandoObject in expandoObjects)
                    ((IDictionary<string, dynamic>)expandoObject)[Inputs[idx].Name] = paramValues;
            }
            return expandoObjects;
        }

        /// <summary>
        /// Unwrap an IGH_Goo to its CLR payload. Uses the interface call (not
        /// dynamic dispatch) to avoid RuntimeBinderException under .NET 7.
        /// Falls back to the raw object for anything that does not implement
        /// IGH_Goo (rare, but cheap to guard against).
        /// </summary>
        private static object UnwrapGoo(object item)
        {
            if (item is IGH_Goo goo) return goo.ScriptVariable();
            return item;
        }

        /// <summary>
        /// Build ordered, strongly-typed argument array for Method.Invoke.
        /// Tree-typed inputs (GH_Structure&lt;TGoo&gt;) bypass the per-iteration
        /// expando bag and are taken straight from the GH input's VolatileData
        /// so the method body sees the entire tree intact. CancellationToken
        /// and IProgress&lt;T&gt; parameters are synthesized from the component's
        /// own CTS / progress sink — they never appear as wired inputs.
        /// </summary>
        public object[] BuildArguments(
            ExpandoObject batch,
            CancellationToken cancellationToken = default,
            Action<object> reportProgress = null,
            IDictionary<string, object> menuState = null,
            bool triggered = false)
        {
            if (Inputs == null || !Inputs.Any())
                return Array.Empty<object>();

            var dict = batch != null
                ? (IDictionary<string, object>)batch
                : new Dictionary<string, object>();
            // Match GH input order to CLR parameter order by skipping the
            // synthetic ones (CancellationToken / IProgress<T> / menu state /
            // secret / trigger) on the CLR side. The GH input list never
            // contains them.
            var ghInputs = ParamServer.Where(x => x.Kind == GH_ParamKind.input).ToList();
            var ordered = new List<object>(Inputs.Count);

            int ghIdx = 0;
            for (int i = 0; i < Inputs.Count; i++)
            {
                var p = Inputs[i];

                // Synthetic: CancellationToken → component's CTS token.
                if (p.ParameterType == typeof(CancellationToken))
                {
                    ordered.Add(cancellationToken);
                    continue;
                }

                // Synthetic: IProgress<T> → wraps the component's progress sink.
                if (IsProgressType(p.ParameterType))
                {
                    ordered.Add(BuildProgressInstance(p.ParameterType, reportProgress));
                    continue;
                }

                // Synthetic: [GrasshopperSecret] → read from the OS credential
                // store keyed by the attribute's Service. Missing values raise
                // a validation exception that BettaComponent turns into a
                // Warning bar and skips the invocation.
                var secretAttr = p.GetCustomAttribute<GrasshopperSecretAttribute>();
                if (secretAttr != null)
                {
                    if (Betta.Services.SecretStore.TryGet(secretAttr.Service, out var secretVal))
                    {
                        ordered.Add(secretVal);
                    }
                    else
                    {
                        throw new Betta.Services.BettaValidationException(
                            $"Missing secret '{secretAttr.Service}'"
                            + (string.IsNullOrEmpty(secretAttr.Prompt) ? "" : " — " + secretAttr.Prompt)
                            + $". Set it via Betta_Secrets.");
                    }
                    continue;
                }

                // Synthetic: [GrasshopperTrigger] → per-solve bool, true only
                // when the user clicked "Run" this pass. Method bodies can
                // early-return on false to skip expensive work.
                if (p.GetCustomAttribute<GrasshopperTriggerAttribute>() != null)
                {
                    ordered.Add(triggered);
                    continue;
                }

                // Menu state: pull from the component's persisted dictionary,
                // or fall back to the [GrasshopperParameter] DefaultValue,
                // or to default(T) as a last resort.
                if (IsMenuStateParameter(p))
                {
                    if (menuState != null && menuState.TryGetValue(p.Name, out var msValue))
                    {
                        ordered.Add(ChangeTypeStrong(msValue, p.ParameterType));
                    }
                    else
                    {
                        var defaultVal = p.GetCustomAttribute<GrasshopperParameterAttribute>()?.DefaultValue;
                        if (defaultVal != null)
                            ordered.Add(ChangeTypeStrong(defaultVal, p.ParameterType));
                        else if (p.ParameterType.IsValueType)
                            ordered.Add(Activator.CreateInstance(p.ParameterType));
                        else
                            ordered.Add(null);
                    }
                    continue;
                }

                // Tree input: hand the whole GH_Structure to the method body.
                if (ghIdx < ghInputs.Count && ghInputs[ghIdx].Access == GH_ParamAccess.tree)
                {
                    ordered.Add(BuildTreeArg(ghInputs[ghIdx].VolatileData, p.ParameterType));
                    ghIdx++;
                    continue;
                }

                dict.TryGetValue(p.Name, out var value);
                ordered.Add(ChangeTypeStrong(value, p.ParameterType));
                ghIdx++;
            }
            return ordered.ToArray();
        }

        /// <summary>
        /// Build a closed-generic <c>IProgress&lt;T&gt;</c> that forwards each
        /// <c>Report(T)</c> call to the component-supplied object-typed sink.
        /// Uses a tiny generic bridge so we don't need Expression Trees.
        /// </summary>
        private static object BuildProgressInstance(Type progressType, Action<object> sink)
        {
            var tArg = progressType.GetGenericArguments()[0];
            var bridge = typeof(ProgressBridge<>).MakeGenericType(tArg);
            return Activator.CreateInstance(bridge, sink);
        }

        private class ProgressBridge<T> : IProgress<T>
        {
            private readonly Action<object> _sink;
            public ProgressBridge(Action<object> sink) { _sink = sink; }
            public void Report(T value) => _sink?.Invoke(value);
        }

        /// <summary>
        /// Produce the tree-typed argument the method body expects from the
        /// GH input's VolatileData. Direct cast when the parameter is
        /// GH_Structure&lt;TGoo&gt;; null fallback otherwise so the method
        /// body can null-check rather than blowing up on a bad cast.
        /// </summary>
        private static object BuildTreeArg(IGH_Structure volatileData, Type parameterType)
        {
            if (parameterType == null || volatileData == null) return null;
            if (parameterType.IsInstanceOfType(volatileData)) return volatileData;
            return null;
        }

        private static object ChangeTypeStrong(object obj, Type targetType)
        {
            if (obj == null)
            {
                if (targetType.IsValueType) return Activator.CreateInstance(targetType);
                // Opt-in default for opaque types: if the type marks itself
                // IBettaDefault and exposes a parameterless ctor, hand the
                // method body a fresh instance instead of null.
                if (typeof(IBettaDefault).IsAssignableFrom(targetType) &&
                    targetType.GetConstructor(Type.EmptyTypes) != null)
                    return Activator.CreateInstance(targetType);
                return null;
            }

            if (targetType.IsAssignableFrom(obj.GetType()))
                return obj;

            // Implicit upcasts to the common geometry parents — these are the
            // conversions GH users expect to "just work" when they wire a
            // Line into a Curve socket etc.
            if (targetType == typeof(Rhino.Geometry.Curve))
            {
                if (obj is Rhino.Geometry.Line line) return line.ToNurbsCurve();
                if (obj is Rhino.Geometry.Arc arc) return arc.ToNurbsCurve();
                if (obj is Rhino.Geometry.Circle circle) return circle.ToNurbsCurve();
                if (obj is Rhino.Geometry.Polyline poly) return poly.ToPolylineCurve();
            }
            if (targetType == typeof(Rhino.Geometry.Brep))
            {
                if (obj is Rhino.Geometry.Surface surf) return surf.ToBrep();
            }

            if (targetType == typeof(string))
                return Convert.ToString(obj);

            // Enum target: accept int (or any integral) and string (the enum
            // member name, case-insensitive). Convert.ChangeType throws for
            // either direction on its own.
            if (targetType.IsEnum)
            {
                try
                {
                    if (obj is string s)
                        return Enum.Parse(targetType, s, ignoreCase: true);
                    var underlying = Enum.GetUnderlyingType(targetType);
                    var asUnderlying = Convert.ChangeType(obj, underlying);
                    return Enum.ToObject(targetType, asUnderlying);
                }
                catch
                {
                    return Activator.CreateInstance(targetType);
                }
            }

            if (typeof(IEnumerable).IsAssignableFrom(targetType) &&
                targetType.IsGenericType &&
                targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var itemType = targetType.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(itemType);
                var typedList = (IList)Activator.CreateInstance(listType);

                IEnumerable source = obj switch
                {
                    string => new[] { obj },
                    IEnumerable enumerable => enumerable,
                    _ => new[] { obj }
                };

                foreach (var item in source)
                    typedList.Add(ChangeTypeStrong(item, itemType));

                return typedList;
            }

            // Array targets — covers both explicit T[] params and the implicit
            // `params T[]` syntax. GH wires the list of values to a single
            // list-access input; we coerce element-by-element into a typed
            // array the method body can receive.
            if (targetType.IsArray)
            {
                var itemType = targetType.GetElementType();
                IEnumerable source = obj switch
                {
                    string => new[] { obj },
                    IEnumerable enumerable => enumerable,
                    _ => new[] { obj }
                };
                var bag = new List<object>();
                foreach (var item in source) bag.Add(ChangeTypeStrong(item, itemType));
                var arr = Array.CreateInstance(itemType, bag.Count);
                for (int i = 0; i < bag.Count; i++) arr.SetValue(bag[i], i);
                return arr;
            }

            try
            {
                return Convert.ChangeType(obj, targetType);
            }
            catch
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }
        }

        public void SetOutputDataAdvanced(IGH_DataAccess DA, object result)
        {
            if (result == null) return;

            var resultType = result.GetType();

            // Tree return: GH_Structure<TGoo> ships as a tree wire. Must come
            // BEFORE the enumerable branch below because GH_Structure
            // implements IEnumerable and would otherwise emit as a list.
            if (OutputPlanner.IsGhStructureType(resultType))
            {
                DA.SetDataTree(0, (IGH_Structure)result);
                return;
            }

            // Opaque domain value: wrap in GH_BettaGoo<T> so the typed
            // Param_BettaGoo<T> output accepts it as a single typed wire,
            // instead of exploding into per-property outputs.
            bool methodReturnOpaque = OpaqueDetection.IsMethodReturnOpaque(Method);
            if (OpaqueDetection.IsTypeOpaque(resultType) || methodReturnOpaque)
            {
                DA.SetData(0, WrapOpaque(result));
                return;
            }

            if (IsSimpleType(resultType))
            {
                DA.SetData(0, result);
                return;
            }

            if (result is IEnumerable enumerable && result is not string)
            {
                // List<T> of opaque T: wrap each item.
                var elementType = GetEnumerableElementType(resultType);
                if (elementType != null && OpaqueDetection.IsTypeOpaque(elementType))
                {
                    var wrapped = new List<object>();
                    foreach (var item in enumerable)
                        wrapped.Add(WrapOpaque(item));
                    DA.SetDataList(0, wrapped);
                    return;
                }
                DA.SetDataList(0, enumerable.Cast<object>().ToList());
                return;
            }

            if (IsTupleType(resultType))
            {
                var tupleValues = GetTupleValues(result);
                for (int i = 0; i < tupleValues.Length && i < 8; i++)
                    EmitOutput(DA, i, tupleValues[i]);
                return;
            }

            if (resultType.IsClass && resultType != typeof(string))
            {
                // Match OutputPlanner's class-return planning EXACTLY (same GetProperties order,
                // same IsExplodableProperty filter, same Take(10)) so set-indices line up with the
                // planned outputs — and route each value through EmitOutput so List<T>/opaque
                // properties emit as list / typed wires instead of being dropped or ToString()'d.
                var properties = resultType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && OutputPlanner.IsExplodableProperty(p.PropertyType))
                    .Take(10)
                    .ToArray();

                for (int i = 0; i < properties.Length; i++)
                {
                    object value;
                    try { value = properties[i].GetValue(result); }
                    catch { continue; /* skip properties that can't be read */ }
                    EmitOutput(DA, i, value);
                }
                return;
            }

            DA.SetData(0, result);
        }

        /// <summary>
        /// Emit one output value at <paramref name="index"/> the way the top-level single-value
        /// path does: a tree as a tree, an opaque value wrapped in GH_BettaGoo&lt;T&gt;, a
        /// non-string IEnumerable as a LIST (wrapping opaque elements), a simple value directly.
        /// Used for tuple ELEMENTS and class-return PROPERTIES, which previously went through a
        /// blind <c>DA.SetData</c> that handed a whole <c>List&lt;T&gt;</c> to a list-access output
        /// as one object (rendered as "System.Collections.Generic.List`1[...]") and silently
        /// dropped opaque / <c>List&lt;opaque&gt;</c> values.
        /// </summary>
        private void EmitOutput(IGH_DataAccess DA, int index, object value)
        {
            if (value == null) return;

            var valueType = value.GetType();

            if (OutputPlanner.IsGhStructureType(valueType))
            {
                DA.SetDataTree(index, (IGH_Structure)value);
                return;
            }

            if (OpaqueDetection.IsTypeOpaque(valueType))
            {
                DA.SetData(index, WrapOpaque(value));
                return;
            }

            if (IsSimpleType(valueType))
            {
                DA.SetData(index, value);
                return;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var elementType = GetEnumerableElementType(valueType);
                if (elementType != null && OpaqueDetection.IsTypeOpaque(elementType))
                {
                    var wrapped = new List<object>();
                    foreach (var item in enumerable)
                        wrapped.Add(WrapOpaque(item));
                    DA.SetDataList(index, wrapped);
                    return;
                }
                DA.SetDataList(index, enumerable.Cast<object>().ToList());
                return;
            }

            DA.SetData(index, value);
        }

        /// <summary>
        /// Wrap an opaque CLR value in a GH_BettaGoo&lt;T&gt; whose T matches the
        /// value's runtime type. Constructed via reflection because the runtime
        /// type isn't known statically here.
        /// </summary>
        private static object WrapOpaque(object value)
        {
            if (value == null) return null;
            var gooType = typeof(GH_BettaGoo<>).MakeGenericType(value.GetType());
            return Activator.CreateInstance(gooType, value);
        }

        private static Type GetEnumerableElementType(Type t)
        {
            if (t == null) return null;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return t.GetGenericArguments()[0];
            foreach (var iface in t.GetInterfaces())
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            return null;
        }

        private static object[] GetTupleValues(object tuple)
        {
            var tupleType = tuple.GetType();
            if (!tupleType.IsGenericType) return new[] { tuple };

            var arity = tupleType.GetGenericArguments().Length;
            var values = new object[arity];

            // System.Tuple<...>: exposes Item1..Item8 as public properties.
            // System.ValueTuple<...>: exposes Item1..Item8 as public fields.
            // Either way, look them up by name so reflection order drift
            // can't misalign Output1/Output2/Output3 with the declared types.
            bool isValueTuple = tupleType.FullName?.StartsWith("System.ValueTuple") == true;
            bool isRefTuple = tupleType.GetGenericTypeDefinition().FullName?.StartsWith("System.Tuple") == true;

            if (!isValueTuple && !isRefTuple) return new[] { tuple };

            for (int i = 0; i < arity; i++)
            {
                var name = $"Item{i + 1}";
                if (isValueTuple)
                    values[i] = tupleType.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(tuple);
                else
                    values[i] = tupleType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(tuple);
            }
            return values;
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
                   type == typeof(string) ||
                   type == typeof(DateTime) ||
                   type == typeof(decimal) ||
                   type.Namespace == "Rhino.Geometry" ||
                   type.IsEnum;
        }

        public bool IsTupleType(Type type)
        {
            return type.IsGenericType &&
                   (type.GetGenericTypeDefinition().FullName?.StartsWith("System.Tuple") == true ||
                    type.FullName?.StartsWith("System.ValueTuple") == true);
        }

        public bool IsComplexObject(Type type)
        {
            return type.IsClass &&
                   type != typeof(string) &&
                   !IsSimpleType(type) &&
                   type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                       .Any(p => p.CanRead && IsSimpleType(p.PropertyType));
        }
    }
}
