// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Betta.Mcp.Inspection;

/// <summary>
/// Reflects over plugin DLLs in <c>%AppData%/Grasshopper/Libraries/Betta/</c>
/// using <see cref="MetadataLoadContext"/> — never <c>Assembly.LoadFrom</c> —
/// so we can read attributes off methods without executing the plugin or
/// requiring Rhino / Grasshopper to be installed.
///
/// Heuristics:
/// <list type="bullet">
///   <item>"Is a Betta plugin" = contains an interface OR class that
///         transitively implements an interface whose full name is
///         <c>Betta.Interfaces.IBettaCollection</c>. Matched by name string
///         because MetadataLoadContext types don't unify across assemblies.</item>
///   <item>"Method count" = number of public instance methods carrying an
///         attribute whose full name is
///         <c>Betta.Attributes.GrasshopperMethodAttribute</c> on a collection
///         type. Mirrors <c>ComponentRegistry.DiscoverFromAssembly</c>:
///         interface-method attributes count once; class-direct methods count
///         once too; interface wins over class on collisions.</item>
/// </list>
/// </summary>
public sealed class PluginInspector : IDisposable
{
    private const string MarkerInterface = "Betta.Interfaces.IBettaCollection";
    private const string MethodAttribute = "Betta.Attributes.GrasshopperMethodAttribute";
    private const string CollectionAttribute = "Betta.Attributes.GrasshopperCollectionAttribute";
    private const string ParameterAttribute = "Betta.Attributes.GrasshopperParameterAttribute";

    private readonly MetadataLoadContext _mlc;
    private readonly string _pluginFolder;

