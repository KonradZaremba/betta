<#
.SYNOPSIS
  Token/cost benchmark: a headless Claude Code agent adds the same GH component to a Betta project and to a
  plain GH_Component project. See README.md. Real runs bill your Claude usage; use -DryRun first.
#>
param(
    [string[]]$Tasks = @('t1-remap', 't2-divide', 't3-async', 't4-multi', 't5-refactor', 't6-shelfpack', 't7-panels'),   # full names or prefixes (t1, t2, ...)
    [string[]]$Arms = @('betta', 'betta-noskill', 'vanilla'),   # betta = scaffold + skill; betta-noskill = scaffold only; vanilla = plain GH_Component; more in $knownArms
    [int]$Runs = 3,
    [string]$Model = 'sonnet',
    [int]$MaxTurns = 40,
    [double]$MaxBudgetUsd = 3,
    [string]$OutDir,
    [string[]]$ExtraArgs = @(),
    [int]$TimeoutMinutes = 45,
    # Betta.Abstractions version for the betta scaffold, e.g. a local prerelease with an unpublished API. It must already
    # be in the NuGet global packages folder; the harness adds no package source.
    [string]$AbstractionsVersion,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
# `powershell -File run.ps1 -Arms a,b` passes "a,b" as one string; split it so both invocation styles work.
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Tasks = @($Tasks | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
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

# The betta scaffold's post-build step copies BenchPlugin.dll into the real %AppData%\Grasshopper\Libraries\Betta.
# MSBuild imports the nearest ancestor Directory.Build.targets after the project body, so this blanks it for
# every workspace without changing the scaffold the agent reads.
New-Item -ItemType Directory -Force -Path $wsRoot | Out-Null
[IO.File]::WriteAllText((Join-Path $wsRoot 'Directory.Build.targets'),
    "<Project>`n  <PropertyGroup>`n    <PostBuildEvent></PostBuildEvent>`n  </PropertyGroup>`n</Project>`n", (New-Object Text.UTF8Encoding($false)))

$skillSrc = Join-Path $repoRoot '.claude\skills\betta-plugin'
# betta-guide / betta-guide-skill add the agent guide the dotnet-new templates ship (CLAUDE.md), without / with the skill.
# betta-guide-mcp adds the guide plus the Betta.Mcp server, pointed at a Libraries folder holding the task's published build.
$knownArms = @('betta', 'betta-noskill', 'betta-guide', 'betta-guide-skill', 'betta-guide-mcp', 'vanilla')
$guideSrc = Join-Path $repoRoot 'templates\Betta.Templates\content\bettaplugin-basic\CLAUDE.md'
foreach ($a in $Arms) { if ($knownArms -notcontains $a) { throw "Unknown arm '$a'. Known arms: $($knownArms -join ', ')" } }
function Get-ScaffoldName([string]$arm) { if ($arm -eq 'vanilla') { return 'vanilla' } else { return 'betta' } }
# Read-only: ties the run to the skill version it used.
$gitCommit = $null
try {
    $g = Invoke-Native -FilePath 'git' -WorkingDirectory $repoRoot -ArgumentList @('rev-parse', '--short', 'HEAD')
    if ($g.ExitCode -eq 0) { $gitCommit = $g.Stdout.Trim() }
} catch { }
$csvPath = Join-Path $OutDir 'runs.csv'
# Same tools for both arms. The build is allowed through either shell tool, plus read-only commands an
# agent pipes build output into, so a denied `dotnet build | tail` doesn't burn turns unevenly.
$allowedTools = @('Read', 'Edit', 'Write', 'Glob', 'Grep',
    'Bash(dotnet build:*)', 'Bash(dotnet restore:*)', 'PowerShell(dotnet build:*)', 'PowerShell(dotnet restore:*)',
    'Bash(ls:*)', 'Bash(cat:*)', 'Bash(head:*)', 'Bash(tail:*)', 'Bash(grep:*)', 'Bash(find:*)')
$isolationFlags = '--setting-sources project,local --strict-mcp-config --mcp-config <empty> --no-session-persistence --permission-mode acceptEdits --allowedTools ' + ($allowedTools -join ' ')

# Betta.Mcp built from this checkout: the betta-guide-mcp arm's server, and the GUID check below.
$mcpDir = Join-Path $wsRoot '_mcp'
$mb = Invoke-Native -FilePath 'dotnet' -WorkingDirectory $repoRoot -ArgumentList @('build', 'Betta.Mcp\Betta.Mcp.csproj', '-c', 'Release', '-o', $mcpDir, '--nologo')
if ($mb.ExitCode -ne 0) { throw "Betta.Mcp build failed:`n$($mb.Stdout)" }
$mcpExe = Join-Path $mcpDir 'betta-mcp.exe'
# PowerShell names are case-insensitive, so these must not be called $abstractionsVersion (that is the parameter).
$scaffoldAbstractionsVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'scaffolds\betta\BenchPlugin.csproj')),
    'Include="Betta.Abstractions" Version="([^"]+)"').Groups[1].Value
