// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Mcp.Tools;
using Betta.Mcp.Resources;

namespace Betta.Mcp.Protocol;

/// <summary>
/// Minimal Model Context Protocol server over JSON-RPC 2.0 / stdio.
///
/// Implements the small subset of MCP that Claude Desktop and other clients
/// need to discover and call our tools and resources:
/// <list type="bullet">
///   <item><c>initialize</c> — handshake; returns server info and capabilities.</item>
///   <item><c>initialized</c> (notification) — client acknowledges handshake.</item>
///   <item><c>tools/list</c> — enumerate registered tools with input schemas.</item>
///   <item><c>tools/call</c> — invoke a tool by name with arguments.</item>
///   <item><c>resources/list</c>, <c>resources/read</c> — enumerate / read named resources.</item>
///   <item><c>ping</c> — health check (some clients send this).</item>
/// </list>
/// One JSON-RPC message per line on stdin and stdout. We avoid the optional
/// Content-Length framing because every shipping MCP host (Claude Desktop,
/// Cursor, Continue) accepts newline-delimited JSON, and it makes the smoke
/// test as simple as `echo '...' | betta-mcp`.
/// </summary>
public sealed class McpServer
{
    // MCP servers and clients negotiate which version of the protocol they
    // speak. 2024-11-05 is the version most current clients (Claude Desktop
    // 0.7+, Cursor) request. If the client asks for something else we echo
    // their requested version back — the message shape we use is stable
    // across these revisions.
    private const string DefaultProtocolVersion = "2024-11-05";

    private readonly ToolRegistry _tools;
    private readonly ResourceRegistry _resources;
    private readonly JsonSerializerOptions _json;

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
            line = line.Trim();
            if (line.Length == 0) continue;

            JsonRpcRequest? req;
            try
            {
                req = JsonSerializer.Deserialize<JsonRpcRequest>(line, _json);
            }
            catch (JsonException ex)
            {
                await WriteResponseAsync(output, new JsonRpcResponse
                {
                    Error = new JsonRpcError { Code = JsonRpcError.ParseError, Message = ex.Message }
                }).ConfigureAwait(false);
                continue;
            }
            if (req is null) continue;

            // JSON-RPC notifications have no id. We must not reply to them.
            var isNotification = req.Id is null || req.Id.Value.ValueKind == JsonValueKind.Null;
            var response = await DispatchAsync(req).ConfigureAwait(false);
            if (isNotification) continue;

            response.Id = req.Id;
            await WriteResponseAsync(output, response).ConfigureAwait(false);
        }
    }

    private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest req)
    {
        try
        {
            switch (req.Method)
            {
                case "initialize":
                    return new JsonRpcResponse { Result = HandleInitialize(req.Params) };

                case "initialized":
                case "notifications/initialized":
                    // Notification ack from the client — nothing to do.
                    return new JsonRpcResponse();

                case "ping":
                    return new JsonRpcResponse { Result = new { } };

                case "tools/list":
                    return new JsonRpcResponse { Result = new { tools = _tools.Tools.Select(t => t.Describe()) } };

                case "tools/call":
                    return new JsonRpcResponse { Result = await HandleToolCallAsync(req.Params).ConfigureAwait(false) };

                case "resources/list":
                    return new JsonRpcResponse { Result = new { resources = _resources.Resources.Select(r => r.Describe()) } };

                case "resources/read":
                    return new JsonRpcResponse { Result = HandleResourceRead(req.Params) };

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

    private object HandleInitialize(JsonElement? @params)
    {
        var version = DefaultProtocolVersion;
        if (@params.HasValue && @params.Value.ValueKind == JsonValueKind.Object
            && @params.Value.TryGetProperty("protocolVersion", out var pv)
            && pv.ValueKind == JsonValueKind.String)
        {
            version = pv.GetString() ?? DefaultProtocolVersion;
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
                       ?? throw new InvalidOperationException($"Unknown resource '{uri}'.");

        var text = resource.Read();
        return new
        {
            contents = new[] { new { uri, mimeType = resource.MimeType, text } },
        };
    }

    private async Task WriteResponseAsync(TextWriter output, JsonRpcResponse response)
    {
        // Per JSON-RPC 2.0: don't include 'result' and 'error' simultaneously,
        // and don't write a response at all for notifications. Both are handled
        // by the caller / WhenWritingNull policy.
        var json = JsonSerializer.Serialize(response, _json);
        await output.WriteLineAsync(json).ConfigureAwait(false);
        await output.FlushAsync().ConfigureAwait(false);
    }

    private static string ThisAssemblyVersion()
        => typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