    public PluginInspector(string? pluginFolder = null)
    {
        _pluginFolder = pluginFolder ?? Paths.PluginFolder;

        // The resolver needs to find the framework reference assemblies plus
        // anything sitting alongside the plugin (Betta.Abstractions.dll, the
        // plugin DLL itself, any deps). PathAssemblyResolver tolerates missing
        // assemblies — if a plugin transitively references something we can't
        // find, attribute reads still succeed.
        var probe = new List<string>();
        if (Directory.Exists(_pluginFolder))
            probe.AddRange(Directory.GetFiles(_pluginFolder, "*.dll"));

        // Betta.Abstractions.dll lives in the Grasshopper Libraries root next
        // to Betta.gha (Betta's deploy target). Without it in the probe path
        // MetadataLoadContext cannot resolve the IBettaCollection interface
        // referenced by plugin DLLs, so the "is a Betta plugin?" check would
        // silently return false for every plugin. Look one level up.
        var librariesRoot = Path.GetDirectoryName(_pluginFolder);
        if (!string.IsNullOrEmpty(librariesRoot) && Directory.Exists(librariesRoot))
            probe.AddRange(Directory.GetFiles(librariesRoot, "*.dll"));

        // Same Betta.Mcp folder — useful if we bundle a copy of the abstractions
        // alongside the tool, or to find System.Reflection.MetadataLoadContext.dll
        // for the resolver itself when run as a global tool.
        var ownDir = AppContext.BaseDirectory;
        if (Directory.Exists(ownDir))
            probe.AddRange(Directory.GetFiles(ownDir, "*.dll"));

        // The .NET 8 host's reference assemblies. RuntimeEnvironment.GetRuntimeDirectory
        // returns the path of the running runtime; that's where mscorlib.dll /
        // System.Runtime.dll live.
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        probe.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));

        var resolver = new PathAssemblyResolver(probe.Distinct());
        _mlc = new MetadataLoadContext(resolver);
    }

    public string PluginFolder => _pluginFolder;

    public IReadOnlyList<PluginFileInfo> ListAll()
    {
        if (!Directory.Exists(_pluginFolder))
            return Array.Empty<PluginFileInfo>();

        var results = new List<PluginFileInfo>();
        foreach (var dll in Directory.GetFiles(_pluginFolder, "*.dll"))
        {
            results.Add(InspectFile(dll));
        }
        return results;
    }

    public PluginFileInfo InspectFile(string dllPath)
    {
        var fi = new FileInfo(dllPath);
        var info = new PluginFileInfo
        {
            File = fi.Name,
            FullPath = fi.FullName,
            SizeBytes = fi.Exists ? fi.Length : 0,
            ModifiedUtc = fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue,
        };

        try
        {
            var asm = _mlc.LoadFromAssemblyPath(dllPath);
            var (interfaceTypes, classTypes) = SelectCollectionTypes(asm);

            int methods = 0;
            foreach (var t in interfaceTypes.Concat(classTypes))
                methods += CountMethods(t);

            info.IsBettaPlugin = interfaceTypes.Count + classTypes.Count > 0;
            info.CollectionCount = interfaceTypes.Count + classTypes.Count;
            info.MethodCount = methods;
        }
        catch (Exception ex)
        {
            info.Error = ex.Message;
        }

        return info;
    }

    /// <summary>
    /// Mirrors <c>ComponentRegistry.DiscoverFromAssembly</c>: interfaces that
    /// inherit IBettaCollection (excluding the marker itself) and carry
    /// [GrasshopperMethod] win first; classes that implement the marker, carry
    /// [GrasshopperMethod] on their own methods, and don't already implement
    /// one of the selected interfaces are the class-direct authoring case.
    /// </summary>
    private static (List<Type> Interfaces, List<Type> Classes) SelectCollectionTypes(Assembly asm)
    {
        var types = SafeGetTypes(asm).ToList();

        var interfaceTypes = types
            .Where(t => t.IsInterface
                        && ImplementsMarker(t)
                        && t.FullName != MarkerInterface
                        && HasGrasshopperMethods(t))
            .ToList();

        var classTypes = types
            .Where(t => t.IsClass && !t.IsAbstract
                        && ImplementsMarker(t)
                        && HasGrasshopperMethods(t)
                        && !t.GetInterfaces().Any(interfaceTypes.Contains))
            .ToList();

        return (interfaceTypes, classTypes);
    }

    private static bool ImplementsMarker(Type t)
    {
        if (t.FullName == MarkerInterface) return true;
        foreach (var i in t.GetInterfaces())
            if (i.FullName == MarkerInterface) return true;
        return false;
    }

    private static bool HasGrasshopperMethods(Type t)
    {
        foreach (var m in t.GetMethods())
            if (TryGetMethodAttribute(m, out _)) return true;
        return false;
    }

    /// <summary>Resolves a component descriptor by display name. First match wins.</summary>
    public DescribedComponent? Describe(string componentName)
    {
        if (!Directory.Exists(_pluginFolder)) return null;

        foreach (var dll in Directory.GetFiles(_pluginFolder, "*.dll"))
        {
            Assembly asm;
            try { asm = _mlc.LoadFromAssemblyPath(dll); }
            catch { continue; }

            var (interfaceTypes, classTypes) = SelectCollectionTypes(asm);
            foreach (var t in interfaceTypes.Concat(classTypes))
            {
                var (colCategory, colSubCategory) = ReadCollectionCategory(t);
                foreach (var m in t.GetMethods())
                {
                    if (!TryGetMethodAttribute(m, out var attrData)) continue;

                    var named = AttributeReader.NamedArguments(attrData);
                    var ctor = AttributeReader.CtorArguments(attrData);
                    var displayName = NamedString(named, "Name") ?? PositionalString(ctor, 0) ?? m.Name;
                    if (!string.Equals(displayName, componentName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return new DescribedComponent
                    {
                        SourceFile = Path.GetFileName(dll),
                        ServiceType = t.FullName ?? t.Name,
                        Method = m.Name,
                        Name = displayName,
                        NickName = NamedString(named, "NickName") ?? displayName,
                        Description = NamedString(named, "Description") ?? PositionalString(ctor, 1) ?? string.Empty,
                        Category = NamedString(named, "Category") ?? colCategory ?? "Betta",
                        SubCategory = NamedString(named, "SubCategory") ?? colSubCategory ?? "General",
                        IconResource = NamedString(named, "IconResource"),
                        Enabled = NamedBool(named, "Enabled") ?? true,
                        Guid = ComputeDeterministicGuid(t, m).ToString(),
                        ReturnType = m.ReturnType?.FullName ?? m.ReturnType?.Name ?? "void",
                        Parameters = m.GetParameters().Select(p => new DescribedParameter
                        {
                            Name = NamedString(AttributeReader.NamedArgumentsOfAttr(p, ParameterAttribute), "Name") ?? p.Name ?? string.Empty,
                            NickName = NamedString(AttributeReader.NamedArgumentsOfAttr(p, ParameterAttribute), "NickName") ?? p.Name ?? string.Empty,
                            Description = NamedString(AttributeReader.NamedArgumentsOfAttr(p, ParameterAttribute), "Description") ?? string.Empty,
                            ClrName = p.Name ?? string.Empty,
                            Type = p.ParameterType.FullName ?? p.ParameterType.Name,
                            Optional = NamedBool(AttributeReader.NamedArgumentsOfAttr(p, ParameterAttribute), "Optional") ?? false,
                        }).ToList(),
                    };
                }
            }
        }
        return null;
    }

    // --- helpers ---

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static bool TryGetMethodAttribute(MethodInfo m, out CustomAttributeData attr)
    {
        var found = m.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == MethodAttribute);
        attr = found!;
        return found is not null;
    }

    private static (string? Category, string? SubCategory) ReadCollectionCategory(Type t)
    {
        var attr = t.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == CollectionAttribute);
        if (attr is null) return (null, null);
        var named = AttributeReader.NamedArguments(attr);
        var ctor = AttributeReader.CtorArguments(attr);
        var cat = NamedString(named, "Category") ?? PositionalString(ctor, 0);
        var sub = NamedString(named, "SubCategory") ?? PositionalString(ctor, 1);
        return (cat, sub);
    }

    private static int CountMethods(Type t)
    {
        int n = 0;
        foreach (var m in t.GetMethods())
            if (TryGetMethodAttribute(m, out _))
                n++;
        return n;
    }

    /// <summary>
    /// Mirrors <c>ComponentDescriptor.GenerateGuidFromMethod</c> verbatim so
    /// the GUID the MCP server reports matches what Betta itself publishes.
    /// </summary>
    private static Guid ComputeDeterministicGuid(Type serviceType, MethodInfo m)
    {
        var paramSig = string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name));
        var input = $"{serviceType.FullName}|{m.Name}|{paramSig}|{m.ReturnType.FullName}";
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(bytes);
    }

    private static string? NamedString(IReadOnlyDictionary<string, object?> args, string key)
        => args.TryGetValue(key, out var v) ? v as string : null;

    private static bool? NamedBool(IReadOnlyDictionary<string, object?> args, string key)
        => args.TryGetValue(key, out var v) && v is bool b ? b : (bool?)null;

    private static string? PositionalString(IReadOnlyList<object?> args, int index)
        => args.Count > index ? args[index] as string : null;

    public void Dispose() => _mlc.Dispose();
}

