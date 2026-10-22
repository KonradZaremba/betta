<#
.SYNOPSIS
  Scores each library built by the u1-facade use case against its brief: which of the 8 components exist, in the
  right category and subcategory, with the inputs and outputs the brief names, and whether panels travel on a typed
  "Panel" wire. Loads each build with inspect/Inspect.exe (headless, needs a Rhino 8 install, not a running Rhino).
  Writes <OutDir>\inspect\<run>.json and <OutDir>\usecase-scores.csv.
#>
param(
    [Parameter(Mandatory)][string]$OutDir,
    [string]$WorkspaceRoot
)
$ErrorActionPreference = 'Stop'
$OutDir = (Resolve-Path $OutDir).Path
if (-not $WorkspaceRoot) { $WorkspaceRoot = Join-Path 'C:\tmp\betta-bench' (Split-Path $OutDir -Leaf) }
$inspect = Join-Path $PSScriptRoot 'inspect\bin\Release\net7.0-windows\win-x64\Inspect.exe'
if (-not (Test-Path $inspect)) { throw "Build the checker first: dotnet build $PSScriptRoot\inspect -c Release" }

# The brief (tasks/u1-facade.md): component -> subcategory, inputs, outputs. Params named here that carry panels
# must be typed "Panel" wires.
$spec = [ordered]@{
    'Panelize Surface'       = @{ Sub = 'Panels';      In = @('Surface', 'U Count', 'V Count');                    Out = @('Panels', 'Outlines') }
    'Panel Info'             = @{ Sub = 'Panels';      In = @('Panel');                                           Out = @('Corners', 'Outline', 'Area', 'Center', 'Index') }
    'Planarity'              = @{ Sub = 'Analysis';    In = @('Panels', 'Tolerance');                              Out = @('Deviation', 'Flat', 'Flat Count') }
    'Classify Panels'        = @{ Sub = 'Analysis';    In = @('Panels', 'Tolerance');                              Out = @('Type', 'Flat Count', 'Cold Bent Count', 'Custom Count') }
    'Panel Stats'            = @{ Sub = 'Analysis';    In = @('Panels');                                           Out = @('Total Area', 'Min Area', 'Max Area', 'Mean Area') }
    'Group Identical Panels' = @{ Sub = 'Fabrication'; In = @('Panels', 'Tolerance');                              Out = @('Group', 'Group Count', 'Representatives') }
    'Nest On Sheets'         = @{ Sub = 'Fabrication'; In = @('Panels', 'Sheet Width', 'Sheet Height', 'Spacing'); Out = @('Outlines', 'Sheet', 'Sheet Count', 'Utilization') }
    'Cut List'               = @{ Sub = 'Fabrication'; In = @('Panels', 'Path', 'Write');                          Out = @('CSV', 'Written') }
}
$panelParams = @('Panels', 'Panel', 'Representatives')

function Norm([string]$s) { ($s -replace '[\s_\-]', '').ToLowerInvariant() }

$runs = Import-Csv (Join-Path $OutDir 'runs.csv')
$inspectDir = Join-Path $OutDir 'inspect'
New-Item -ItemType Directory -Force -Path $inspectDir | Out-Null
$rows = foreach ($r in $runs) {
    $name = "$($r.task)-$($r.arm)-$($r.run)"
    $bin = Join-Path $WorkspaceRoot "$name\bin\Release\net7.0-windows"
    $dll = @('BenchPlugin.gha', 'BenchPlugin.dll') | ForEach-Object { Join-Path $bin $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
    $json = Join-Path $inspectDir "$name.json"
    $comps = @()
    if ($dll) {
        & $inspect $dll $json | Out-Null
        if (Test-Path $json) { $comps = @((Get-Content $json -Raw | ConvertFrom-Json).components) }
    }

    $points = 0; $exact = 0; $total = 0; $found = 0; $placed = 0; $typed = 0; $typedTotal = 0; $errors = 0
    foreach ($key in $spec.Keys) {
        $s = $spec[$key]
        $c = $comps | Where-Object { -not $_.error -and (Norm $_.name) -eq (Norm $key) } | Select-Object -First 1
        $items = $s.In.Count + $s.Out.Count
        $total += 1 + $items
        if (-not $c) { $typedTotal += @($s.In + $s.Out | Where-Object { $panelParams -contains $_ }).Count; continue }
        $found++; $points++; $exact++
        if ($c.category -eq 'Facade' -and (Norm $c.subCategory) -eq (Norm $s.Sub)) { $placed++ }
        foreach ($pair in @(@($s.In, $c.inputs), @($s.Out, $c.outputs))) {
            foreach ($want in $pair[0]) {
                $p = @($pair[1]) | Where-Object { (Norm $_.name) -eq (Norm $want) } | Select-Object -First 1
                # A Betta [GrasshopperTrigger] bool is a "Run" menu item, not a pin: it still covers Write.
                if (-not $p -and $want -eq 'Write' -and $c.extra.trigger) { $points++; $exact++; continue }
                if ($p) { $points++; if ($p.name -ceq $want) { $exact++ } }
                if ($panelParams -contains $want) { $typedTotal++; if ($p -and $p.typeName -match 'Panel') { $typed++ } }
            }
        }
    }
    $errors = @($comps | Where-Object { $_.error }).Count
    [pscustomobject][ordered]@{
        run = $name; arm = $r.arm; build_pass = $r.build_pass
        components_found = "$found/$($spec.Count)"; placed_right = "$placed/$($spec.Count)"
        conformance = [math]::Round($points / $total, 3); exact_names = [math]::Round($exact / $total, 3)
        panel_wires_typed = "$typed/$typedTotal"
        components_total = $comps.Count; instantiate_errors = $errors
    }
}
$rows | Export-Csv (Join-Path $OutDir 'usecase-scores.csv') -NoTypeInformation -Encoding UTF8
$rows | Format-Table -AutoSize | Out-String -Width 220
