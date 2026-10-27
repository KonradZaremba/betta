<#
.SYNOPSIS
  Scores t9-iterate: five maintenance changes to the seeded facade library. Loads every build and both seeded bases
  with inspect/Inspect.exe (headless, needs a Rhino 8 install, not a running Rhino) and checks what a headless load
  can see: every facade component still publishes the GUID saved files use, Panel Stats has the "Min Area" input,
  Planarity the "Max Deviation" output, Classify Panels outputs "Class" instead of "Type", and Group Identical Panels
  runs as a task (a check of the code). The Tolerance errors and the progress display are read from the diffs.
  Writes <OutDir>\inspect\<run>.json and <OutDir>\iterate-scores.csv.
#>
param(
    [Parameter(Mandatory)][string]$OutDir,
    [string]$WorkspaceRoot
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$OutDir = (Resolve-Path $OutDir).Path
if (-not $WorkspaceRoot) { $WorkspaceRoot = Join-Path 'C:\tmp\betta-bench' (Split-Path $OutDir -Leaf) }
$inspect = Join-Path $PSScriptRoot 'inspect\bin\Release\net7.0-windows\win-x64\Inspect.exe'
if (-not (Test-Path $inspect)) { throw "Build the checker first: dotnet build $PSScriptRoot\inspect -c Release" }
$inspectDir = Join-Path $OutDir 'inspect'
New-Item -ItemType Directory -Force -Path $inspectDir | Out-Null
$task = 't9-iterate'

function Norm([string]$s) { ($s -replace '[\s_\-]', '').ToLowerInvariant() }
function Find($comps, [string]$name) { @($comps) | Where-Object { (Norm $_.name) -eq (Norm $name) } | Select-Object -First 1 }
function Has($params, [string]$name) { [bool](@($params) | Where-Object { (Norm $_.name) -eq (Norm $name) }) }

function Get-Components([string]$dir, [string]$name) {
    $bin = Join-Path $dir 'bin\Release\net7.0-windows'
    $dll = @('BenchPlugin.gha', 'BenchPlugin.dll') | ForEach-Object { Join-Path $bin $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $dll) { return @() }
    $json = Join-Path $inspectDir "$name.json"
    & $inspect $dll $json | Out-Null
    if (-not (Test-Path $json)) { return @() }
    @((Get-Content $json -Raw | ConvertFrom-Json).components | Where-Object { -not $_.error -and $_.category -eq 'Facade' })
}

# The seeded starting point, built once per scaffold: the GUIDs that saved files refer to.
$baseGuids = @{}
foreach ($scaffold in 'betta', 'vanilla') {
    $dir = Join-Path $WorkspaceRoot "_base\$task-$scaffold"
    if (-not (Test-Path $dir)) { continue }
    $b = & dotnet build $dir -c Release --nologo 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Base build failed for ${scaffold}:`n$($b -join "`n")" }
    $map = @{}
    foreach ($c in Get-Components $dir "_base-$task-$scaffold") { $map[(Norm $c.name)] = "$($c.guid)" }
    $baseGuids[$scaffold] = $map
}

$runs = Import-Csv (Join-Path $OutDir 'runs.csv') | Where-Object { $_.task -eq $task }
$rows = foreach ($r in $runs) {
    $name = "$($r.task)-$($r.arm)-$($r.run)"
    $ws = Join-Path $WorkspaceRoot $name
    $scaffold = if ($r.arm -eq 'vanilla') { 'vanilla' } else { 'betta' }
    $comps = Get-Components $ws $name
    $base = $baseGuids[$scaffold]

    $kept = 0
    foreach ($k in $base.Keys) {
        $c = @($comps) | Where-Object { (Norm $_.name) -eq $k } | Select-Object -First 1
        if ($c -and "$($c.guid)" -eq $base[$k]) { $kept++ }
    }
    $minArea = Has (Find $comps 'Panel Stats').inputs 'Min Area'
    $maxDev = Has (Find $comps 'Planarity').outputs 'Max Deviation'
    $cls = Find $comps 'Classify Panels'
    $renamed = (Has $cls.outputs 'Class') -and -not (Has $cls.outputs 'Type')

    # Background grouping: a Task-returning method (Betta), or a task in the component class (by hand).
    $src = (Get-ChildItem $ws -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    if ($scaffold -eq 'betta') {
        $async = $src -match 'Task<[^;{]*GroupIdenticalPanels\s*\('
    } else {
        $i = $src.IndexOf('"Group Identical Panels"')
        $async = $false
        if ($i -ge 0) {
            $start = [math]::Max(0, $src.LastIndexOf('class ', $i))
            $end = $src.IndexOf('base("', $i + 10); if ($end -lt 0) { $end = $src.Length }
            $async = $src.Substring($start, $end - $start) -match 'Task\.Run|TaskCapable|Task\.Factory|\basync\b'
        }
    }
    $checks = @($minArea, $maxDev, $renamed, $async) | Where-Object { $_ }

    [pscustomobject][ordered]@{
        run = $name; arm = $r.arm; build_pass = $r.build_pass
        cost_usd = [math]::Round([double]$r.total_cost_usd, 3); turns = $r.num_turns; added_lines = $r.added_lines
        guids_kept = "$kept/$($base.Count)"
        min_area_input = $minArea; max_deviation_output = $maxDev; type_renamed_class = $renamed; group_runs_as_task = $async
        checks = "$($checks.Count)/4"
    }
}
$rows | Export-Csv (Join-Path $OutDir 'iterate-scores.csv') -NoTypeInformation -Encoding UTF8
$rows | Format-Table -AutoSize | Out-String -Width 240
