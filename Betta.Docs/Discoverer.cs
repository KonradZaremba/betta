// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using System.Reflection;
using System.Xml;

namespace Betta.Docs;

/// <summary>
/// Reads a plugin DLL with <see cref="MetadataLoadContext"/> and returns the
/// list of components it would publish to Grasshopper. Mirrors the discovery
/// rules in Betta's <c>ComponentRegistry</c> — IBettaCollection-bearing
/// interfaces (contract-first) plus IBettaCollection-bearing concrete classes
/// (terse style), with the same "interface wins" dedup.
/// </summary>
internal static class Discoverer
{
    private const string IBettaCollectionFullName = "Betta.Interfaces.IBettaCollection";
    private const string GhMethodAttrFullName     = "Betta.Attributes.GrasshopperMethodAttribute";
    private const string GhParamAttrFullName      = "Betta.Attributes.GrasshopperParameterAttribute";
    private const string GhOutputAttrFullName     = "Betta.Attributes.GrasshopperOutputAttribute";
    private const string GhCollectionAttrFullName = "Betta.Attributes.GrasshopperCollectionAttribute";
    private const string GhOpaqueAttrFullName     = "Betta.Attributes.GrasshopperOpaqueAttribute";
    private const string IBettaValueFullName      = "Betta.Interfaces.IBettaValue";

    public static IReadOnlyList<DiscoveredComponent> Discover(string dllPath)
    {
        var resolverPaths = BuildResolverPaths(dllPath);
        // PathAssemblyResolver throws on miss; ToleratingResolver wraps it so
        // unresolved references (e.g. RhinoCommon when Rhino isn't installed
        // beside the tool) return null and the load continues.
        var resolver = new ToleratingResolver(new PathAssemblyResolver(resolverPaths));
        using var mlc = new MetadataLoadContext(resolver, coreAssemblyName: "System.Private.CoreLib");

        var asm = mlc.LoadFromAssemblyPath(dllPath);
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }

        // Interfaces first (contract-first style)
        var interfaceTypes = types
            .Where(t => SafeIs(t, t2 => t2.IsInterface
                                        && IsBettaCollection(t2)
                                        && t2.FullName != IBettaCollectionFullName
                                        && HasGrasshopperMethods(t2)))
            .ToList();

        // Then concrete classes — but skip classes that implement a discovered
        // interface, so the interface wins (same rule as ComponentRegistry).
        var classTypes = types
            .Where(t => SafeIs(t, t2 => t2.IsClass && !t2.IsAbstract
                                        && IsBettaCollection(t2)
                                        && HasGrasshopperMethods(t2)
                                        && !t2.GetInterfaces().Any(i => interfaceTypes.Any(it => it.FullName == i.FullName))))
            .ToList();

        var xmlDocs = XmlDocReader.LoadAlongside(dllPath);

