// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Betta.Mcp.Tests;

/// <summary>
/// Runs the real betta-mcp process over stdio. The stdio transport is UTF-8,
/// but Console.In on Windows decodes with the console code page, which used to
/// garble any non-ASCII argument. The in-process tests never touch Program.cs,
/// so only a real process catches that.
/// </summary>
public class StdioEncodingTests
{
    [Fact]
    public async Task Non_ascii_arguments_survive_the_stdio_round_trip()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "betta-mcp.dll");
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;

        // Raw UTF-8 bytes, with the BOM a Windows pipe can prepend.
        const string request =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"betta_validate_signature\"," +
            "\"arguments\":{\"signature\":\"double Łuk(double kąt)\"}}}\n";
        var stdin = proc.StandardInput.BaseStream;
        await stdin.WriteAsync(new byte[] { 0xEF, 0xBB, 0xBF });
        await stdin.WriteAsync(Encoding.UTF8.GetBytes(request));
        stdin.Close();

        var line = await proc.StandardOutput.ReadLineAsync();
        Assert.True(proc.WaitForExit(10_000), "betta-mcp did not exit after stdin closed");
        Assert.NotNull(line);

        var text = JsonDocument.Parse(line!).RootElement
            .GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        var parsed = JsonDocument.Parse(text!).RootElement.GetProperty("parsed");
        Assert.Equal("Łuk", parsed.GetProperty("name").GetString());
        Assert.Equal("kąt", parsed.GetProperty("parameters")[0].GetProperty("name").GetString());
    }
}
