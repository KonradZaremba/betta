// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Betta.Mcp.Protocol;
using Xunit;

namespace Betta.Mcp.Tests;

/// <summary>Drives the real McpServer in-process, one JSON-RPC line at a time.</summary>
public class McpProtocolTests
{
    private const string MetaKey = "io.modelcontextprotocol/protocolVersion";
    private static readonly string[] Supported = { "2026-07-28", "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

    private static async Task<JsonElement> Send(McpServer server, string json)
    {
        var reply = await server.HandleLineAsync(json);
        Assert.NotNull(reply);
        return JsonDocument.Parse(reply!).RootElement.Clone();
    }

    private static string ModernRequest(string method, string version = "2026-07-28", string id = "1") =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{{\"_meta\":{{\"{MetaKey}\":\"{version}\"}}}}}}";

    private static string Initialize(string version) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{{\"protocolVersion\":\"{version}\"}}}}";

    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    public async Task Legacy_initialize_echoes_supported_version(string version)
    {
        var r = await Send(new McpServer(), Initialize(version));
        Assert.Equal(version, r.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Theory]
    [InlineData("1900-01-01")]
    [InlineData("2026-07-28")] // modern revision has no initialize handshake
    public async Task Legacy_initialize_answers_2025_11_25_for_unknown_version(string version)
    {
        var r = await Send(new McpServer(), Initialize(version));
        Assert.Equal("2025-11-25", r.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task Legacy_responses_carry_no_modern_fields()
    {
        var server = new McpServer();
        var init = await Send(server, Initialize("2025-06-18"));
        Assert.Null(await server.HandleLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
        var list = await Send(server, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");

        foreach (var result in new[] { init.GetProperty("result"), list.GetProperty("result") })
        {
            Assert.False(result.TryGetProperty("resultType", out _));
            Assert.False(result.TryGetProperty("ttlMs", out _));
            Assert.False(result.TryGetProperty("cacheScope", out _));
        }
        Assert.Equal(5, list.GetProperty("result").GetProperty("tools").GetArrayLength());
    }

    [Fact]
    public async Task Legacy_ping_still_works()
    {
        var r = await Send(new McpServer(), "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ping\"}");
        Assert.Equal(JsonValueKind.Object, r.GetProperty("result").ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discover_has_expected_shape_with_and_without_meta(bool withMeta)
    {
        var json = withMeta
            ? ModernRequest("server/discover")
            : "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"server/discover\"}";
        var result = (await Send(new McpServer(), json)).GetProperty("result");

        Assert.Equal("complete", result.GetProperty("resultType").GetString());
        Assert.Equal(Supported, result.GetProperty("supportedVersions").EnumerateArray().Select(e => e.GetString()));
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.True(result.GetProperty("capabilities").TryGetProperty("resources", out _));
        var info = result.GetProperty("_meta").GetProperty("io.modelcontextprotocol/serverInfo");
        Assert.Equal("betta-mcp", info.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(info.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrEmpty(result.GetProperty("instructions").GetString()));
        Assert.True(result.GetProperty("ttlMs").GetInt32() > 0);
        Assert.Equal("public", result.GetProperty("cacheScope").GetString());
    }

    [Fact]
    public async Task Modern_tools_list_is_cacheable_sorted_and_annotated()
    {
        var result = (await Send(new McpServer(), ModernRequest("tools/list"))).GetProperty("result");

        Assert.Equal("complete", result.GetProperty("resultType").GetString());
        Assert.True(result.GetProperty("ttlMs").GetInt32() > 0);
        Assert.Equal("public", result.GetProperty("cacheScope").GetString());
        Assert.True(result.GetProperty("_meta").TryGetProperty("io.modelcontextprotocol/serverInfo", out _));

        var tools = result.GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(5, tools.Count);
        var names = tools.Select(t => t.GetProperty("name").GetString()!).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
        foreach (var t in tools)
        {
            Assert.True(t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
            Assert.False(string.IsNullOrEmpty(t.GetProperty("title").GetString()));
        }
    }

    [Fact]
    public async Task Modern_resources_list_is_public_and_read_is_private()
    {
        var server = new McpServer();
        var list = (await Send(server, ModernRequest("resources/list"))).GetProperty("result");
        Assert.Equal("public", list.GetProperty("cacheScope").GetString());
        Assert.Equal("complete", list.GetProperty("resultType").GetString());

        var read = await Send(server,
            $"{{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"resources/read\",\"params\":{{\"uri\":\"betta://log\",\"_meta\":{{\"{MetaKey}\":\"2026-07-28\"}}}}}}");
        var result = read.GetProperty("result");
        Assert.Equal("private", result.GetProperty("cacheScope").GetString());
        Assert.True(result.TryGetProperty("ttlMs", out _));
    }

    [Fact]
    public async Task Modern_unknown_resource_is_invalid_params()
    {
        var r = await Send(new McpServer(),
            $"{{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"resources/read\",\"params\":{{\"uri\":\"betta://nope\",\"_meta\":{{\"{MetaKey}\":\"2026-07-28\"}}}}}}");
        Assert.Equal(-32602, r.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Modern_unsupported_version_is_rejected_with_32022()
    {
        var error = (await Send(new McpServer(), ModernRequest("tools/list", "1900-01-01"))).GetProperty("error");

        Assert.Equal(-32022, error.GetProperty("code").GetInt32());
        Assert.Equal("Unsupported protocol version", error.GetProperty("message").GetString());
        var data = error.GetProperty("data");
        Assert.Equal("1900-01-01", data.GetProperty("requested").GetString());
        Assert.Equal(Supported, data.GetProperty("supported").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Modern_tools_call_is_stateless_and_carries_result_type()
    {
        var r = await Send(new McpServer(),
            $"{{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{{\"name\":\"betta_validate_signature\",\"arguments\":{{\"signature\":\"double Cube(double x)\"}},\"_meta\":{{\"{MetaKey}\":\"2026-07-28\"}}}}}}");
        var result = r.GetProperty("result");
        Assert.Equal("complete", result.GetProperty("resultType").GetString());
        Assert.False(result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task Initialize_pins_process_to_legacy_semantics()
    {
        var server = new McpServer();
        await Send(server, Initialize("2025-11-25"));
        var list = await Send(server, ModernRequest("tools/list", id: "2"));
        Assert.False(list.GetProperty("result").TryGetProperty("resultType", out _));
    }

    [Fact]
    public async Task Parse_error_and_unknown_method_are_reported()
    {
        var server = new McpServer();
        var bad = await Send(server, "{not json");
        Assert.Equal(-32700, bad.GetProperty("error").GetProperty("code").GetInt32());
        var unknown = await Send(server, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"nope\"}");
        Assert.Equal(-32601, unknown.GetProperty("error").GetProperty("code").GetInt32());
    }
}
