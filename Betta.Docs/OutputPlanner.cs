// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

namespace Betta.Docs;

/// <summary>
/// Standalone re-implementation of Betta's OutputPlanner — kept in-tool so
/// betta-docs has zero dependency on the Betta runtime DLL. Mirrors the rules
/// documented in Betta/OutputPlanner.cs:
///   1. [GrasshopperOutput] on method or [return:] wins, indexed by Index.
///   2. ValueTuple element names (TupleElementNamesAttribute).
///   3. Fallbacks: "Output" for single, "Output1..N" for tuples,
///      property names for class returns.
/// </summary>
internal static class OutputPlanner
{
    private const string GhOutputAttrFullName = "Betta.Attributes.GrasshopperOutputAttribute";
    private const string GhOpaqueAttrFullName = "Betta.Attributes.GrasshopperOpaqueAttribute";
    private const string IBettaValueFullName  = "Betta.Interfaces.IBettaValue";

    public static List<OutputInfo> PlanOutputs(MethodInfo method)
    {
        var plans = new List<OutputInfo>();
        var outputType = method.ReturnType;
        if (outputType is null || outputType.FullName == "System.Void")
            return plans;

        // Collect explicit [GrasshopperOutput] attrs (method + return parameter).
        var attrs = new List<CustomAttributeData>();
        foreach (var a in method.GetCustomAttributesData())
            if (a.AttributeType.FullName == GhOutputAttrFullName) attrs.Add(a);
        if (method.ReturnParameter is not null)
            foreach (var a in method.ReturnParameter.GetCustomAttributesData())
                if (a.AttributeType.FullName == GhOutputAttrFullName) attrs.Add(a);

        CustomAttributeData? AttrFor(int index) => attrs.FirstOrDefault(a => GetNamedOrCtorIndex(a) == index);

        // Simple
        if (IsSimpleType(outputType))
        {
            var a = AttrFor(0);
            plans.Add(new OutputInfo(
                NameOrDefault(a, "Output"),
                NickOrDefault(a, "Out"),
                DescOrDefault(a, "Method output"),
                Discoverer.TypeDisplay(outputType),
                IsList: false));
            return plans;
        }

        if (IsGhStructureType(outputType))
        {
            var a = AttrFor(0);
            plans.Add(new OutputInfo(
                NameOrDefault(a, "Output"),
                NickOrDefault(a, "Out"),
                DescOrDefault(a, "Tree output"),
                Discoverer.TypeDisplay(outputType),
                IsList: false));
            return plans;
        }

        if (outputType.IsGenericType && outputType.GetGenericTypeDefinition().Name.StartsWith("List`1"))
        {
            var a = AttrFor(0);
            plans.Add(new OutputInfo(
                NameOrDefault(a, "Output"),
                NickOrDefault(a, "Out"),
                DescOrDefault(a, "Method output list"),
                Discoverer.TypeDisplay(outputType.GetGenericArguments()[0]),
                IsList: true));
            return plans;
        }

        if (IsTupleType(outputType))
        {
            var tupleTypes = outputType.GetGenericArguments();
            var tupleNames = ReadTupleElementNames(method);

            for (int i = 0; i < tupleTypes.Length; i++)
            {
                var a = AttrFor(i);
                var elementName = (tupleNames is not null && i < tupleNames.Count) ? tupleNames[i] : null;
                var name = (string?)CtorOrNamed(a, "Name", 0) ?? elementName ?? $"Output{i + 1}";
                var nick = (string?)NamedOnly(a, "NickName") ?? (string?)CtorOrNamed(a, "Name", 0) ?? elementName ?? $"Out{i + 1}";
                var desc = (string?)CtorOrNamed(a, "Description", 1) ?? $"Method output {i + 1}";
                plans.Add(new OutputInfo(name, nick, desc, Discoverer.TypeDisplay(tupleTypes[i]), IsList: false));
            }
            return plans;
        }

        // Opaque check — type-level or method-return-level
        bool opaque = IsTypeOpaque(outputType) || IsMethodReturnOpaque(method);

        // Class → properties (unless opaque)
        if (!opaque && outputType.IsClass && outputType.FullName != "System.String")
        {
            var props = outputType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && IsSimpleType(p.PropertyType))
                .Take(10)
                .ToList();
            if (props.Count > 0)
            {
                foreach (var prop in props)
                    plans.Add(new OutputInfo(
                        prop.Name, prop.Name, $"Property: {prop.Name}",
                        Discoverer.TypeDisplay(prop.PropertyType), IsList: false));
                return plans;
            }
        }

