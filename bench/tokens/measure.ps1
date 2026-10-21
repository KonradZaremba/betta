<#
.SYNOPSIS
  Measures the code an agent added to a workspace (relative to its scaffold), or the size of a file/folder.

.EXAMPLE
  . .\measure.ps1; Measure-AddedCode -Scaffold .\scaffolds\betta -Workspace C:\tmp\betta-bench\x\t1-betta-1
.EXAMPLE
  .\measure.ps1 -Scaffold .\scaffolds\betta -Workspace C:\tmp\betta-bench\x\t1-betta-1
.EXAMPLE
  .\measure.ps1 -Path .\reference\betta\Remap.cs
#>
param(
    [string]$Scaffold,
    [string]$Workspace,
    [string]$Path
)

. (Join-Path $PSScriptRoot 'common.ps1')

# Token estimate is a rough chars/4 heuristic, not a tokenizer count; always label it "est.".
function Get-EstTokens([long]$Chars) { return [long][math]::Ceiling($Chars / 4.0) }

# Copies a tree without build output / agent config so they never show up in the diff.
function Copy-Clean([string]$From, [string]$To) {
    New-Item -ItemType Directory -Force -Path $To | Out-Null
    & robocopy $From $To /E /XD bin obj .claude .git /XF CLAUDE.md AGENTS.md /NFL /NDL /NJH /NJS /NP | Out-Null
    # robocopy exit codes below 8 mean success
    $global:LASTEXITCODE = 0
}

function Measure-AddedCode {
    param([Parameter(Mandatory)][string]$Scaffold, [Parameter(Mandatory)][string]$Workspace)

    $stage = Join-Path ([IO.Path]::GetTempPath()) ('betta-measure-' + [guid]::NewGuid().ToString('N'))
    try {
        Copy-Clean (Resolve-Path $Scaffold).Path (Join-Path $stage 'a')
        Copy-Clean (Resolve-Path $Workspace).Path (Join-Path $stage 'b')

        $common = @('-c', 'core.autocrlf=false', '-c', 'core.safecrlf=false', '-c', 'core.quotepath=off',
                    'diff', '--no-index', '--no-ext-diff')
        # git exits 1 when the trees differ; that is the normal case here.
        $names = Invoke-Native -FilePath 'git' -WorkingDirectory $stage -ArgumentList ($common + @('--name-status', 'a', 'b'))
        $patch = Invoke-Native -FilePath 'git' -WorkingDirectory $stage -ArgumentList ($common + @('-U0', 'a', 'b'))

        $files = 0
        foreach ($l in ($names.Stdout -split "`n")) { if ($l.Trim().Length -gt 0) { $files++ } }

        $lines = 0; $chars = 0L
        foreach ($l in ($patch.Stdout -split "`n")) {
            $l = $l.TrimEnd("`r")
            if ($l.StartsWith('+') -and -not $l.StartsWith('+++')) {
                $lines++
                $chars += $l.Length   # '+' marker stands in for the newline
            }
        }
        return [pscustomobject]@{
            files_changed    = $files
            added_lines      = $lines
            added_chars      = $chars
            added_est_tokens = (Get-EstTokens $chars)
        }
    } finally {
        if (Test-Path $stage) { Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue }
    }
}

# Size of a single file, or of all *.cs files in a folder. chars include one newline per line.
function Measure-Path {
    param([Parameter(Mandatory)][string]$Path)
    $item = Get-Item $Path
    if ($item.PSIsContainer) { $targets = Get-ChildItem $Path -Recurse -Filter *.cs -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } }
    else { $targets = @($item) }
    $lines = 0; $chars = 0L
    foreach ($f in $targets) {
        $text = [IO.File]::ReadAllText($f.FullName) -replace "`r`n", "`n"
        $text = $text.TrimEnd("`n")
        $arr = $text -split "`n"
        $lines += $arr.Count
        $chars += $text.Length + $arr.Count
    }
    return [pscustomobject]@{
        files            = @($targets).Count
        lines            = $lines
        chars            = $chars
        est_tokens       = (Get-EstTokens $chars)
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    if ($Path) { Measure-Path -Path $Path | Format-List }
    elseif ($Scaffold -and $Workspace) { Measure-AddedCode -Scaffold $Scaffold -Workspace $Workspace | Format-List }
    elseif ($PSBoundParameters.Count -gt 0 -or $args.Count -gt 0) { Write-Error 'Use -Path, or -Scaffold with -Workspace.' }
}
