// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Betta.Attributes;

namespace Betta
{
    /// <summary>
    /// One planned output — pure data, no Grasshopper types. Keeping the
    /// output-naming logic free of GH types means it can be unit-tested without
    /// a running Rhino. <see cref="ParamVector.GetOutputs(Type, MethodInfo)"/>
    /// maps each plan to a concrete <c>IGH_Param</c>.
    /// </summary>
    public readonly struct OutputPlan
    {
        public readonly string Name;
        public readonly string NickName;
        public readonly string Description;
        public readonly Type Type;
        public readonly bool IsList;

        public OutputPlan(string name, string nickName, string description, Type type, bool isList)
        {
            Name = name;
            NickName = nickName;
            Description = description;
            Type = type;
            IsList = isList;
        }
    }

    /// <summary>
    /// Resolves a method's return type into the set of outputs it should expose,
    /// with display names. Naming priority, per output:
    ///   1. <c>[GrasshopperOutput(...)]</c> on the method or its return value
    ///      (use <c>Index</c> to target a tuple element; AllowMultiple).
    ///   2. ValueTuple element names — <c>(double Sum, double Average)</c>
    ///      yields outputs named "Sum"/"Average" automatically.
    ///   3. Defaults — "Output", or "Output1".."OutputN" for tuples, or the
    ///      property name for class returns.
    /// </summary>
    public static class OutputPlanner
    {
        public static List<OutputPlan> PlanOutputs(Type outputType, MethodInfo method)
        {
            var plans = new List<OutputPlan>();
            if (outputType == null || outputType == typeof(void))
                return plans; // no outputs for void methods

            // Explicit [GrasshopperOutput] attributes (method-level + return-value),
            // resolved by Index. AllowMultiple lets a tuple method name each element.
            var outputAttrs = method == null
                ? new List<GrasshopperOutputAttribute>()
                : method.GetCustomAttributes<GrasshopperOutputAttribute>()
                    .Concat(method.ReturnParameter?.GetCustomAttributes<GrasshopperOutputAttribute>()
                            ?? Enumerable.Empty<GrasshopperOutputAttribute>())
                    .ToList();
            GrasshopperOutputAttribute AttrFor(int index) =>
                outputAttrs.FirstOrDefault(a => a.Index == index);

            // Single simple / Rhino-geometry output.
            if (IsSimpleType(outputType))
            {
                var a = AttrFor(0);
                plans.Add(new OutputPlan(
                    a?.Name ?? "Output", a?.NickName ?? a?.Name ?? "Out",
                    a?.Description ?? "Method output", outputType, false));
                return plans;
            }

            // List<T> output.
            if (outputType.IsGenericType && outputType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var a = AttrFor(0);
                plans.Add(new OutputPlan(
                    a?.Name ?? "Output", a?.NickName ?? a?.Name ?? "Out",
                    a?.Description ?? "Method output list", outputType, true));
                return plans;
            }

            // Tuple → one output per element.
            if (IsTupleType(outputType))
            {
                var tupleTypes = GetTupleElementTypes(outputType);
                var tupleNames = method?.ReturnParameter
                    ?.GetCustomAttribute<TupleElementNamesAttribute>()
                    ?.TransformNames;

                for (int i = 0; i < tupleTypes.Length; i++)
                {
                    var a = AttrFor(i);
                    var elementName = (tupleNames != null && i < tupleNames.Count) ? tupleNames[i] : null;
                    var name = a?.Name ?? elementName ?? $"Output{i + 1}";
                    var nick = a?.NickName ?? a?.Name ?? elementName ?? $"Out{i + 1}";
                    var desc = a?.Description ?? $"Method output {i + 1}";
                    plans.Add(new OutputPlan(name, nick, desc, tupleTypes[i], false));
                }
                return plans;
            }

            // Complex object → one output per public simple property.
            if (outputType.IsClass && outputType != typeof(string))
            {
                var properties = outputType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && IsSimpleType(p.PropertyType))
                    .Take(10); // cap to avoid UI clutter

                foreach (var prop in properties)
                    plans.Add(new OutputPlan(prop.Name, prop.Name, $"Property: {prop.Name}", prop.PropertyType, false));

                if (plans.Any())
                    return plans;
            }

            // Fallback: single generic output.
            var fb = AttrFor(0);
            plans.Add(new OutputPlan(
                fb?.Name ?? "Output", fb?.NickName ?? fb?.Name ?? "Out",
                fb?.Description ?? "Method output", outputType, false));
            return plans;
        }

        public static bool IsSimpleType(Type type) =>
            type.IsPrimitive ||
            type == typeof(string) ||
            type == typeof(DateTime) ||
            type == typeof(decimal) ||
            type.Namespace == "Rhino.Geometry" || // Rhino geometry types
            type.IsEnum;

        public static bool IsTupleType(Type type) =>
            type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(Tuple<>) ||
             type.GetGenericTypeDefinition() == typeof(Tuple<,>) ||
             type.GetGenericTypeDefinition() == typeof(Tuple<,,>) ||
             type.GetGenericTypeDefinition() == typeof(Tuple<,,,>) ||
             type.GetGenericTypeDefinition() == typeof(Tuple<,,,,>) ||
             type.GetGenericTypeDefinition() == typeof(Tuple<,,,,,>) ||
             type.GetGenericTypeDefinition() == typeof(Tuple<,,,,,,>) ||
             type.GetGenericTypeDefinition() == typeof(Tuple<,,,,,,,>) ||
             (type.FullName?.StartsWith("System.ValueTuple") == true)); // ValueTuple support

        public static Type[] GetTupleElementTypes(Type tupleType) =>
            tupleType.IsGenericType ? tupleType.GetGenericArguments() : Array.Empty<Type>();
    }
}
