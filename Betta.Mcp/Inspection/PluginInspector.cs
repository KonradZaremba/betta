// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using Betta.Guids;

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

    /// <param name="extraFolders">More folders to resolve assemblies from, such as the build output of a plugin read by path.</param>
    public PluginInspector(string? pluginFolder = null, string? logFile = null, IEnumerable<string>? extraFolders = null)
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
        foreach (var folder in extraFolders ?? Enumerable.Empty<string>())
            if (Directory.Exists(folder))
                probe.AddRange(Directory.GetFiles(folder, "*.dll"));

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

                    var overloaded = IsOverloaded(t, m);
                    try
                    {
                        d.Guid = PublishedGuid(t, m, named, overloaded).ToString();
                        if (!d.GuidPinned)
                        {
                            var legacy = GuidScheme.LegacyGuid(t, m).ToString();
                            if (legacy != d.Guid) d.LegacyGuid = legacy;
                        }
                        d.FormerGuids = FormerGuids(t, m, NamedStrings(named, "FormerNames"), ReadCollectionFormerNames(t), overloaded)
                            .Select(g => g.ToString()).Where(g => g != d.Guid).ToList();
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
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException)
                    {
                        // A type in the signature lives in an assembly this server can't find.
                        d.Notes.Add($"Couldn't resolve a type in the signature ({ex.Message}), so the GUID can't be computed here. " +
                                    "loggedGuids, or the Betta Inspector component in Rhino, shows the published value.");
                        return d;
                    }

                    d.Notes.AddRange(GuidNotes(d, overloaded));
                    return d;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Every <c>[GrasshopperMethod]</c> in <paramref name="dllPath"/>, or in every plugin in the Betta
    /// folder, as betta.lock sees it: the GUID Betta publishes, the FormerNames GUIDs, and the label
    /// the build writes. A method whose signature names a type this server can't resolve is left
    /// out and listed in <paramref name="errors"/>.
    /// </summary>
    internal List<Betta.Lock.LockComponent> LockComponents(string? dllPath, List<string> errors)
    {
        var dlls = dllPath != null
            ? new[] { dllPath }
            : Directory.Exists(_pluginFolder) ? Directory.GetFiles(_pluginFolder, "*.dll").Where(IsPluginCandidate).ToArray() : Array.Empty<string>();
        var result = new List<Betta.Lock.LockComponent>();
        foreach (var dll in dlls)
        {
            (List<Type> Interfaces, List<Type> Classes) collections;
            try
            {
                collections = SelectCollectionTypes(_mlc.LoadFromAssemblyPath(dll));
            }
            catch (Exception ex) when (ex is FileLoadException or FileNotFoundException or BadImageFormatException or TypeLoadException)
            {
                errors.Add($"{Path.GetFileName(dll)}: {ex.Message}");
                continue;
            }

            foreach (var t in collections.Interfaces.Concat(collections.Classes))
            foreach (var m in t.GetMethods())
            {
                if (!TryGetMethodAttribute(m, out var attr)) continue;
                var named = AttributeReader.NamedArguments(attr);
                var overloaded = IsOverloaded(t, m);
                try
                {
                    result.Add(new Betta.Lock.LockComponent(GuidScheme.StableTypeName(t), m.Name, overloaded,
                        m.GetParameters().Select(p => GuidScheme.StableTypeName(p.ParameterType)).ToArray(),
                        PublishedGuid(t, m, named, overloaded),
                        FormerGuids(t, m, NamedStrings(named, "FormerNames"), ReadCollectionFormerNames(t), overloaded).ToList(),
                        NamedBool(named, "Enabled") ?? true));
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException)
                {
                    errors.Add($"{t.FullName}.{m.Name}: couldn't resolve a type in its signature ({ex.Message}), so its GUID is unknown here.");
                }
            }
        }
        return result;
    }

    /// <summary>The GUID Betta publishes: <c>[GrasshopperMethod(Guid = …)]</c> when pinned, else derived as <c>ComponentDescriptor</c> does.</summary>
    private static Guid PublishedGuid(Type t, MethodInfo m, IReadOnlyDictionary<string, object?> named, bool overloaded) =>
        System.Guid.TryParse(NamedString(named, "Guid"), out var pinned) ? pinned
        : GuidScheme.MethodGuid(t, m, overloaded);

    // Disabled methods count, as in ComponentDescriptor, so toggling Enabled changes no GUID.
    private static bool IsOverloaded(Type t, MethodInfo m) =>
        GuidScheme.IsOverloaded(t, m, o => TryGetMethodAttribute(o, out _));

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

    private static IEnumerable<string> GuidNotes(DescribedComponent d, bool overloaded)
    {
        if (d.GuidPinned)
        {
            yield return "Pinned with [GrasshopperMethod(Guid = ...)], for example kept from a ported GH_Component: it never changes.";
            yield break;
        }
        var pin = $"[GrasshopperMethod(..., Guid = \"{d.Guid}\")]";
        yield return overloaded
            ? "This method name is overloaded, so the GUID is derived from the type, method, parameters and return type. Any change " +
              $"to the signature breaks saved .gh files; to make one, pin the current value first: {pin}. Adding the overload also " +
              "changed the GUID of the method that had the name first; to keep its saved files, list its own name on it: " +
              $"[GrasshopperMethod(..., FormerNames = new[] {{ \"{d.Method}\" }})]. Distinct method names avoid all of this."
            : "Derived from the type and method name only: parameters and outputs can change without breaking saved .gh files " +
              "(add new inputs and outputs after the existing ones, since files reconnect wires by position). Renaming the method or its type, " +
              "or moving the method to another type, changes it; when you do, list the old name so saved files still open: " +
              $"[GrasshopperMethod(..., FormerNames = new[] {{ \"{d.Method}\" }})] (or \"{d.ServiceType}.{d.Method}\" when moving it to another type, " +
              $"or [GrasshopperCollection(FormerNames = new[] {{ \"{d.ServiceType}\" }})] when renaming the type). Pinning works too: {pin}.";
        if (d.FormerGuids.Count > 0)
            yield return $"Also answers to {string.Join(", ", d.FormerGuids)} from FormerNames: files saved under those names open and take the current GUID when saved again.";
        if (d.LegacyGuid != null)
            yield return $"Betta 0.7 and earlier published this component as {d.LegacyGuid}: that scheme hashed the whole signature, and " +
                         "for generic types their assembly versions (computed here with this server's .NET 8 and the installed RhinoCommon). " +
                         "Betta 0.8 registers it as a hidden alias, so 0.7 files open and take the new GUID on the next save; for generic " +
                         "signatures only when saved with the same plugin, Rhino and .NET versions. loggedGuids shows what Rhino actually published.";
    }

    /// <summary>
    /// The GUIDs Betta.log recorded for a component (ComponentRegistry logs "+ Category/SubCategory/Name (guid)"
    /// on every registration), distinct, ordered by last appearance. Reads Betta.log.old first: Betta rolls the
    /// log at 10 MB, and the GUIDs an older release published are the ones worth pinning. Shared read: Rhino may
    /// be writing the log.
    /// </summary>
    private List<string> ReadLoggedGuids(string category, string subCategory, string name)
    {
        var result = new List<string>();
        var marker = $" + {category}/{subCategory}/{name} (";
        foreach (var line in LoggedRegistrations())
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
        return result;
    }

    private List<string>? _loggedRegistrations;

    // The " + " registration lines of Betta.log.old then Betta.log, read once per inspector, not per component.
    private List<string> LoggedRegistrations()
    {
        if (_loggedRegistrations != null) return _loggedRegistrations;
        _loggedRegistrations = new List<string>();
        foreach (var path in new[] { _logFile + ".old", _logFile })
        {
            if (!File.Exists(path)) continue;
            try { _loggedRegistrations.AddRange(SharedFile.ReadLines(path).Where(l => l.Contains(" + ", StringComparison.Ordinal))); }
            catch (IOException) { }
        }
        return _loggedRegistrations;
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

    /// <summary>The GUIDs a method had under its FormerNames, hashed as <c>ComponentDescriptor</c> does.</summary>
    private static IEnumerable<Guid> FormerGuids(Type serviceType, MethodInfo m, string[] formerMethodNames, string[] formerTypeNames,
        bool overloaded) =>
        GuidScheme.FormerInputs(GuidScheme.StableTypeName(serviceType), m.Name, overloaded, formerMethodNames, formerTypeNames)
            .Select(GuidScheme.Md5);

    private static string[] ReadCollectionFormerNames(Type t)
    {
        var attr = t.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.FullName == CollectionAttribute);
        return attr is null ? Array.Empty<string>() : NamedStrings(AttributeReader.NamedArguments(attr), "FormerNames");
    }

    // A string[] named argument arrives as a collection of typed arguments.
    private static string[] NamedStrings(IReadOnlyDictionary<string, object?> args, string key)
        => args.TryGetValue(key, out var v) && v is IEnumerable<CustomAttributeTypedArgument> items
            ? items.Select(i => i.Value).OfType<string>().ToArray()
            : Array.Empty<string>();

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
    /// <summary>
    /// The pre-0.8 GUID (a hash that included assembly versions) when it differs from <see cref="Guid"/>;
    /// Betta 0.8 registers it as a hidden alias.
    /// </summary>
    public string? LegacyGuid { get; set; }
    /// <summary>GUIDs from the method's and type's FormerNames, each a hidden alias in Betta.</summary>
    public List<string> FormerGuids { get; set; } = new();
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
