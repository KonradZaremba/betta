<#
.SYNOPSIS
  Reads runs.csv from -OutDir and writes report.md next to it.
#>
param([Parameter(Mandatory)][string]$OutDir)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'measure.ps1')

$csv = Join-Path $OutDir 'runs.csv'
$rows = @(Import-Csv $csv)
$meta = $null
$metaPath = Join-Path $OutDir 'meta.json'
if (Test-Path $metaPath) { $meta = Get-Content $metaPath -Raw | ConvertFrom-Json }

function Get-Nums($set, [string]$col) {
    return @($set | ForEach-Object { $_.$col } | Where-Object { $_ -ne $null -and $_ -ne '' } | ForEach-Object { [double]$_ })
}
function Get-Median([double[]]$v) {
    if ($v.Count -eq 0) { return $null }
    $s = @($v | Sort-Object)
    $mid = [int][math]::Floor($s.Count / 2)
    if ($s.Count % 2 -eq 1) { return $s[$mid] }
    return ($s[$mid - 1] + $s[$mid]) / 2
}
function Format-Num($x, [string]$fmt) {
    if ($null -eq $x) { return 'n/a' }
    return ([double]$x).ToString($fmt)
}
# "median (min-max)"
function Format-Stat($set, [string]$col, [string]$fmt, [double]$scale = 1) {
    $v = @(Get-Nums $set $col | ForEach-Object { $_ / $scale })
    if ($v.Count -eq 0) { return 'n/a' }
    $mn = ($v | Measure-Object -Minimum).Minimum; $mx = ($v | Measure-Object -Maximum).Maximum
    return ('{0} ({1}-{2})' -f (Format-Num (Get-Median $v) $fmt), (Format-Num $mn $fmt), (Format-Num $mx $fmt))
}
function Get-Med($set, [string]$col, [double]$scale = 1) {
    $v = @(Get-Nums $set $col | ForEach-Object { $_ / $scale })
    return (Get-Median $v)
}
function Format-Ratio($a, $b) {
    if ($null -eq $a -or $null -eq $b -or $b -eq 0) { return 'n/a' }
    return (($a / $b).ToString('0.00') + 'x')
}

$sb = New-Object System.Text.StringBuilder
function Add([string]$s = '') { [void]$sb.AppendLine($s) }

Add '# Token benchmark report'
Add ''
$models = (@($rows | ForEach-Object { $_.models } | Where-Object { $_ } | Select-Object -Unique) -join ', ')
if (-not $models) { $models = 'n/a' }
$runsPer = (@($rows | Group-Object task, arm | ForEach-Object { $_.Count } | Measure-Object -Maximum).Maximum)
Add ('- Date: {0}' -f $(if ($meta) { $meta.date } else { (Get-Date).ToString('yyyy-MM-dd HH:mm') }))
Add ('- Model requested: {0} (models seen in results: {1})' -f $(if ($meta) { $meta.model } else { 'n/a' }), $models)
Add ('- Runs per task x arm: {0}' -f $runsPer)
if ($meta) {
    Add ('- Max turns: {0}; max budget per run: ${1}' -f $meta.max_turns, $meta.max_budget_usd)
    if ($meta.PSObject.Properties['arms']) { Add ('- Arms: {0}' -f (@($meta.arms) -join ', ')) }
    if ($meta.PSObject.Properties['skill_mode']) { Add ('- Skill mode (betta arm): {0}' -f $meta.skill_mode) }
    if ($meta.PSObject.Properties['git_commit'] -and $meta.git_commit) { Add ('- Repo commit (skill version): {0}' -f $meta.git_commit) }
    Add ('- Isolation flags: `{0}`' -f $meta.isolation_flags)
    if ($meta.PSObject.Properties['mcp_arm'] -and @($meta.arms | Where-Object { $_ -like '*-mcp' }).Count -gt 0) { Add ('- MCP arm: {0}' -f $meta.mcp_arm) }
    if ($meta.PSObject.Properties['merged']) { Add ('- Merged rows: {0}' -f $meta.merged) }
    if ($meta.extra_args) { Add ('- Extra args: `{0}`' -f ($meta.extra_args -join ' ')) }
    if ($meta.dry_run) { Add '- **DRY RUN: no agent was run; token columns are empty.**' }
}
Add '- Token counts are summed from `modelUsage` (input + cache creation + cache read + output). `added_est_tokens` is est. = ceil(added chars / 4).'
Add ''