        var components = new List<DiscoveredComponent>();
        foreach (var serviceType in interfaceTypes.Concat(classTypes))
        {
            var collectionAttr = GetCustomAttribute(serviceType.GetCustomAttributesData(), GhCollectionAttrFullName);
            var defaultCategory = (string?)NamedArg(collectionAttr, "Category") ?? (string?)CtorArg(collectionAttr, 0)
                                  ?? TrimInterfacePrefix(serviceType.Name);
            var defaultSubCat = (string?)NamedArg(collectionAttr, "SubCategory") ?? (string?)CtorArg(collectionAttr, 1)
                                ?? "General";

            MethodInfo[] methods;
            try { methods = serviceType.GetMethods(); }
            catch (FileNotFoundException) { continue; }

            foreach (var method in methods)
            {
                try
                {
                    var attr = GetCustomAttribute(method.GetCustomAttributesData(), GhMethodAttrFullName);
                    if (attr is null) continue;

                    var enabled = NamedArg(attr, "Enabled") as bool? ?? true;
                    if (!enabled) continue;

                    // Name: positional ctor arg 0 wins over named.
                    var name = (string?)CtorArg(attr, 0) ?? (string?)NamedArg(attr, "Name") ?? method.Name;
                    var nickName = (string?)NamedArg(attr, "NickName") ?? name;
                    var description = (string?)CtorArg(attr, 1) ?? (string?)NamedArg(attr, "Description")
                                      ?? xmlDocs.GetMethodSummary(method) ?? name;
                    var category = (string?)NamedArg(attr, "Category") ?? defaultCategory;
                    var subCategory = (string?)NamedArg(attr, "SubCategory") ?? defaultSubCat;
                    var iconResource = (string?)NamedArg(attr, "IconResource");

                    var inputs = method.GetParameters()
                        .Select(p => BuildInput(p, xmlDocs))
                        .ToImmutableList();

                    var outputs = OutputPlanner.PlanOutputs(method).ToImmutableList();

                    components.Add(new DiscoveredComponent(
                        sourceDll: dllPath,
                        serviceType: serviceType.FullName ?? serviceType.Name,
                        methodName: method.Name,
                        name: name!,
                        nickName: nickName!,
                        description: description!,
                        category: category!,
                        subCategory: subCategory!,
                        iconResource: iconResource,
                        inputs: inputs,
                        outputs: outputs));
                }
                catch (FileNotFoundException ex)
                {
                    // A reference assembly (RhinoCommon, Grasshopper, ...) is
                    // missing. Skip this method; report once per DLL so the
                    // caller can install the missing dep if desired.
                    Console.Error.WriteLine($"    skip {serviceType.Name}.{method.Name}: missing reference {ex.FileName}");
                }
            }
        }

