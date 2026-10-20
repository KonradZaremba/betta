<#
.SYNOPSIS
  Token/cost benchmark: a headless Claude Code agent adds the same GH component to a Betta project and to a
  plain GH_Component project. See README.md. Real runs bill your Claude usage; use -DryRun first.
#>
param(
    [string[]]$Tasks = @('t1-remap', 't2-divide', 't3-async'),   # full names or prefixes (t1, t2, ...)
    [string[]]$Arms = @('betta', 'vanilla'),
    [int]$Runs = 3,
    [string]$Model = 'sonnet',
    [int]$MaxTurns = 40,
    [double]$MaxBudgetUsd = 3,
    [string]$OutDir,
    [ValidateSet('skill', 'none')][string]$SkillMode = 'skill',
    [string[]]$ExtraArgs = @(),
    [int]$TimeoutMinutes = 45,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'measure.ps1')

$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$stamp = Get-Date -Format 'yyyy-MM-dd-HHmm'
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "results\$stamp" }
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'raw') | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$runId = Split-Path $OutDir -Leaf
# Workspaces live outside C:\Users\konra on purpose so no ancestor CLAUDE.md is loaded.
$wsRoot = Join-Path 'C:\tmp\betta-bench' $runId

# Empty MCP config (BOM-free) so --strict-mcp-config loads no servers.
$mcpConfig = Join-Path $OutDir 'mcp-empty.json'
[IO.File]::WriteAllText($mcpConfig, '{"mcpServers":{}}', (New-Object Text.UTF8Encoding($false)))

$skillSrc = Join-Path $repoRoot '.claude\skills\betta-plugin\SKILL.md'
$csvPath = Join-Path $OutDir 'runs.csv'
# Same tools for both arms. The build is allowed through either shell tool, plus read-only commands an
# agent pipes build output into, so a denied `dotnet build | tail` doesn't burn turns unevenly.
$allowedTools = @('Read', 'Edit', 'Write', 'Glob', 'Grep',
    'Bash(dotnet build:*)', 'Bash(dotnet restore:*)', 'PowerShell(dotnet build:*)', 'PowerShell(dotnet restore:*)',
    'Bash(ls:*)', 'Bash(cat:*)', 'Bash(head:*)', 'Bash(tail:*)', 'Bash(grep:*)', 'Bash(find:*)')
$isolationFlags = '--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools ' + ($allowedTools -join ' ')

$meta = [ordered]@{
    date = (Get-Date).ToString('yyyy-MM-dd HH:mm'); model = $Model; runs = $Runs; max_turns = $MaxTurns
    max_budget_usd = $MaxBudgetUsd; skill_mode = $SkillMode; dry_run = [bool]$DryRun
    extra_args = $ExtraArgs; isolation_flags = $isolationFlags
}
[IO.File]::WriteAllText((Join-Path $OutDir 'meta.json'), ($meta | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

function Get-Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$name]
    if ($p) { return $p.Value }
    return $null
}
function Sum-Cell($values) {
    $present = @($values | Where-Object { $null -ne $_ })
    if ($present.Count -eq 0) { return $null }
    return ($present | Measure-Object -Sum).Sum
}