# Per task x arm
Add '## Per task and arm (median (min-max))'
Add ''
Add '| Task | Arm | n | total_tokens | output_tokens | cost USD | turns | duration s | final context tokens | build pass | added est. tokens | GUID kept |'
Add '|---|---|---|---|---|---|---|---|---|---|---|---|'
$tasks = @($rows | ForEach-Object { $_.task } | Select-Object -Unique)
$arms = @($rows | ForEach-Object { $_.arm } | Select-Object -Unique)
$cell = @{}
foreach ($t in $tasks) {
    foreach ($a in $arms) {
        $set = @($rows | Where-Object { $_.task -eq $t -and $_.arm -eq $a })
        if ($set.Count -eq 0) { continue }
        $cell["$t|$a"] = $set
        $pass = @($set | Where-Object { $_.build_pass -eq 'True' }).Count
        # guid_kept is only recorded for tasks that change an existing component (older runs lack the column).
        $checked = @($set | Where-Object { $_.PSObject.Properties['guid_kept'] -and $_.guid_kept -ne '' })
        $kept = if ($checked.Count -gt 0) { '{0}/{1}' -f @($checked | Where-Object { $_.guid_kept -eq 'True' }).Count, $checked.Count } else { 'n/a' }
        Add ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9}/{2} | {10} | {11} |' -f $t, $a, $set.Count,
            (Format-Stat $set 'total_tokens' '#,0'), (Format-Stat $set 'output_tokens' '#,0'),
            (Format-Stat $set 'total_cost_usd' '0.000'), (Format-Stat $set 'num_turns' '0.#'),
            (Format-Stat $set 'duration_ms' '0.0' 1000), (Format-Stat $set 'final_context_tokens' '#,0'),
            $pass, (Format-Stat $set 'added_est_tokens' '#,0'), $kept)
    }
}
Add ''

# Per-arm totals (sum of per-task medians)
Add '## Per-arm total (sum of per-task medians)'
Add ''
Add '| Arm | total_tokens | output_tokens | cost USD | turns | duration s | added est. tokens |'
Add '|---|---|---|---|---|---|---|'
$armTotals = @{}
foreach ($a in $arms) {
    $tot = @{}
    foreach ($col in 'total_tokens', 'output_tokens', 'total_cost_usd', 'num_turns', 'duration_ms', 'added_est_tokens') {
        $sum = 0.0; $any = $false
        foreach ($t in $tasks) {
            if (-not $cell.ContainsKey("$t|$a")) { continue }
            $m = Get-Med $cell["$t|$a"] $col
            if ($null -ne $m) { $sum += $m; $any = $true }
        }
        if ($any) { $tot[$col] = $sum } else { $tot[$col] = $null }
    }
    $armTotals[$a] = $tot
    $dur = $tot['duration_ms']; if ($null -ne $dur) { $dur = $dur / 1000 }
    Add ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} |' -f $a, (Format-Num $tot['total_tokens'] '#,0'), (Format-Num $tot['output_tokens'] '#,0'),
        (Format-Num $tot['total_cost_usd'] '0.000'), (Format-Num $tot['num_turns'] '0.#'), (Format-Num $dur '0.0'), (Format-Num $tot['added_est_tokens'] '#,0'))
}
Add ''