        // Fallback
        {
            var a = AttrFor(0);
            plans.Add(new OutputInfo(
                NameOrDefault(a, "Output"),
                NickOrDefault(a, "Out"),
                DescOrDefault(a, "Method output"),
                Discoverer.TypeDisplay(outputType),
                IsList: false));
            return plans;
        }
    }

    private static int GetNamedOrCtorIndex(CustomAttributeData a)
    {
        // The 4-arg ctor is (name, nickName, description, index=0). Index can
        // also come via named arg. Default to 0.
        foreach (var na in a.NamedArguments)
            if (na.MemberName == "Index" && na.TypedValue.Value is int i) return i;
        if (a.ConstructorArguments.Count >= 4 && a.ConstructorArguments[3].Value is int ii) return ii;
        return 0;
    }

    private static string NameOrDefault(CustomAttributeData? a, string fallback) =>
        (string?)CtorOrNamed(a, "Name", 0) ?? fallback;

    private static string NickOrDefault(CustomAttributeData? a, string fallback)
    {
        var nick = NamedOnly(a, "NickName") as string;
        if (!string.IsNullOrEmpty(nick)) return nick!;
        var name = CtorOrNamed(a, "Name", 0) as string;
        if (!string.IsNullOrEmpty(name)) return name!;
        return fallback;
    }

    private static string DescOrDefault(CustomAttributeData? a, string fallback) =>
        (string?)CtorOrNamed(a, "Description", 1) ?? fallback;

    private static object? CtorOrNamed(CustomAttributeData? a, string name, int ctorIndex)
    {
        if (a is null) return null;
        if (a.ConstructorArguments.Count > ctorIndex)
        {
            var v = a.ConstructorArguments[ctorIndex].Value;
            if (v is not null) return v;
        }
        foreach (var na in a.NamedArguments)
            if (na.MemberName == name) return na.TypedValue.Value;
        return null;
    }

    private static object? NamedOnly(CustomAttributeData? a, string name)
    {
        if (a is null) return null;
        foreach (var na in a.NamedArguments)
            if (na.MemberName == name) return na.TypedValue.Value;
        return null;
    }

    private static IList<string?>? ReadTupleElementNames(MethodInfo method)
    {
        if (method.ReturnParameter is null) return null;
        foreach (var a in method.ReturnParameter.GetCustomAttributesData())
        {
            if (a.AttributeType.FullName == "System.Runtime.CompilerServices.TupleElementNamesAttribute"
                && a.ConstructorArguments.Count == 1
                && a.ConstructorArguments[0].Value is IReadOnlyCollection<CustomAttributeTypedArgument> arr)
            {
                return arr.Select(x => x.Value as string).ToList();
            }
        }
        return null;
    }

    private static bool IsTupleType(Type t)
    {
        if (!t.IsGenericType) return false;
        var def = t.GetGenericTypeDefinition().FullName ?? "";
        return def.StartsWith("System.Tuple") || def.StartsWith("System.ValueTuple");
    }

    private static bool IsGhStructureType(Type t) =>
        t.IsGenericType && (t.GetGenericTypeDefinition().FullName == "Grasshopper.Kernel.Data.GH_Structure`1");

    public static bool IsSimpleType(Type t)
    {
        if (t.IsPrimitive) return true;
        if (t.IsEnum) return true;
        if (t.Namespace == "Rhino.Geometry") return true;
        return t.FullName switch
        {
            "System.String" => true,
            "System.DateTime" => true,
            "System.Decimal" => true,
            _ => false
        };
    }

    private static bool IsTypeOpaque(Type t)
    {
        if (t is null) return false;
        if (t.GetCustomAttributesData().Any(a => a.AttributeType.FullName == GhOpaqueAttrFullName)) return true;
        foreach (var iface in t.GetInterfaces())
        {
            if (iface.FullName == IBettaValueFullName) return true;
            if (iface.GetCustomAttributesData().Any(a => a.AttributeType.FullName == GhOpaqueAttrFullName)) return true;
        }
        return false;
    }

    private static bool IsMethodReturnOpaque(MethodInfo m)
    {
        if (m.GetCustomAttributesData().Any(a => a.AttributeType.FullName == GhOpaqueAttrFullName)) return true;
        if (m.ReturnParameter is not null
            && m.ReturnParameter.GetCustomAttributesData().Any(a => a.AttributeType.FullName == GhOpaqueAttrFullName))
            return true;
        return false;
    }

    internal sealed record OutputInfo(
        string Name,
        string NickName,
        string Description,
        string TypeDisplay,
        bool IsList);
}
