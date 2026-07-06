// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

namespace Betta.Docs;

/// <summary>
/// CLI entry point. Hand-parses argv so the tool has no extra dependencies
/// beyond System.Reflection.MetadataLoadContext.
/// </summary>
internal static class CliApp
{
    public static int Run(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts.ShowHelp)
        {
            PrintHelp();
            return 0;
        }
        if (opts.Error is not null)
        {
            Console.Error.WriteLine("error: " + opts.Error);
            Console.Error.WriteLine();
            PrintHelp();
            return 2;
        }

        var dlls = CollectDlls(opts);
        if (dlls.Count == 0)
        {
            Console.Error.WriteLine("error: no DLLs found from --plugin / --folder arguments");
            return 2;
        }

        var outDir = Path.GetFullPath(opts.OutPath ?? "./docs-betta");
        Directory.CreateDirectory(outDir);

        var allComponents = new List<DiscoveredComponent>();
        foreach (var dll in dlls)
        {
            try
            {
                var found = Discoverer.Discover(dll);
                if (found.Count == 0)
                {
                    Console.WriteLine($"  · {Path.GetFileName(dll)}: no Betta components");
                    continue;
                }
                Console.WriteLine($"  + {Path.GetFileName(dll)}: {found.Count} component(s)");
                allComponents.AddRange(found);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ! {Path.GetFileName(dll)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (allComponents.Count == 0)
        {
            Console.Error.WriteLine("no Betta components found across the supplied DLLs");
            return 1;
        }

        var files = MarkdownEmitter.Emit(allComponents, outDir);
        Console.WriteLine();
        Console.WriteLine($"Wrote {files.Count} markdown file(s) to {outDir}");
        foreach (var f in files)
            Console.WriteLine($"  - {Path.GetRelativePath(outDir, f)}");

        return 0;
    }

    private static List<string> CollectDlls(Options opts)
    {
        var list = new List<string>();
        foreach (var p in opts.Plugins)
        {
            var full = Path.GetFullPath(p);
            if (!File.Exists(full))
            {
                Console.Error.WriteLine($"  ! --plugin not found: {full}");
                continue;
            }
            list.Add(full);
        }
        foreach (var f in opts.Folders)
        {
            var full = Path.GetFullPath(f);
            if (!Directory.Exists(full))
            {
                Console.Error.WriteLine($"  ! --folder not found: {full}");
                continue;
            }
            foreach (var dll in Directory.EnumerateFiles(full, "*.dll", SearchOption.TopDirectoryOnly))
                list.Add(dll);
        }
        // De-dup while preserving order
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "-h":
                case "--help":
                    o.ShowHelp = true;
                    return o;
                case "--plugin":
                    if (++i >= args.Length) { o.Error = "--plugin requires a path"; return o; }
                    o.Plugins.Add(args[i]);
                    break;
                case "--folder":
                    if (++i >= args.Length) { o.Error = "--folder requires a path"; return o; }
                    o.Folders.Add(args[i]);
                    break;
                case "--out":
                    if (++i >= args.Length) { o.Error = "--out requires a path"; return o; }
                    o.OutPath = args[i];
                    break;
                default:
                    o.Error = $"unknown argument: {a}";
                    return o;
            }
        }
        if (o.Plugins.Count == 0 && o.Folders.Count == 0)
            o.Error = "at least one --plugin or --folder is required";
        return o;
    }

    private static void PrintHelp()
    {
        var version = typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? typeof(CliApp).Assembly.GetName().Version?.ToString() ?? "?";
        Console.WriteLine($"betta-docs {version}");
        Console.WriteLine("Reflects over Betta plugin DLLs and emits markdown component documentation.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  betta-docs --plugin PATH [--plugin PATH ...] [--out DIR]");
        Console.WriteLine("  betta-docs --folder PATH [--folder PATH ...] [--out DIR]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --plugin PATH   A single Betta plugin DLL. Repeatable.");
        Console.WriteLine("  --folder PATH   A folder of plugin DLLs. Repeatable. Walks *.dll non-recursively.");
        Console.WriteLine("  --out DIR       Output directory for markdown. Default: ./docs-betta/");
        Console.WriteLine("  -h, --help      Show this help.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  betta-docs --folder \"%APPDATA%/Grasshopper/Libraries/Betta\" --out ./docs");
    }

    private sealed class Options
    {
        public List<string> Plugins { get; } = new();
        public List<string> Folders { get; } = new();
        public string? OutPath { get; set; }
        public bool ShowHelp { get; set; }
        public string? Error { get; set; }
    }
}
