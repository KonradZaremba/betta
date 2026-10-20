// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using System.Text.Json.Nodes;
using Betta.Mcp.Tools;
using Betta.Mcp.Resources;

namespace Betta.Mcp.Protocol;

/// <summary>
/// Minimal Model Context Protocol server over JSON-RPC 2.0 / stdio. It speaks
/// two protocol eras side by side:
/// <list type="bullet">
///   <item><b>Modern (2026-07-28)</b> — stateless. Every request carries
///     <c>params._meta["io.modelcontextprotocol/protocolVersion"]</c>; there is
///     no handshake. <c>server/discover</c> is the probe clients send first.
///     Results carry <c>resultType</c>, list/read results also <c>ttlMs</c> and
///     <c>cacheScope</c>.</item>
///   <item><b>Legacy (2024-11-05 .. 2025-11-25)</b> — <c>initialize</c> /
///     <c>notifications/initialized</c> handshake, plain results.</item>
/// </list>
/// Dual-era rule: a request carrying the modern protocolVersion meta is served
/// by the modern rules; an <c>initialize</c> request pins the process to legacy
/// semantics (modern meta is ignored from then on). Either way the method set is:
/// <c>tools/list</c>, <c>tools/call</c>, <c>resources/list</c>, <c>resources/read</c>,
/// <c>server/discover</c>, plus legacy <c>ping</c>.
///
/// One JSON-RPC message per line on stdin and stdout. We avoid the optional
/// Content-Length framing because every shipping MCP host (Claude Desktop,
/// Cursor, Continue) accepts newline-delimited JSON, and it makes the smoke
/// test as simple as `echo '...' | betta-mcp`.
/// </summary>
public sealed class McpServer
{
    public const string ModernVersion = "2026-07-28";
    private const string LegacyFallbackVersion = "2025-11-25";
    private const string DefaultLegacyVersion = "2024-11-05";
    private const string MetaVersionKey = "io.modelcontextprotocol/protocolVersion";
    private const string MetaServerInfoKey = "io.modelcontextprotocol/serverInfo";
    public const int UnsupportedProtocolVersionCode = -32022;

    // Static lists never change within a process; live state (log, registry)
    // reflects the local machine, so it stays private and short-lived.
    private const int StaticTtlMs = 3_600_000;
    private const int LiveTtlMs = 5_000;

