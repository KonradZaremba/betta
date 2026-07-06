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

            // Tree output: GH_Structure<TGoo>. Surface as a single tree-access
            // output; ParamVector.GetParamAccessType maps the type to
            // GH_ParamAccess.tree automatically.
            if (IsGhStructureType(outputType))
            {
                var a = AttrFor(0);
                plans.Add(new OutputPlan(
                    a?.Name ?? "Output", a?.NickName ?? a?.Name ?? "Out",
                    a?.Description ?? "Tree output", outputType, false));
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

            // Tuple → one output per element. Includes high-arity ValueTuples
            // — those with 8+ elements — via GetTupleElementTypes' TRest walk,
            // so a method returning (int a, int b, ..., int i) surfaces nine
            // outputs, not eight.
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

            // Opaque domain object: pass through as a single typed output
            // instead of exploding into properties. The runtime auto-generates
            // a GH_BettaGoo<T> + Param_BettaGoo<T> for the type.
            bool opaque = OpaqueDetection.IsTypeOpaque(outputType) ||
                          OpaqueDetection.IsMethodReturnOpaque(method);

            // Raw System.Drawing.Bitmap / Image: these are container types
            // with a large property surface (Width, Height, PixelFormat,
            // Palette, Flags, HorizontalResolution…) that make the "class
            // explosion" branch below emit nonsense. Ship them as one output.
            // For richer bake / preview / PNG ergonomics, plugin authors
            // should return Betta.Preview.BettaImage instead — that's opaque
            // and gets a typed goo pair automatically.
            if (outputType.FullName == "System.Drawing.Bitmap" ||
                outputType.FullName == "System.Drawing.Image")
            {
                var a = AttrFor(0);
                plans.Add(new OutputPlan(
                    a?.Name ?? "Image", a?.NickName ?? a?.Name ?? "Img",
                    a?.Description ?? "Bitmap output", outputType, false));
                return plans;
            }

            // Complex object → one output per public property (unless opaque).
            // Property types include simple types AND opaque types / List<T>
            // of opaque types, so a class returning a mix of primitives and
            // domain objects surfaces every field as a typed wire. Without
            // that, classes returning opaque-typed properties silently drop
            // them (ML Plans' History / Gallery / LoadWithMetadata hit this).
            if (!opaque && outputType.IsClass && outputType != typeof(string))
            {
                var properties = outputType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && IsExplodableProperty(p.PropertyType))
                    .Take(10); // cap to avoid UI clutter

                foreach (var prop in properties)
                {
                    var propType = prop.PropertyType;
                    bool isList = propType.IsGenericType &&
                                  propType.GetGenericTypeDefinition() == typeof(List<>);
                    plans.Add(new OutputPlan(prop.Name, prop.Name, $"Property: {prop.Name}", propType, isList));
                }

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

        /// <summary>
        /// Return the flattened element types of a tuple. For arities ≤ 7 this
        /// is just <c>GetGenericArguments()</c>; for arity 8+, the eighth
        /// generic argument (<c>TRest</c>) is another tuple whose own elements
        /// count. Walks TRest recursively so a <c>ValueTuple&lt;T1..T7,ValueTuple&lt;T8,T9&gt;&gt;</c>
        /// yields all nine element types in declaration order.
        /// </summary>
        public static Type[] GetTupleElementTypes(Type tupleType)
        {
            if (tupleType == null || !tupleType.IsGenericType)
                return Array.Empty<Type>();

            var acc = new List<Type>();
            Walk(tupleType);
            return acc.ToArray();

            void Walk(Type t)
            {
                var args = t.GetGenericArguments();
                if (args.Length < 8)
                {
                    acc.AddRange(args);
                    return;
                }
                // Arity-8 slot: 7 elements + 1 TRest.
                for (int i = 0; i < 7; i++) acc.Add(args[i]);
                var rest = args[7];
                if (IsTupleType(rest)) Walk(rest);
                else acc.Add(rest); // shouldn't happen for well-formed tuples
            }
        }

        /// <summary>
        /// Property types eligible for the class-return-explosion output list:
        /// simple types (primitives, string, Rhino geometry, enums), single
        /// opaque domain objects (marked with [GrasshopperOpaque] or IBettaValue),
        /// and <c>List&lt;T&gt;</c> of either. The list-of-simple case flows
        /// through the normal list-access output pipeline; opaque + list-of-opaque
        /// flow through the Param_BettaGoo&lt;T&gt; pair the runtime auto-generates.
        /// </summary>
        internal static bool IsExplodableProperty(Type t)
        {
            if (t == null) return false;
            if (IsSimpleType(t)) return true;
            if (OpaqueDetection.IsTypeOpaque(t)) return true;

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var element = t.GetGenericArguments()[0];
                return IsSimpleType(element) || OpaqueDetection.IsTypeOpaque(element);
            }
            return false;
        }

        /// <summary>
        /// True if <paramref name="t"/> is the closed generic
        /// <c>GH_Structure&lt;TGoo&gt;</c>. Detected by FullName string so this
        /// GH-free file stays free of a Grasshopper.Kernel.Data reference.
        /// </summary>
        public static bool IsGhStructureType(Type t) =>
            t != null &&
            t.IsGenericType &&
            t.GetGenericTypeDefinition().FullName == "Grasshopper.Kernel.Data.GH_Structure`1";
    }
}
