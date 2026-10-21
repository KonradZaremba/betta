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
    private readonly string _logFile;

    public PluginInspector(string? pluginFolder = null, string? logFile = null)
    {
        _pluginFolder = pluginFolder ?? Paths.PluginFolder;
        _logFile = logFile ?? Paths.LogFile;

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

        // RhinoCommon and Grasshopper from the Rhino install. Plugins reference them with
        // ExcludeAssets="runtime", so they are never next to the plugin, yet a signature like
        // List<Curve> can't be named without them. The installed versions are also the ones
        // Rhino hashes into derived GUIDs.
        probe.AddRange(Paths.RhinoAssemblies.Where(File.Exists));

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

        foreach (var dll in Directory.GetFiles(_pluginFolder, "*.dll").Where(IsPluginCandidate))
        {
            Assembly asm;
            (List<Type> Interfaces, List<Type> Classes) collections;
            try
            {
                asm = _mlc.LoadFromAssemblyPath(dll);
                collections = SelectCollectionTypes(asm);
            }
            catch (Exception ex) when (ex is FileLoadException or FileNotFoundException or BadImageFormatException or TypeLoadException)
            {
                continue;
            }

            var (interfaceTypes, classTypes) = collections;
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

                    var d = new DescribedComponent
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
                    };
                    d.LoggedGuids = ReadLoggedGuids(d.Category, d.SubCategory, d.Name);
                    // ComponentRegistry uses [GrasshopperMethod(Guid = ...)] when set, so a pinned GUID wins here too.
                    d.GuidPinned = System.Guid.TryParse(NamedString(named, "Guid"), out var pinned);

                    try
                    {
                        d.Guid = d.GuidPinned ? pinned.ToString() : ComputeDeterministicGuid(t, m).ToString();
                        d.ReturnType = m.ReturnType?.FullName ?? m.ReturnType?.Name ?? "void";
                        d.Parameters = m.GetParameters().Select(p => new DescribedParameter
                        {
                            Name = NamedString(AttributeReader.NamedArgumentsOfAttr(p, ParameterAttribute), "Name") ?? p.Name ?? string.Empty,
                            NickName = NamedString(AttributeReader.NamedArgumentsOfAttr(p, ParameterAttribute), "NickName") ?? p.Name ?? string.Empty,
                            Description = NamedString(AttributeReader.NamedArgumentsOfAttr(p, ParameterAttribute), "Description") ?? string.Empty,
                            ClrName = p.Name ?? string.Empty,
                            Type = p.ParameterType.FullName ?? p.ParameterType.Name,
                            Optional = NamedBool(AttributeReader.NamedArgumentsOfAttr(p, ParameterAttribute), "Optional") ?? false,
                        }).ToList();
                        if (!d.GuidPinned)
                            d.VersionedAssemblies = VersionedAssembliesOf(m);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException)
                    {
                        // A type in the signature lives in an assembly this server can't find.
                        d.Notes.Add($"Couldn't resolve a type in the signature ({ex.Message}), so the GUID can't be computed here. " +
                                    "loggedGuids, or the Betta Inspector component in Rhino, shows the published value.");
                        return d;
                    }

                    d.GuidVersionDependent = d.VersionedAssemblies.Count > 0;
                    d.Notes.Add(GuidNote(d));
                    return d;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// True when the DLL references Betta.Abstractions, read from its metadata without loading it.
    /// A plugin must (IBettaCollection lives there); its bundled dependencies don't, and loading
    /// them explicitly collides with the copies the resolver already picked.
    /// </summary>
    public static bool IsPluginCandidate(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            if (!pe.HasMetadata) return false;
            var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            foreach (var handle in md.AssemblyReferences)
                if (md.GetString(md.GetAssemblyReference(handle).Name) == "Betta.Abstractions") return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // --- helpers ---

    private static string GuidNote(DescribedComponent d)
    {
        var pin = $"[GrasshopperMethod(..., Guid = \"{d.Guid}\")]";
        if (d.GuidPinned)
            return "Pinned with [GrasshopperMethod(Guid = ...)]: it never changes, whatever happens to the signature or to assembly versions.";
        if (d.GuidVersionDependent)
            return "Derived from a signature with generic types, whose names include these assembly versions: " +
                   string.Join(", ", d.VersionedAssemblies) + ". The GUID changes whenever one of them does (a new build of the plugin, " +
                   $"a Rhino update, a .NET switch), and saved .gh files then lose the component. Pin it now with {pin}. " +
                   "If saved files were made with an earlier build, pin the GUID they use instead: loggedGuids lists what Betta has " +
                   "published, oldest first. This value is computed with this server's .NET 8 and the installed RhinoCommon; " +
                   "if it differs from the last loggedGuids entry, trust the log.";
        return "Derived from the signature: renaming the method or its type, or renaming, reordering or retyping a parameter " +
               $"changes it and breaks saved .gh files. Pin it with {pin} before such a change.";
    }

    /// <summary>
    /// Assemblies whose versions end up in the GUID hash: <c>Type.FullName</c> of a generic type
    /// assembly-qualifies its type arguments (<c>List`1[[Rhino.Geometry.Curve, RhinoCommon, Version=8.26…]]</c>).
    /// </summary>
    private static List<string> VersionedAssembliesOf(MethodInfo m)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        void Walk(Type type, bool isTypeArgument)
        {
            if (type.HasElementType) { Walk(type.GetElementType()!, isTypeArgument); return; }
            if (isTypeArgument)
            {
                var name = type.Assembly.GetName();
                found.Add($"{name.Name} {name.Version}");
            }
            if (type.IsGenericType)
                foreach (var arg in type.GetGenericArguments()) Walk(arg, true);
        }
        foreach (var p in m.GetParameters()) Walk(p.ParameterType, false);
        Walk(m.ReturnType, false);
        return found.ToList();
    }

    /// <summary>
    /// The GUIDs Betta.log recorded for a component (ComponentRegistry logs "+ Category/SubCategory/Name (guid)"
    /// on every registration), distinct, ordered by last appearance. Shared read: Rhino may be writing the log.
    /// </summary>
    private List<string> ReadLoggedGuids(string category, string subCategory, string name)
    {
        var result = new List<string>();
        if (!File.Exists(_logFile)) return result;
        var marker = $" + {category}/{subCategory}/{name} (";
        try
        {
            using var stream = new FileStream(_logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var at = line.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0) continue;
                var start = at + marker.Length;
                var end = line.IndexOf(')', start);
                if (end < 0 || !System.Guid.TryParse(line.AsSpan(start, end - start), out var g)) continue;
                var s = g.ToString();
                result.Remove(s);
                result.Add(s);
            }
        }
        catch (IOException) { }
        return result;
    }

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
    /// <summary>Set with [GrasshopperMethod(Guid = ...)], so it never changes.</summary>
    public bool GuidPinned { get; set; }
    /// <summary>Derived, and the signature's generic types embed assembly versions in the hash.</summary>
    public bool GuidVersionDependent { get; set; }
    /// <summary>"Name Version" of each assembly whose version is in the hash.</summary>
    public List<string> VersionedAssemblies { get; set; } = new();
    /// <summary>GUIDs Betta.log recorded for this component, oldest first.</summary>
    public List<string> LoggedGuids { get; set; } = new();
    public List<string> Notes { get; set; } = new();
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