# Parses the claude JSON result into the token/cost columns. Tolerates missing fields.
function Read-ClaudeResult([string]$rawPath) {
    $r = [ordered]@{ subtype = $null; is_error = $null; num_turns = $null; duration_ms = $null; total_cost_usd = $null
        models = $null; input_tokens = $null; cache_creation_input_tokens = $null; cache_read_input_tokens = $null
        output_tokens = $null; total_tokens = $null; final_context_tokens = $null }
    if (-not (Test-Path $rawPath) -or (Get-Item $rawPath).Length -eq 0) { return $r }
    try { $j = Get-Content $rawPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $r }
    if ($j -is [array]) { $j = @($j | Where-Object { (Get-Prop $_ 'type') -eq 'result' })[-1] }
    if ($null -eq $j) { return $r }

    $r.subtype = Get-Prop $j 'subtype'; $r.is_error = Get-Prop $j 'is_error'
    $r.num_turns = Get-Prop $j 'num_turns'; $r.duration_ms = Get-Prop $j 'duration_ms'
    $r.total_cost_usd = Get-Prop $j 'total_cost_usd'

    $in = @(); $cc = @(); $cr = @(); $out = @()
    $mu = Get-Prop $j 'modelUsage'
    if ($mu -and @($mu.PSObject.Properties).Count -gt 0) {
        $r.models = (@($mu.PSObject.Properties | ForEach-Object { $_.Name }) -join ';')
        foreach ($m in $mu.PSObject.Properties) {
            $in += Get-Prop $m.Value 'inputTokens'; $out += Get-Prop $m.Value 'outputTokens'
            $cr += Get-Prop $m.Value 'cacheReadInputTokens'; $cc += Get-Prop $m.Value 'cacheCreationInputTokens'
        }
    } else {
        $u = Get-Prop $j 'usage'
        $in = @(Get-Prop $u 'input_tokens'); $out = @(Get-Prop $u 'output_tokens')
        $cr = @(Get-Prop $u 'cache_read_input_tokens'); $cc = @(Get-Prop $u 'cache_creation_input_tokens')
    }
    $r.input_tokens = Sum-Cell $in; $r.cache_creation_input_tokens = Sum-Cell $cc
    $r.cache_read_input_tokens = Sum-Cell $cr; $r.output_tokens = Sum-Cell $out
    $r.total_tokens = Sum-Cell @($r.input_tokens, $r.cache_creation_input_tokens, $r.cache_read_input_tokens, $r.output_tokens)

    # usage.iterations holds only the final API call, so its last element is the final context size.
    $iters = Get-Prop (Get-Prop $j 'usage') 'iterations'
    if ($iters) {
        $last = @($iters)[-1]
        $r.final_context_tokens = Sum-Cell @((Get-Prop $last 'input_tokens'), (Get-Prop $last 'cache_read_input_tokens'), (Get-Prop $last 'cache_creation_input_tokens'))
    }
    return $r
}

function Test-WorkspaceBuild([string]$ws) {
    $b = Invoke-Native -FilePath 'dotnet' -WorkingDirectory $ws -ArgumentList @('build', '-c', 'Release', '--nologo')
    $text = $b.Stdout + "`n" + $b.Stderr
    $errs = $null
    if ($text -match '(\d+)\s+Error\(s\)') { $errs = [int]$Matches[1] }
    else { $errs = @(($text -split "`n") | Where-Object { $_ -match ' error ' }).Count }
    return @{ Pass = ($b.ExitCode -eq 0 -and $errs -eq 0); Errors = $errs }
}

function Show-Args([string[]]$list) {
    # Print the prompt (always element 1) abbreviated; everything else verbatim.
    $shown = @()
    for ($i = 0; $i -lt $list.Count; $i++) {
        if ($i -eq 1) { $shown += ('"' + $list[$i].Substring(0, [math]::Min(60, $list[$i].Length)).Replace("`n", ' ') + '..."') }
        else { $shown += $list[$i] }
    }
    Write-Host ('  claude ' + ($shown -join ' '))
}

$taskFiles = @()
foreach ($t in $Tasks) {
    $m = Get-ChildItem (Join-Path $PSScriptRoot 'tasks') -Filter "$t*.md"
    if (-not $m) { throw "No task matches '$t'" }
    $taskFiles += $m
}

