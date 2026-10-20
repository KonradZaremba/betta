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

# Runs a native exe with an empty, immediately-closed stdin (claude -p otherwise waits 3 s for stdin
# data), capturing stdout and stderr separately as UTF-8. Returns @{ ExitCode; Stdout; Stderr; TimedOut }.
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory = (Get-Location).Path,
        [int]$TimeoutMinutes = 0
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

    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    [void]$p.Start()
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
