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
using Grasshopper.Kernel;
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
                var parameter = new ParamVector(input).ToGhParam();
                ParamServer.RegisterInputParam(parameter);
            }
        }

        public void GenerateOutputs()
        {
            var declared = Outputs;

            // Task<T> / ValueTask<T>: unwrap so the output param(s) reflect T,
            // not the Task wrapper. The runtime's async path already hands
            // the unwrapped result to SetOutputDataAdvanced.
            if (declared != null && declared.IsGenericType)
            {
                var gt = declared.GetGenericTypeDefinition();
                if (gt == typeof(System.Threading.Tasks.Task<>) ||
                    gt == typeof(System.Threading.Tasks.ValueTask<>))
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
            if (inputs.Any(x => x.VolatileData.PathCount != 1))
                throw new Exception("Item inputs in trees are not supported yet");

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

                    var branch = input.VolatileData.get_Branch(0);
                    var index = i < input.VolatileDataCount ? i : input.VolatileDataCount - 1;
                    if (index < 0) continue;

                    expandoDict[Inputs[idx].Name] = UnwrapGoo(branch[index]);
                }

                expandoList.Add(expandoObject);
            }

            return expandoList;
        }

        public List<ExpandoObject> GetListData(List<ExpandoObject> expandoObjects)
        {
            var inputs = ParamServer.Where(x => x.Kind == GH_ParamKind.input).ToList();
            if (inputs.Any(x => x.VolatileData.PathCount != 1))
                throw new Exception("List inputs in trees are not supported yet");

            for (int idx = 0; idx < inputs.Count; idx++)
            {
                var input = inputs[idx];
                if (input.Access != GH_ParamAccess.list) continue;

                var branch = input.VolatileData.get_Branch(0);
                var paramValues = new List<dynamic>(branch.Count);
                for (int j = 0; j < branch.Count; j++)
                    paramValues.Add(UnwrapGoo(branch[j]));

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
        /// </summary>
        public object[] BuildArguments(ExpandoObject batch)
        {
            if (batch == null || Inputs == null || !Inputs.Any())
                return Array.Empty<object>();

            var dict = (IDictionary<string, object>)batch;
            var ordered = new List<object>(Inputs.Count);

            foreach (var p in Inputs)
            {
                dict.TryGetValue(p.Name, out var value);
                ordered.Add(ChangeTypeStrong(value, p.ParameterType));
            }
            return ordered.ToArray();
        }

        private static object ChangeTypeStrong(object obj, Type targetType)
        {
            if (obj == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType.IsAssignableFrom(obj.GetType()))
                return obj;

            if (targetType == typeof(string))
                return Convert.ToString(obj);

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

            if (IsSimpleType(resultType))
            {
                DA.SetData(0, result);
                return;
            }

            if (result is IEnumerable enumerable && result is not string)
            {
                DA.SetDataList(0, enumerable.Cast<object>().ToList());
                return;
            }

            if (IsTupleType(resultType))
            {
                var tupleValues = GetTupleValues(result);
                for (int i = 0; i < tupleValues.Length && i < 8; i++)
                    DA.SetData(i, tupleValues[i]);
                return;
            }

            if (resultType.IsClass && resultType != typeof(string))
            {
                var properties = resultType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && IsSimpleType(p.PropertyType))
                    .Take(10)
                    .ToArray();

                for (int i = 0; i < properties.Length; i++)
                {
                    try { DA.SetData(i, properties[i].GetValue(result)); }
                    catch { /* skip properties that can't be read */ }
                }
                return;
            }

            DA.SetData(0, result);
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
