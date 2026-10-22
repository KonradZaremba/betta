// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;
using Betta.Components;
using Betta.Services;
using Grasshopper.Kernel;

// Usage: inspect <plugin.dll|.gha> <out.json>
// Writes every component the plugin publishes, the way Grasshopper would build it: a GH_Component
// library by instantiating each GH_Component subclass, a Betta library through ComponentRegistry and
// BettaComponentProxy. Needs a Rhino 8 install (RhinoCommon and Grasshopper resolve from it), not a
// running Rhino.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("usage: inspect <plugin.dll|.gha> <out.json>"); return 2; }
        RhinoInside.Resolver.Initialize();
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromGrasshopperFolder;
        return Run(Path.GetFullPath(args[0]), args[1]);
    }

    // Separate method so no Grasshopper type is touched before the resolver is in place.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string pluginPath, string outPath)
    {
        var context = new PluginContext(Path.GetDirectoryName(pluginPath));
        var asm = context.LoadFromAssemblyPath(pluginPath);
        var isBetta = asm.GetReferencedAssemblies().Any(a => a.Name == "Betta.Abstractions");
        var components = isBetta ? FromBetta(asm) : FromGhComponents(asm);

        var report = new
        {
            plugin = Path.GetFileName(pluginPath),
            kind = isBetta ? "betta" : "gh_component",
            components,
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{components.Count} components ({report.kind}) -> {outPath}");
        return 0;
    }

    private static List<object> FromBetta(Assembly asm)
    {
        var registry = new ComponentRegistry();
        var result = new List<object>();
        foreach (var d in registry.DiscoverFromAssembly(asm))
        {
            var returnType = d.Method.ReturnType;
            var isAsync = typeof(Task).IsAssignableFrom(returnType) ||
                          returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>) ||
                          returnType == typeof(ValueTask);
            result.Add(Describe(() => (GH_Component)new BettaComponentProxy(d).CreateInstance(), new
            {
                method = d.ServiceType.Name + "." + d.Method.Name,
                trigger = d.HasTrigger,
                isAsync,
            }));
        }
        return result;
    }

    private static List<object> FromGhComponents(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

        var result = new List<object>();
        foreach (var t in types.Where(t => typeof(GH_Component).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null))
        {
            var bases = new List<string>();
            for (var b = t.BaseType; b != null; b = b.BaseType) bases.Add(b.Name);
            result.Add(Describe(() => (GH_Component)Activator.CreateInstance(t), new
            {
                type = t.Name,
                baseTypes = bases,
                isAsync = bases.Any(b => b.StartsWith("GH_TaskCapableComponent", StringComparison.Ordinal)),
            }));
        }
        return result;
    }

    private static object Describe(Func<GH_Component> create, object extra)
    {
        try
        {
            var c = create();
            return new
            {
                name = c.Name,
                nickName = c.NickName,
                category = c.Category,
                subCategory = c.SubCategory,
                guid = c.ComponentGuid,
                inputs = c.Params.Input.Select(Param).ToList(),
                outputs = c.Params.Output.Select(Param).ToList(),
                extra,
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.GetType().Name + ": " + ex.Message, extra };
        }
    }

    private static object Param(IGH_Param p) => new
    {
        name = p.Name,
        nickName = p.NickName,
        paramType = p.GetType().Name,
        typeName = p.TypeName,
        access = p.Access.ToString(),
        optional = p.Optional,
    };

    // Grasshopper.dll and GH_IO.dll live in <Rhino>\Plug-ins\Grasshopper, outside the folder
    // RhinoInside.Resolver covers (same probe as TestBetta's GrasshopperSingleton).
    private static Assembly ResolveFromGrasshopperFolder(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (name == null || name.EndsWith(".resources", StringComparison.Ordinal)) return null;
        var system = RhinoInside.Resolver.RhinoSystemDirectory;
        if (string.IsNullOrEmpty(system)) return null;
        var root = Path.GetDirectoryName(system.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var candidate = Path.Combine(root ?? "", "Plug-ins", "Grasshopper", name + ".dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }

    // The plugin may be built against a newer Betta.Abstractions than this tool's; hand it the copy
    // Betta itself uses so their types match. Its own other dependencies load from its folder.
    private sealed class PluginContext : AssemblyLoadContext
    {
        private readonly string _dir;
        public PluginContext(string dir) : base("plugin") { _dir = dir; }

        protected override Assembly Load(AssemblyName name)
        {
            if (name.Name == "Betta.Abstractions") return typeof(Betta.Interfaces.IBettaCollection).Assembly;
            if (Default.Assemblies.Any(a => a.GetName().Name == name.Name)) return null;
            var local = Path.Combine(_dir, name.Name + ".dll");
            return File.Exists(local) ? LoadFromAssemblyPath(local) : null;
        }
    }
}