public sealed class PluginFileInfo
{
    public string File { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public bool IsBettaPlugin { get; set; }
    public int CollectionCount { get; set; }
    public int MethodCount { get; set; }
    public string? Error { get; set; }
}

public sealed class DescribedComponent
{
    public string SourceFile { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public string? IconResource { get; set; }
    public bool Enabled { get; set; }
    public string Guid { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public List<DescribedParameter> Parameters { get; set; } = new();
}

public sealed class DescribedParameter
{
    public string Name { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ClrName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Optional { get; set; }
}

internal static class AttributeReader
{
    public static IReadOnlyDictionary<string, object?> NamedArguments(CustomAttributeData attr)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var na in attr.NamedArguments)
            dict[na.MemberName] = na.TypedValue.Value;
        return dict;
    }

    public static IReadOnlyList<object?> CtorArguments(CustomAttributeData attr)
        => attr.ConstructorArguments.Select(c => c.Value).ToList();

    public static IReadOnlyDictionary<string, object?> NamedArgumentsOfAttr(ParameterInfo p, string fullName)
    {
        var attr = p.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.FullName == fullName);
        if (attr is null) return new Dictionary<string, object?>();
        var named = NamedArguments(attr);
        var ctor = CtorArguments(attr);
        // The Name attribute also has a positional ctor; surface that as the "Name" key
        // so downstream lookups uniformly use named keys.
        var merged = new Dictionary<string, object?>(named, StringComparer.Ordinal);
        if (!merged.ContainsKey("Name") && ctor.Count > 0 && ctor[0] is string s) merged["Name"] = s;
        if (!merged.ContainsKey("Description") && ctor.Count > 1 && ctor[1] is string d) merged["Description"] = d;
        return merged;
    }
}