$usedAbstractionsVersion = if ($AbstractionsVersion) { $AbstractionsVersion } else { $scaffoldAbstractionsVersion }
# Rewrites the betta scaffold's Betta.Abstractions version in a copied project (base dirs and workspaces alike,
# so it never counts as code the agent added).
function Set-AbstractionsVersion([string]$dir) {
    if ($usedAbstractionsVersion -eq $scaffoldAbstractionsVersion) { return }
    $proj = Join-Path $dir 'BenchPlugin.csproj'
    $txt = [IO.File]::ReadAllText($proj)
    $new = $txt.Replace("Include=""Betta.Abstractions"" Version=""$scaffoldAbstractionsVersion""", "Include=""Betta.Abstractions"" Version=""$usedAbstractionsVersion""")
    if ($new -eq $txt -and $txt -match 'Include="Betta.Abstractions"') { throw "Could not set Betta.Abstractions $usedAbstractionsVersion in $proj" }
    [IO.File]::WriteAllText($proj, $new, (New-Object Text.UTF8Encoding($false)))
}
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$abstractionsDll = Join-Path $nugetRoot "betta.abstractions\$usedAbstractionsVersion\lib\netstandard2.0\Betta.Abstractions.dll"
# Tasks that change an existing component: its display name. The run records the GUID Betta publishes for it
# after the agent's change and whether it still equals the published one (saved .gh files keep working).
$guidCheck = @{ 't5-refactor' = 'Remap' }

