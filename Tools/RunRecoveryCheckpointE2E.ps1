[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('turn-overview', 'minigame-intro-ready', 'match-complete')]
    [string]$Checkpoint,
    [string]$Executable,
    [string]$RunDirectory,
    [ValidateSet('forest-graybox', 'maze-graybox')]
    [string]$Map = 'maze-graybox',
    [ValidateRange(600, 7200)]
    [int]$TimeoutSeconds = 2400
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $repoRoot 'Builds\Windows-Development\MazeParty.exe'
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($RunDirectory)) {
    $day = Get-Date -Format 'yyyy-MM-dd'
    $RunDirectory = Join-Path $repoRoot `
        "Builds\TestArtifacts\RecoveryQA\$day\$Checkpoint-$stamp"
}
$RunDirectory = [System.IO.Path]::GetFullPath($RunDirectory)
if (Test-Path -LiteralPath $RunDirectory) {
    if (@(Get-ChildItem -LiteralPath $RunDirectory -Force).Count -ne 0) {
        throw "Run directory must be new or empty: $RunDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $RunDirectory -Force | Out-Null
}

$checkpointProfileCode = switch ($Checkpoint) {
    'turn-overview' { 'turn' }
    'minigame-intro-ready' { 'minigame' }
    'match-complete' { 'complete' }
}
$profileStamp = Get-Date -Format 'yyMMddHHmmss'
$profilePrefix = "rcv-$checkpointProfileCode-$profileStamp"
$longestProfile = "$profilePrefix-p3"
if ($longestProfile.Length -gt 30) {
    throw "Authentication profile exceeds Unity's 30-character limit: $longestProfile"
}
$processes = @{}

function Start-RecoveryPlayer {
    param(
        [ValidateRange(0, 3)] [int]$Index,
        [ValidateSet('initial', 'resume')] [string]$Stage
    )

    $role = if ($Index -eq 0) { 'host' } else { 'client' }
    $logPath = Join-Path $RunDirectory "unity-player-$Index-$Stage.log"
    $arguments = @(
        '-e2e-full-match',
        '-e2e-role', $role,
        '-e2e-player', $Index,
        '-e2e-run-dir', ('"' + $RunDirectory + '"'),
        '-e2e-map', $Map,
        '-e2e-recovery-checkpoint', $Checkpoint,
        '-e2e-recovery-stage', $Stage,
        '-auth-profile', "$profilePrefix-p$Index",
        '-screen-fullscreen', '0',
        '-screen-width', '1280',
        '-screen-height', '720',
        '-logFile', ('"' + $logPath + '"')
    )
    return Start-Process -FilePath $Executable -ArgumentList $arguments `
        -PassThru -WindowStyle Normal
}

function Assert-NoFailure {
    $failure = Get-ChildItem -LiteralPath $RunDirectory `
        -Filter 'failed-*.marker' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $failure) {
        throw "Recovery E2E failed: $($failure.FullName)"
    }
}

function Assert-NoUnexpectedProcessExit {
    foreach ($index in @($processes.Keys)) {
        $process = $processes[$index]
        $passPath = Join-Path $RunDirectory "passed-$index.marker"
        if ($null -ne $process -and $process.HasExited -and
            !(Test-Path -LiteralPath $passPath)) {
            throw "Recovery player $index process $($process.Id) exited unexpectedly with $($process.ExitCode)."
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
        Assert-NoFailure
        Assert-NoUnexpectedProcessExit
        if (Test-Path -LiteralPath $Path) {
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for $Description. Evidence: $RunDirectory"
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
try {
    for ($index = 0; $index -lt 4; $index++) {
        $processes[$index] = Start-RecoveryPlayer -Index $index -Stage initial
        Start-Sleep -Milliseconds 300
    }

    Wait-ForFile `
        -Path (Join-Path $RunDirectory 'recovery-checkpoint-ready.marker') `
        -Deadline $deadline `
        -Description "$Checkpoint initial recovery checkpoint"

    foreach ($process in $processes.Values) {
        if (!$process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }
    Set-Content -LiteralPath `
        (Join-Path $RunDirectory 'initial-processes-killed.marker') `
        -Value 'KILLED' -NoNewline
    $joinCodePath = Join-Path $RunDirectory 'join-code.txt'
    if (Test-Path -LiteralPath $joinCodePath) {
        Remove-Item -LiteralPath $joinCodePath -Force
    }
    # Let the backend retire every hard-killed membership before the same
    # authentication profiles create the recovery lobby. Reusing them too
    # quickly can deliver a delayed departure into the new lobby while its
    # host is switching to Playing.
    Start-Sleep -Seconds 12

    $processes = @{}
    for ($index = 0; $index -lt 4; $index++) {
        $processes[$index] = Start-RecoveryPlayer -Index $index -Stage resume
        Start-Sleep -Milliseconds 300
    }

    for ($index = 0; $index -lt 4; $index++) {
        Wait-ForFile -Path (Join-Path $RunDirectory "passed-$index.marker") `
            -Deadline $deadline -Description "player $index recovery PASS"
    }
    Wait-ForFile -Path (Join-Path $RunDirectory 'summary.json') `
        -Deadline $deadline -Description 'recovery summary.json'

    foreach ($process in $processes.Values) {
        if (!$process.HasExited) {
            $remainingMs = [Math]::Max(
                1,
                [int](($deadline - (Get-Date)).TotalMilliseconds))
            if (!$process.WaitForExit($remainingMs)) {
                throw "Recovery player $($process.Id) did not exit after PASS."
            }
        }
        if ($process.ExitCode -ne 0) {
            throw "Recovery player $($process.Id) exited with $($process.ExitCode)."
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
