# Shared helpers, dot-sourced by measure.ps1 / run.ps1 / report.ps1. Windows PowerShell 5.1 compatible.

[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture

# Windows command-line quoting (CommandLineToArgvW rules). Windows PowerShell 5.1 mangles embedded
# double quotes when passing arguments to native exes, so we build the argument string ourselves.
function ConvertTo-NativeArg {
    param([string]$Arg)
    if ($Arg.Length -gt 0 -and $Arg -notmatch '[\s"]') { return $Arg }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    $backslashes = 0
    foreach ($ch in $Arg.ToCharArray()) {
        if ($ch -eq '\') { $backslashes++; continue }
        if ($ch -eq '"') {
            [void]$sb.Append('\', $backslashes * 2 + 1)
            [void]$sb.Append('"')
        } else {
            if ($backslashes -gt 0) { [void]$sb.Append('\', $backslashes) }
            [void]$sb.Append($ch)
        }
        $backslashes = 0
    }
    if ($backslashes -gt 0) { [void]$sb.Append('\', $backslashes * 2) }
    [void]$sb.Append('"')
    return $sb.ToString()
}

# Runs a native exe with an immediately-closed stdin (claude -p otherwise waits 3 s for stdin data), after
# writing -StdinText to it if given, and capturing stdout and stderr separately as UTF-8. -Environment adds
# variables for the child only. Returns @{ ExitCode; Stdout; Stderr; TimedOut }.
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory = (Get-Location).Path,
        [int]$TimeoutMinutes = 0,
        [string]$StdinText = '',
        [hashtable]$Environment = @{}
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = (($ArgumentList | ForEach-Object { ConvertTo-NativeArg $_ }) -join ' ')
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
    foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }

    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    [void]$p.Start()
    if ($StdinText) { $p.StandardInput.Write($StdinText) }
    $p.StandardInput.Close()
    # Async reads on both pipes avoid the classic full-pipe deadlock.
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    $timedOut = $false
    if ($TimeoutMinutes -gt 0) {
        if (-not $p.WaitForExit($TimeoutMinutes * 60000)) {
            $timedOut = $true
            try { $p.Kill() } catch { }
            $p.WaitForExit()
        }
    } else {
        $p.WaitForExit()
    }
    return @{
        ExitCode = $p.ExitCode
        Stdout   = $outTask.Result
        Stderr   = $errTask.Result
        TimedOut = $timedOut
    }
}

# Lays a plugin DLL out like %AppData%\Grasshopper\Libraries: Betta.Abstractions.dll in -Dir, the plugin in
# -Dir\Betta (the folder Betta and Betta.Mcp scan). Returns the plugin folder.
function New-BettaLibraries {
    param([Parameter(Mandatory)][string]$Dir, [Parameter(Mandatory)][string]$PluginDll, [Parameter(Mandatory)][string]$AbstractionsDll)
    $pluginDir = Join-Path $Dir 'Betta'
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    Copy-Item -Path $AbstractionsDll -Destination $Dir -Force
    Copy-Item -Path $PluginDll -Destination $pluginDir -Force
    return $pluginDir
}

# The GUID Betta publishes for a component (a pinned [GrasshopperMethod(Guid = ...)] or the type|method hash),
# read through Betta.Mcp's betta_describe in one stateless MCP request. $null if the component isn't found.
# -WithAliases returns it followed by the hidden aliases saved files also open through (FormerNames, pre-0.8).
function Get-BettaComponentGuid {
    param([Parameter(Mandatory)][string]$McpExe, [Parameter(Mandatory)][string]$PluginFolder, [Parameter(Mandatory)][string]$Component,
        [switch]$WithAliases)
    $req = @{ jsonrpc = '2.0'; id = 1; method = 'tools/call'
        params = @{ name = 'betta_describe'; arguments = @{ name = $Component }; _meta = @{ 'io.modelcontextprotocol/protocolVersion' = '2026-07-28' } } }
    $r = Invoke-Native -FilePath $McpExe -StdinText (($req | ConvertTo-Json -Depth 6 -Compress) + "`n") -Environment @{ BETTA_PLUGIN_FOLDER = $PluginFolder } -TimeoutMinutes 1
    try {
        $line = @($r.Stdout -split "`n" | Where-Object { $_.Trim() })[0]
        $d = ($line | ConvertFrom-Json).result.content[0].text | ConvertFrom-Json
        if ($d.found -and $WithAliases) {
            return @([string]$d.descriptor.guid) + @($d.descriptor.formerGuids | ForEach-Object { [string]$_ }) +
                @($d.descriptor.legacyGuid | Where-Object { $_ } | ForEach-Object { [string]$_ })
        }
        if ($d.found) { return [string]$d.descriptor.guid }
    } catch { }
    return $null
}