        return components;
    }

    private static bool SafeIs(Type t, Func<Type, bool> pred)
    {
        try { return pred(t); }
        catch (FileNotFoundException) { return false; }
        catch (TypeLoadException) { return false; }
    }

    private static InputInfo BuildInput(ParameterInfo p, XmlDocReader xmlDocs)
    {
        var attr = GetCustomAttribute(p.GetCustomAttributesData(), GhParamAttrFullName);
        var name = (string?)CtorArg(attr, 0) ?? (string?)NamedArg(attr, "Name") ?? p.Name ?? "Input";
        var nick = (string?)NamedArg(attr, "NickName") ?? name;
        var description = (string?)CtorArg(attr, 1) ?? (string?)NamedArg(attr, "Description")
                          ?? xmlDocs.GetParamSummary(p) ?? name;
        var optional = NamedArg(attr, "Optional") as bool? ?? false;
        var defaultVal = NamedArg(attr, "DefaultValue");
        var hasDefault = defaultVal is not null;

        // Treat List<T> as list access, otherwise item.
        var paramType = p.ParameterType;
        var isList = paramType.IsGenericType
                     && paramType.GetGenericTypeDefinition().Name.StartsWith("List`1");

        var elementType = isList ? paramType.GetGenericArguments()[0] : paramType;

        return new InputInfo(
            ClrName: p.Name ?? "?",
            DisplayName: name!,
            NickName: nick!,
            Description: description!,
            TypeDisplay: TypeDisplay(elementType),
            IsList: isList,
            Optional: optional,
            HasDefault: hasDefault,
            DefaultValue: defaultVal);
    }

    private static bool IsBettaCollection(Type t)
    {
        // Walk the interface chain by FullName (Type identity won't match
        // across MetadataLoadContext + runtime).
        foreach (var iface in t.GetInterfaces())
            if (iface.FullName == IBettaCollectionFullName) return true;
        return false;
    }

    private static bool HasGrasshopperMethods(Type type) =>
        type.GetMethods().Any(m => GetCustomAttribute(m.GetCustomAttributesData(), GhMethodAttrFullName) is not null);

    private static CustomAttributeData? GetCustomAttribute(IList<CustomAttributeData> data, string fullName) =>
        data.FirstOrDefault(a => a.AttributeType.FullName == fullName);

    private static object? CtorArg(CustomAttributeData? attr, int index)
    {
        if (attr is null || index >= attr.ConstructorArguments.Count) return null;
        return attr.ConstructorArguments[index].Value;
    }

    private static object? NamedArg(CustomAttributeData? attr, string name)
    {
        if (attr is null) return null;
        foreach (var na in attr.NamedArguments)
            if (na.MemberName == name) return na.TypedValue.Value;
        return null;
    }

    private static string TrimInterfacePrefix(string n) =>
        (!string.IsNullOrEmpty(n) && n.Length > 1 && n[0] == 'I' && char.IsUpper(n[1])) ? n.Substring(1) : n;

    internal static string TypeDisplay(Type t)
    {
        // Friendly C#-style name without fully qualifying (Rhino.Geometry.Point3d is fine,
        // but List<Point3d> reads better than List`1[Point3d]).
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var baseName = def.Name;
            var tick = baseName.IndexOf('`');
            if (tick >= 0) baseName = baseName.Substring(0, tick);
            var args = string.Join(", ", t.GetGenericArguments().Select(TypeDisplay));
            return baseName + "<" + args + ">";
        }
        if (t.IsArray) return TypeDisplay(t.GetElementType()!) + "[]";
        // Friendly short names for primitives
        return t.FullName switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Boolean" => "bool",
            "System.Decimal" => "decimal",
            "System.Byte" => "byte",
            "System.DateTime" => "DateTime",
            "System.Object" => "object",
            _ => t.Name
        };
    }

    /// <summary>
    /// Build the set of paths MetadataLoadContext needs to resolve references.
    /// Includes the plugin folder, the tool's own runtime (for Betta.Abstractions
    /// if it travels with the tool), and the .NET 8 reference assemblies that
    /// ship with the runtime.
    /// </summary>
    private static IEnumerable<string> BuildResolverPaths(string dllPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) The plugin DLL itself + its sibling DLLs
        var pluginDir = Path.GetDirectoryName(Path.GetFullPath(dllPath))!;
        foreach (var sibling in Directory.EnumerateFiles(pluginDir, "*.dll"))
            seen.Add(sibling);

        // 2) The tool's runtime directory — Betta.Abstractions ships with the tool
        var toolDir = Path.GetDirectoryName(typeof(Discoverer).Assembly.Location);
        if (!string.IsNullOrEmpty(toolDir) && Directory.Exists(toolDir))
        {
            foreach (var dll in Directory.EnumerateFiles(toolDir, "*.dll"))
                seen.Add(dll);
        }

        // 3) The current .NET runtime directory — System.Private.CoreLib lives here
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir))
        {
            foreach (var dll in Directory.EnumerateFiles(runtimeDir, "*.dll"))
                seen.Add(dll);
        }

        return seen;
    }
}

/// <summary>
/// Wraps a PathAssemblyResolver so that unresolved references return null
/// instead of throwing. MetadataLoadContext lets a resolver return null;
/// downstream code (e.g. <c>parameter.ParameterType</c>) will then throw
/// FileNotFoundException only when something actually touches the missing
/// assembly. Lets us read attribute-only metadata from a plugin that also
/// happens to import RhinoCommon.
/// </summary>
internal sealed class ToleratingResolver : MetadataAssemblyResolver
{
    private readonly PathAssemblyResolver _inner;

    public ToleratingResolver(PathAssemblyResolver inner) => _inner = inner;

    public override Assembly? Resolve(MetadataLoadContext context, AssemblyName name)
    {
        try { return _inner.Resolve(context, name); }
        catch (FileNotFoundException) { return null; }
        catch (FileLoadException) { return null; }
    }
}

internal sealed record DiscoveredComponent(
    string sourceDll,
    string serviceType,
    string methodName,
    string name,
    string nickName,
    string description,
    string category,
    string subCategory,
    string? iconResource,
    ImmutableList<InputInfo> inputs,
    ImmutableList<OutputPlanner.OutputInfo> outputs);

internal sealed record InputInfo(
    string ClrName,
    string DisplayName,
    string NickName,
    string Description,
    string TypeDisplay,
    bool IsList,
    bool Optional,
    bool HasDefault,
    object? DefaultValue);