$meta = [ordered]@{
    date = (Get-Date).ToString('yyyy-MM-dd HH:mm'); model = $Model; runs = $Runs; max_turns = $MaxTurns
    max_budget_usd = $MaxBudgetUsd; arms = $Arms; git_commit = $gitCommit; dry_run = [bool]$DryRun
    extra_args = $ExtraArgs; isolation_flags = $isolationFlags; abstractions_version = $usedAbstractionsVersion
    mcp_arm = 'betta-guide-mcp: --mcp-config with one stdio server "betta" (Betta.Mcp from this checkout, BETTA_PLUGIN_FOLDER = the task''s published build) and mcp__betta + ToolSearch added to --allowedTools'
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
    $m = Get-ChildItem (Join-Path $PSScriptRoot 'tasks') -Filter "$t*.md" -File
    if (-not $m) { throw "No task matches '$t'" }
    $taskFiles += $m
}
# Base dir per (task, scaffold) = bare scaffold + the task's seed files. Measurement diffs against it, so
# an untouched seeded workspace reports 0 added lines.
$baseDirs = @{}
function Get-BaseDir([string]$task, [string]$scaffold) {
    $key = "$task|$scaffold"
    if ($baseDirs.ContainsKey($key)) { return $baseDirs[$key] }
    $dir = Join-Path $wsRoot "_base\$task-$scaffold"
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    Copy-Clean (Join-Path $PSScriptRoot "scaffolds\$scaffold") $dir
    $seed = Join-Path $PSScriptRoot "tasks\$task.seed\$scaffold"
    if (Test-Path $seed) { Copy-Item -Path (Join-Path $seed '*') -Destination $dir -Recurse -Force }
    Set-AbstractionsVersion $dir
    $baseDirs[$key] = $dir
    return $dir
}
# The task's starting code built once (Release): what a user already has deployed and saved files refer to.
$published = @{}
function Get-PublishedBuild([string]$task) {
    if ($published.ContainsKey($task)) { return $published[$task] }
    $dir = Join-Path $wsRoot "_published\$task"
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    Copy-Clean (Get-BaseDir $task 'betta') $dir
    $b = Invoke-Native -FilePath 'dotnet' -WorkingDirectory $dir -ArgumentList @('build', '-c', 'Release', '--nologo')
    if ($b.ExitCode -ne 0) { throw "Published build of $task failed:`n$($b.Stdout)" }
    $dll = Join-Path $dir 'bin\Release\net7.0-windows\BenchPlugin.dll'
    $guid = $null
    if ($guidCheck.ContainsKey($task)) {
        $guid = Get-BettaComponentGuid -McpExe $mcpExe -Component $guidCheck[$task] `
            -PluginFolder (New-BettaLibraries -Dir (Join-Path $wsRoot "_published\$task-libs") -PluginDll $dll -AbstractionsDll $abstractionsDll)
        if (-not $guid) { throw "betta_describe found no '$($guidCheck[$task])' in the published build of $task" }
    }
    $published[$task] = @{ Dll = $dll; Guid = $guid }
    return $published[$task]
}

foreach ($tf in $taskFiles) {
    $task = [IO.Path]::GetFileNameWithoutExtension($tf.Name)
    $prompt = [IO.File]::ReadAllText($tf.FullName).Trim()
    foreach ($arm in $Arms) {
        $scaffold = Get-ScaffoldName $arm
        $withSkill = ($arm -eq 'betta' -or $arm -eq 'betta-guide-skill')
        $withGuide = ($arm -like 'betta-guide*')
        $withMcp = ($arm -eq 'betta-guide-mcp')
        $baseDir = Get-BaseDir $task $scaffold
        $pub = $null
        if ($scaffold -eq 'betta' -and ($withMcp -or $guidCheck.ContainsKey($task))) { $pub = Get-PublishedBuild $task }
        $seed = Join-Path $PSScriptRoot "tasks\$task.seed\$scaffold"
        for ($n = 1; $n -le $Runs; $n++) {
            $name = "$task-$arm-$n"
            $ws = Join-Path $wsRoot $name
            if (Test-Path $ws) { Remove-Item -Recurse -Force $ws }
            New-Item -ItemType Directory -Force -Path $ws | Out-Null
            Copy-Item -Path (Join-Path $PSScriptRoot "scaffolds\$scaffold\*") -Destination $ws -Recurse -Force
            if (Test-Path $seed) { Copy-Item -Path (Join-Path $seed '*') -Destination $ws -Recurse -Force }
            Set-AbstractionsVersion $ws
            # measure.ps1 excludes CLAUDE.md, so the guide never counts as code the agent added.
            if ($withGuide) { Copy-Item -Path $guideSrc -Destination (Join-Path $ws 'CLAUDE.md') -Force }

            if ($withSkill) {
                $skillDir = Join-Path $ws '.claude\skills\betta-plugin'
                New-Item -ItemType Directory -Force -Path $skillDir | Out-Null
                Copy-Item -Path (Join-Path $skillSrc '*') -Destination $skillDir -Recurse -Force
                # 0.8.0 is not on nuget.org yet; an agent copying the skill's snippets would break restore.
                foreach ($md in (Get-ChildItem $skillDir -Recurse -Filter *.md -File)) {
                    $txt = [IO.File]::ReadAllText($md.FullName).Replace('Version="0.8.0"', 'Version="0.7.2.1"')
                    [IO.File]::WriteAllText($md.FullName, $txt, (New-Object Text.UTF8Encoding($false)))
                }
            }

            $runMcpConfig = $mcpConfig
            $runTools = $allowedTools
            if ($withMcp) {
                # Outside the workspace, so it is neither project content nor counted as added code.
                $libs = New-BettaLibraries -Dir (Join-Path $wsRoot "_libs\$name") -PluginDll $pub.Dll -AbstractionsDll $abstractionsDll
                $runMcpConfig = Join-Path $wsRoot "_libs\$name.mcp.json"
                $cfg = @{ mcpServers = @{ betta = @{ type = 'stdio'; command = $mcpExe; args = @(); env = @{ BETTA_PLUGIN_FOLDER = $libs } } } }
                [IO.File]::WriteAllText($runMcpConfig, ($cfg | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
                $runTools = $allowedTools + @('mcp__betta', 'ToolSearch')   # MCP tool schemas are deferred behind ToolSearch
            }

            $claudeArgs = @('-p', $prompt, '--output-format', 'json', '--model', $Model,
                '--setting-sources', 'project,local', '--strict-mcp-config', '--mcp-config', $runMcpConfig,
                '--no-session-persistence', '--permission-mode', 'acceptEdits',
                '--allowedTools') + $runTools + @(
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
            $m = Measure-AddedCode -Scaffold $baseDir -Workspace $ws
            $guid = $null; $guidKept = $null
            $builtDll = Join-Path $ws 'bin\Release\net7.0-windows\BenchPlugin.dll'
            if ($pub -and $pub.Guid -and $build.Pass -and (Test-Path $builtDll)) {
                $guid = Get-BettaComponentGuid -McpExe $mcpExe -Component $guidCheck[$task] `
                    -PluginFolder (New-BettaLibraries -Dir (Join-Path $wsRoot "_check\$name") -PluginDll $builtDll -AbstractionsDll $abstractionsDll)
                $guidKept = ($guid -eq $pub.Guid)
            }

            $row = [pscustomobject][ordered]@{
                task = $task; arm = $arm; run = $n; exit_code = $exit
                subtype = $r.subtype; is_error = $r.is_error; num_turns = $r.num_turns; duration_ms = $r.duration_ms
                total_cost_usd = $r.total_cost_usd; models = $r.models
                input_tokens = $r.input_tokens; cache_creation_input_tokens = $r.cache_creation_input_tokens
                cache_read_input_tokens = $r.cache_read_input_tokens; output_tokens = $r.output_tokens
                total_tokens = $r.total_tokens; final_context_tokens = $r.final_context_tokens
                build_pass = $build.Pass; build_errors = $build.Errors
                files_changed = $m.files_changed; added_lines = $m.added_lines
                added_chars = $m.added_chars; added_est_tokens = $m.added_est_tokens
                guid = $guid; guid_kept = $guidKept
            }
            $row | Export-Csv -Path $csvPath -Append -NoTypeInformation -Encoding UTF8
            Write-Host ("  exit={0} build_pass={1} errors={2} added_lines={3} total_tokens={4} guid_kept={5}" -f $exit, $build.Pass, $build.Errors, $m.added_lines, $r.total_tokens, $guidKept)
        }
    }
}

& (Join-Path $PSScriptRoot 'report.ps1') -OutDir $OutDir
Write-Host "Results: $OutDir"
