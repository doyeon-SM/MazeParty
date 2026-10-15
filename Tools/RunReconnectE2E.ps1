[CmdletBinding()]
param(
    [string]$Executable,
    [string]$RunDirectory,
    [switch]$HostRecovery,
    [switch]$ReconnectTimeout,
    [ValidateRange(0, 145)]
    [int]$HostReturnDelaySeconds = 0,
    [ValidateRange(120, 1200)]
    [int]$TimeoutSeconds = 420
)

$ErrorActionPreference = 'Stop'
if ($HostRecovery -and $ReconnectTimeout) { throw 'Choose one recovery scenario per run.' }
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $repoRoot 'Builds\Windows-Development\MazeParty.exe'
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($RunDirectory)) {
    $RunDirectory = Join-Path $repoRoot ("Logs\ReconnectE2E-$stamp")
}
$RunDirectory = [System.IO.Path]::GetFullPath($RunDirectory)
if (Test-Path -LiteralPath $RunDirectory) {
    if (@(Get-ChildItem -LiteralPath $RunDirectory -Force).Count -ne 0) {
        throw "Run directory must be new or empty: $RunDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $RunDirectory | Out-Null
}

$profilePrefix = "reconnect-$stamp"
$processes = @{}

function Start-ReconnectPlayer {
    param(
        [ValidateRange(0, 3)] [int]$Index,
        [ValidateSet('initial', 'resume')] [string]$Stage
    )

    $role = if ($Index -eq 0) { 'host' } else { 'client' }
    $logPath = Join-Path $RunDirectory "unity-player-$Index-$Stage.log"
    $arguments = @(
        '-e2e-reconnect',
        '-e2e-role', $role,
        '-e2e-player', $Index,
        '-e2e-run-dir', ('"' + $RunDirectory + '"'),
        '-e2e-reconnect-stage', $Stage,
        '-auth-profile', "$profilePrefix-p$Index",
        '-screen-fullscreen', '0',
        '-screen-width', '1280',
        '-screen-height', '720',
        '-logFile', ('"' + $logPath + '"')
    )
    if ($HostRecovery) { $arguments += '-e2e-host-recovery' }
    if ($ReconnectTimeout) { $arguments += '-e2e-reconnect-timeout' }
    return Start-Process -FilePath $Executable -ArgumentList $arguments `
        -PassThru -WindowStyle Hidden
}

function Assert-NoUnexpectedProcessExit {
    foreach ($index in @($processes.Keys)) {
        $process = $processes[$index]
        $passPath = Join-Path $RunDirectory "passed-$index.marker"
        if ($null -ne $process -and $process.HasExited -and
            !(Test-Path -LiteralPath $passPath)) {
            throw "Player $index process $($process.Id) exited unexpectedly with $($process.ExitCode)."
        }
    }
}

function Wait-ForFile {
    param(
        [string]$Path,
        [datetime]$Deadline,
        [string]$Description
    )

    while ((Get-Date) -lt $Deadline) {
        $failure = Get-ChildItem -LiteralPath $RunDirectory `
            -Filter 'failed-*.marker' -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $failure) {
            throw "Reconnect E2E failed: $($failure.FullName)"
        }
        Assert-NoUnexpectedProcessExit
        if (Test-Path -LiteralPath $Path) {
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for $Description. Evidence: $RunDirectory"
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
try {
    for ($index = 0; $index -lt 4; $index++) {
        $processes[$index] = Start-ReconnectPlayer -Index $index -Stage initial
        Start-Sleep -Milliseconds 300
    }

    $victim = if ($HostRecovery) { 0 } else { 1 }
    $killRequest = if ($HostRecovery) { 'kill-host.request' } else { 'kill-client-1.request' }
    Wait-ForFile -Path (Join-Path $RunDirectory $killRequest) `
        -Deadline $deadline -Description 'hard-disconnect request'

    $disconnecting = $processes[$victim]
    if (!$disconnecting.HasExited) {
        Stop-Process -Id $disconnecting.Id -Force
        $disconnecting.WaitForExit()
    }
    [void]$processes.Remove($victim)
    if ($HostRecovery) {
        for ($index = 1; $index -lt 4; $index++) {
            Wait-ForFile -Path (Join-Path $RunDirectory "host-waiting-$index.marker") `
                -Deadline $deadline -Description "client $index waiting in the original room"
        }
    } else {
        Set-Content -LiteralPath (Join-Path $RunDirectory 'client-1-killed.marker') `
            -Value 'KILLED' -NoNewline
        Wait-ForFile -Path (Join-Path $RunDirectory 'reconnect-pause-observed.marker') `
            -Deadline $deadline -Description 'host reconnect-pause validation'
    }
    if ($HostRecovery -and $HostReturnDelaySeconds -gt 0) {
        $returnAt = (Get-Date).AddSeconds($HostReturnDelaySeconds)
        while ((Get-Date) -lt $returnAt) {
            Assert-NoUnexpectedProcessExit
            Start-Sleep -Milliseconds 500
        }
    }
    if ($ReconnectTimeout) {
        Wait-ForFile -Path (Join-Path $RunDirectory 'timeout-lobby-ready.marker') `
            -Deadline $deadline -Description '60-second timeout and retained checkpoint'
    }
    $processes[$victim] = Start-ReconnectPlayer -Index $victim -Stage resume

    for ($index = 0; $index -lt 4; $index++) {
        Wait-ForFile -Path (Join-Path $RunDirectory "passed-$index.marker") `
            -Deadline $deadline -Description "player $index PASS marker"
    }
    Wait-ForFile -Path (Join-Path $RunDirectory 'summary.json') `
        -Deadline $deadline -Description 'summary.json'

    foreach ($process in $processes.Values) {
        if (!$process.HasExited) {
            $remainingMs = [Math]::Max(
                1,
                [int](($deadline - (Get-Date)).TotalMilliseconds))
            if (!$process.WaitForExit($remainingMs)) {
                throw "Player process $($process.Id) did not exit after PASS."
            }
        }
        if ($process.ExitCode -ne 0) {
            throw "Player process $($process.Id) exited with $($process.ExitCode)."
        }
    }

    Write-Output $RunDirectory
} finally {
    foreach ($process in $processes.Values) {
        if ($null -ne $process -and !$process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