    private static readonly string[] LegacyVersions =
        { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

    /// <summary>Every version this server answers to, newest first.</summary>
    public static readonly IReadOnlyList<string> SupportedVersions =
        new[] { ModernVersion }.Concat(LegacyVersions).ToArray();

    private const string Instructions =
        "Inspect a local Betta Grasshopper installation: loaded plugins, recent log lines, component descriptors, and method-signature validation.";

    private readonly ToolRegistry _tools;
    private readonly ResourceRegistry _resources;
    private readonly JsonSerializerOptions _json;
    private bool _legacyPinned;

    public McpServer()
    {
        _tools = new ToolRegistry();
        _tools.RegisterAll();
        _resources = new ResourceRegistry();
        _resources.RegisterAll();
        _json = new JsonSerializerOptions
        {
            // MCP messages are camelCase across the board.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        string? line;
        while (!cancellationToken.IsCancellationRequested
               && (line = await input.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            var reply = await HandleLineAsync(line).ConfigureAwait(false);
            if (reply is null) continue;
            await output.WriteLineAsync(reply).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Process one JSON-RPC message and return the response JSON, or null when
    /// no response is due (blank line, notification). This is the whole server
    /// minus the stdio loop, which keeps it testable in-process.
    /// </summary>
    public async Task<string?> HandleLineAsync(string line)
    {
        // Tolerate a UTF-8 BOM on the first line (Windows shells prepend one when piping).
        line = line.Trim().TrimStart('﻿');
        if (line.Length == 0) return null;

        JsonRpcRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<JsonRpcRequest>(line, _json);
        }
        catch (JsonException ex)
        {
            return Serialize(new JsonRpcResponse
            {
                Error = new JsonRpcError { Code = JsonRpcError.ParseError, Message = ex.Message }
            });
        }
        if (req is null) return null;

        // JSON-RPC notifications have no id. We must not reply to them.
        var isNotification = req.Id is null || req.Id.Value.ValueKind == JsonValueKind.Null;
        var response = await DispatchAsync(req).ConfigureAwait(false);
        if (isNotification) return null;

        response.Id = req.Id;
        return Serialize(response);
    }

    private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest req)
    {
        try
        {
            if (req.Method == "initialize")
            {
                _legacyPinned = true;
                return new JsonRpcResponse { Result = HandleInitialize(req.Params) };
            }

            // server/discover is the stdio probe: it answers with or without _meta.
            if (req.Method == "server/discover")
                return new JsonRpcResponse { Result = Modern(DiscoverBody(), "public", StaticTtlMs, includeTtl: true) };

            var requested = string.Empty;
            var modern = !_legacyPinned && TryGetMetaVersion(req.Params, out requested);
            if (modern)
            {
                if (!SupportedVersions.Contains(requested))
                    return new JsonRpcResponse
                    {
                        Error = new JsonRpcError
                        {
                            Code = UnsupportedProtocolVersionCode,
                            Message = "Unsupported protocol version",
                            Data = new { supported = SupportedVersions, requested },
                        }
                    };
            }

            switch (req.Method)
            {
                case "initialized":
                case "notifications/initialized":
                    // Notification ack from the client — nothing to do.
                    return new JsonRpcResponse();

                case "ping":
                    // Removed in the modern revision, kept for legacy clients.
                    return new JsonRpcResponse { Result = new { } };

                case "tools/list":
                {
                    // Deterministic order: clients cache the list in the modern era.
                    var body = new { tools = _tools.Tools.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => t.Describe()) };
                    return new JsonRpcResponse { Result = modern ? Modern(body, "public", StaticTtlMs, true) : body };
                }

                case "tools/call":
                {
                    var body = await HandleToolCallAsync(req.Params).ConfigureAwait(false);
                    return new JsonRpcResponse { Result = modern ? Modern(body, null, 0, false) : body };
                }

                case "resources/list":
                {
                    var body = new { resources = _resources.Resources.Select(r => r.Describe()) };
                    return new JsonRpcResponse { Result = modern ? Modern(body, "public", StaticTtlMs, true) : body };
                }

                case "resources/read":
                {
                    object body;
                    try { body = HandleResourceRead(req.Params); }
                    catch (ResourceNotFoundException ex) when (modern)
                    {
                        // The modern revision reports a missing resource as invalid params.
                        return new JsonRpcResponse
                        {
                            Error = new JsonRpcError { Code = JsonRpcError.InvalidParams, Message = ex.Message }
                        };
                    }
                    // Log and registry reflect this machine's live state.
                    return new JsonRpcResponse { Result = modern ? Modern(body, "private", LiveTtlMs, true) : body };
                }

                default:
                    return new JsonRpcResponse
                    {
                        Error = new JsonRpcError
                        {
                            Code = JsonRpcError.MethodNotFound,
                            Message = $"Method '{req.Method}' is not implemented by Betta.Mcp."
                        }
                    };
            }
        }
        catch (Exception ex)
        {
            return new JsonRpcResponse
            {
                Error = new JsonRpcError
                {
                    Code = JsonRpcError.InternalError,
                    Message = ex.Message,
                    Data = new { type = ex.GetType().FullName }
                }
            };
        }
    }

    private static bool TryGetMetaVersion(JsonElement? @params, out string version)
    {
        version = string.Empty;
        if (@params.HasValue && @params.Value.ValueKind == JsonValueKind.Object
            && @params.Value.TryGetProperty("_meta", out var meta) && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty(MetaVersionKey, out var pv) && pv.ValueKind == JsonValueKind.String)
        {
            version = pv.GetString() ?? string.Empty;
            return true;
        }
        return false;
    }

    private object HandleInitialize(JsonElement? @params)
    {
        // Answer with a version we actually support: echo the client's if it is
        // a legacy one we know, otherwise offer the newest legacy revision.
        var version = LegacyFallbackVersion;
        if (@params.HasValue && @params.Value.ValueKind == JsonValueKind.Object
            && @params.Value.TryGetProperty("protocolVersion", out var pv)
            && pv.ValueKind == JsonValueKind.String
            && pv.GetString() is { } requested
            && LegacyVersions.Contains(requested))
        {
            version = requested;
        }

        return new
        {
            protocolVersion = version,
            capabilities = new
            {
                tools = new { },
                resources = new { },
            },
            serverInfo = new
            {
                name = "betta-mcp",
                version = ThisAssemblyVersion(),
            },
        };
    }

    private object DiscoverBody() => new
    {
        supportedVersions = SupportedVersions,
        capabilities = new { tools = new { }, resources = new { } },
        instructions = Instructions,
    };

    /// <summary>
    /// Decorate a result with the modern-only fields: resultType, serverInfo
    /// meta, and (for list/read/discover results) ttlMs + cacheScope.
    /// </summary>
    private object Modern(object body, string? cacheScope, int ttlMs, bool includeTtl)
    {
        var node = JsonSerializer.SerializeToNode(body, _json) as JsonObject ?? new JsonObject();
        node["resultType"] = "complete";
        var meta = node["_meta"] as JsonObject ?? new JsonObject();
        meta[MetaServerInfoKey] = new JsonObject
        {
            ["name"] = "betta-mcp",
            ["version"] = ThisAssemblyVersion(),
        };
        node["_meta"] = meta;
        if (includeTtl)
        {
            node["ttlMs"] = ttlMs;
            node["cacheScope"] = cacheScope ?? "public";
        }
        return node;
    }

    private async Task<object> HandleToolCallAsync(JsonElement? @params)
    {
        if (!@params.HasValue || @params.Value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("tools/call requires an object 'params'.");

        string? name = null;
        JsonElement? args = null;
        if (@params.Value.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            name = nameEl.GetString();
        if (@params.Value.TryGetProperty("arguments", out var argsEl))
            args = argsEl;

        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("tools/call missing 'name'.");

        var tool = _tools.Tools.FirstOrDefault(t => t.Name == name)
                   ?? throw new InvalidOperationException($"Unknown tool '{name}'.");

        var result = await tool.InvokeAsync(args).ConfigureAwait(false);

        // MCP returns tool output as { content: [{ type: "text", text: "..." }], isError: false }.
        // We serialize the structured result as JSON text — clients (Claude Desktop) display
        // and re-feed it just fine, and it keeps tool authoring simple.
        var text = JsonSerializer.Serialize(result, _json);
        return new
        {
            content = new[] { new { type = "text", text } },
            isError = false,
        };
    }

    private object HandleResourceRead(JsonElement? @params)
    {
        if (!@params.HasValue || @params.Value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("resources/read requires an object 'params'.");
        if (!@params.Value.TryGetProperty("uri", out var uriEl) || uriEl.ValueKind != JsonValueKind.String)
            throw new ArgumentException("resources/read missing 'uri'.");

        var uri = uriEl.GetString() ?? string.Empty;
        var resource = _resources.Resources.FirstOrDefault(r => r.Uri == uri)
                       ?? throw new ResourceNotFoundException($"Unknown resource '{uri}'.");

        var text = resource.Read();
        return new
        {
            contents = new[] { new { uri, mimeType = resource.MimeType, text } },
        };
    }

    // Per JSON-RPC 2.0: don't include 'result' and 'error' simultaneously,
    // and don't write a response at all for notifications. Both are handled
    // by the caller / WhenWritingNull policy.
    private string Serialize(JsonRpcResponse response) => JsonSerializer.Serialize(response, _json);

    private static string ThisAssemblyVersion()
    {
        var asm = typeof(McpServer).Assembly;
        var info = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
            asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        var v = info?.InformationalVersion;
        if (!string.IsNullOrEmpty(v))
        {
            int plus = v.IndexOf('+');
            return plus < 0 ? v : v[..plus];
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private sealed class ResourceNotFoundException : InvalidOperationException
    {
        public ResourceNotFoundException(string message) : base(message) { }
    }
}