# Ratio vs vanilla (only when the run has vanilla rows, e.g. not for a guide-only batch)
if ($arms -contains 'vanilla') {
Add '## Ratio vs vanilla (median of arm / median of vanilla; below 1.00x favours the arm)'
Add ''
Add '| Task | Arm | total_tokens | output_tokens | cost USD | turns | added est. tokens |'
Add '|---|---|---|---|---|---|---|'
foreach ($t in ($tasks + 'ALL')) {
    foreach ($a in $arms) {
        if ($a -eq 'vanilla') { continue }
        $vals = @()
        foreach ($col in 'total_tokens', 'output_tokens', 'total_cost_usd', 'num_turns', 'added_est_tokens') {
            if ($t -eq 'ALL') { $x = $armTotals[$a][$col]; $y = $armTotals['vanilla'][$col] }
            else {
                $x = $null; $y = $null
                if ($cell.ContainsKey("$t|$a")) { $x = Get-Med $cell["$t|$a"] $col }
                if ($cell.ContainsKey("$t|vanilla")) { $y = Get-Med $cell["$t|vanilla"] $col }
            }
            $vals += (Format-Ratio $x $y)
        }
        Add ('| {0} | {1} | {2} |' -f $t, $a, ($vals -join ' | '))
    }
}
Add ''
}

# Skill effect: betta vs betta-noskill, per task and overall
if ($arms -contains 'betta' -and $arms -contains 'betta-noskill') {
    Add '## Skill effect: betta vs betta-noskill (median ratio; below 1.00x means the skill helps)'
    Add ''
    Add '| Task | Comparison | total_tokens | output_tokens | cost USD | turns | added est. tokens |'
    Add '|---|---|---|---|---|---|---|'
    foreach ($t in ($tasks + 'ALL')) {
        if ($t -ne 'ALL' -and (-not $cell.ContainsKey("$t|betta") -or -not $cell.ContainsKey("$t|betta-noskill"))) { continue }
        $vals = @()
        foreach ($col in 'total_tokens', 'output_tokens', 'total_cost_usd', 'num_turns', 'added_est_tokens') {
            if ($t -eq 'ALL') { $x = $armTotals['betta'][$col]; $y = $armTotals['betta-noskill'][$col] }
            else { $x = Get-Med $cell["$t|betta"] $col; $y = Get-Med $cell["$t|betta-noskill"] $col }
            $vals += (Format-Ratio $x $y)
        }
        Add ('| {0} | betta vs betta-noskill | {1} |' -f $t, ($vals -join ' | '))
    }
    Add ''
}

# Reference implementations (free code-size measurement, t1-t3 only)
Add '## Reference implementation size (hand-written, minimal, idiomatic)'
Add ''
Add 'chars include one newline per line; tokens are est. = ceil(chars / 4).'
Add ''
Add '| Component | Betta lines | Betta chars | Betta est. tokens | Vanilla lines | Vanilla chars | Vanilla est. tokens | Betta/Vanilla (est. tokens) |'
Add '|---|---|---|---|---|---|---|---|'
$tl = 0; $tc = 0L; $tt = 0; $vl = 0; $vc = 0L; $vt = 0
foreach ($c in 'Remap', 'DivideCurveStats', 'AsyncStats') {
    $b = Measure-Path (Join-Path $PSScriptRoot "reference\betta\$c.cs")
    $v = Measure-Path (Join-Path $PSScriptRoot "reference\vanilla\$c.cs")
    $tl += $b.lines; $tc += $b.chars; $tt += $b.est_tokens; $vl += $v.lines; $vc += $v.chars; $vt += $v.est_tokens
    Add ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |' -f $c, $b.lines, $b.chars, $b.est_tokens, $v.lines, $v.chars, $v.est_tokens, (Format-Ratio $b.est_tokens $v.est_tokens))
}
Add ('| **Total** | {0} | {1} | {2} | {3} | {4} | {5} | {6} |' -f $tl, $tc, $tt, $vl, $vc, $vt, (Format-Ratio $tt $vt))
Add ''

$outPath = Join-Path $OutDir 'report.md'
[IO.File]::WriteAllText($outPath, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
Write-Host "Report: $outPath"