foreach ($tf in $taskFiles) {
    $task = [IO.Path]::GetFileNameWithoutExtension($tf.Name)
    $prompt = [IO.File]::ReadAllText($tf.FullName).Trim()
    foreach ($arm in $Arms) {
        $withSkill = ($arm -eq 'betta' -and $SkillMode -eq 'skill')
        $armLabel = $arm
        if ($arm -eq 'betta' -and $SkillMode -eq 'none') { $armLabel = 'betta-noskill' }
        for ($n = 1; $n -le $Runs; $n++) {
            $name = "$task-$armLabel-$n"
            $ws = Join-Path $wsRoot $name
            if (Test-Path $ws) { Remove-Item -Recurse -Force $ws }
            New-Item -ItemType Directory -Force -Path $ws | Out-Null
            Copy-Item -Path (Join-Path $PSScriptRoot "scaffolds\$arm\*") -Destination $ws -Recurse -Force

            if ($withSkill) {
                $skillDir = Join-Path $ws '.claude\skills\betta-plugin'
                New-Item -ItemType Directory -Force -Path $skillDir | Out-Null
                # 0.8.0 is not on nuget.org yet; an agent copying the skill's snippets would break restore.
                $txt = [IO.File]::ReadAllText($skillSrc).Replace('Version="0.8.0"', 'Version="0.7.2.1"')
                [IO.File]::WriteAllText((Join-Path $skillDir 'SKILL.md'), $txt, (New-Object Text.UTF8Encoding($false)))
            }

            $claudeArgs = @('-p', $prompt, '--output-format', 'json', '--model', $Model,
                '--setting-sources', 'project,local', '--strict-mcp-config', '--mcp-config', $mcpConfig,
                '--no-session-persistence', '--permission-mode', 'acceptEdits',
                '--allowedTools') + $allowedTools + @(
                '--max-turns', "$MaxTurns", '--max-budget-usd', "$MaxBudgetUsd") + $ExtraArgs

            Write-Host "[$name] workspace: $ws"
            $rawPath = Join-Path $OutDir "raw\$name.json"
            $exit = $null
            if ($DryRun) {
                Show-Args $claudeArgs
            } else {
                # A failed or over-budget run is data; keep going.
                try {
                    $res = Invoke-Native -FilePath 'claude' -ArgumentList $claudeArgs -WorkingDirectory $ws -TimeoutMinutes $TimeoutMinutes
                    $exit = $res.ExitCode
                    if ($res.TimedOut) { $exit = -999 }
                    [IO.File]::WriteAllText($rawPath, $res.Stdout, (New-Object Text.UTF8Encoding($false)))
                    [IO.File]::WriteAllText((Join-Path $OutDir "raw\$name.err.txt"), $res.Stderr, (New-Object Text.UTF8Encoding($false)))
                } catch {
                    $exit = -1
                    [IO.File]::WriteAllText((Join-Path $OutDir "raw\$name.err.txt"), $_.Exception.Message, (New-Object Text.UTF8Encoding($false)))
                }
            }

            $r = Read-ClaudeResult $rawPath
            $build = Test-WorkspaceBuild $ws
            $m = Measure-AddedCode -Scaffold (Join-Path $PSScriptRoot "scaffolds\$arm") -Workspace $ws

            $row = [pscustomobject][ordered]@{
                task = $task; arm = $armLabel; run = $n; exit_code = $exit
                subtype = $r.subtype; is_error = $r.is_error; num_turns = $r.num_turns; duration_ms = $r.duration_ms
                total_cost_usd = $r.total_cost_usd; models = $r.models
                input_tokens = $r.input_tokens; cache_creation_input_tokens = $r.cache_creation_input_tokens
                cache_read_input_tokens = $r.cache_read_input_tokens; output_tokens = $r.output_tokens
                total_tokens = $r.total_tokens; final_context_tokens = $r.final_context_tokens
                build_pass = $build.Pass; build_errors = $build.Errors
                files_changed = $m.files_changed; added_lines = $m.added_lines
                added_chars = $m.added_chars; added_est_tokens = $m.added_est_tokens
            }
            $row | Export-Csv -Path $csvPath -Append -NoTypeInformation -Encoding UTF8
            Write-Host ("  exit={0} build_pass={1} errors={2} added_lines={3} total_tokens={4}" -f $exit, $build.Pass, $build.Errors, $m.added_lines, $r.total_tokens)
        }
    }
}

& (Join-Path $PSScriptRoot 'report.ps1') -OutDir $OutDir
Write-Host "Results: $OutDir"
