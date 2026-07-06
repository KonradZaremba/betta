// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Mcp.Inspection;

namespace Betta.Mcp.Resources;

/// <summary>
/// MCP resources are addressable read-only blobs the client can fetch. Same
/// data as the tools, just exposed under a stable URI so a client (or a user
/// browsing the resources palette) can read them without having to call a tool.
/// </summary>
public interface IResource
{
    string Uri { get; }
    string Name { get; }
    string Description { get; }
    string MimeType { get; }
    string Read();
    object Describe() => new { uri = Uri, name = Name, description = Description, mimeType = MimeType };
}

public sealed class ResourceRegistry
{
    private readonly List<IResource> _resources = new();
    public IReadOnlyList<IResource> Resources => _resources;

    public void RegisterAll()
    {
        _resources.Add(new BettaLogResource());
        _resources.Add(new BettaRegistryResource());
    }
}

public sealed class BettaLogResource : IResource
{
    private const int TailLines = 200;
    public string Uri => "betta://log";
    public string Name => "Betta log";
    public string Description => $"Last {TailLines} lines of %AppData%/Grasshopper/Libraries/Betta.log";
    public string MimeType => "text/plain";

    public string Read()
    {
        var path = Paths.LogFile;
        if (!File.Exists(path)) return $"(no log at {path})";
        var collected = new List<string>(TailLines);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (collected.Count == TailLines) collected.RemoveAt(0);
            collected.Add(line);
        }
        return string.Join("\n", collected);
    }
}

public sealed class BettaRegistryResource : IResource
{
    public string Uri => "betta://registry";
    public string Name => "Betta plugin registry";
    public string Description => "JSON snapshot of the same data returned by betta_status: every DLL in the Betta plugin folder and whether Betta would treat it as a plugin.";
    public string MimeType => "application/json";

    public string Read()
    {
        using var inspector = new PluginInspector();
        var entries = inspector.ListAll();
        var payload = new
        {
            pluginFolder = inspector.PluginFolder,
            folderExists = Directory.Exists(inspector.PluginFolder),
            entries = entries.Select(e => new
            {
                file = e.File,
                fullPath = e.FullPath,
                size = e.SizeBytes,
                mtime = e.ModifiedUtc.ToString("O"),
                isBettaPlugin = e.IsBettaPlugin,
                collectionCount = e.CollectionCount,
                methodCount = e.MethodCount,
                error = e.Error,
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
    }
}
