[CmdletBinding()]
param(
    [string]$Executable,
    [string]$RunDirectory,
    [ValidateSet('forest-graybox', 'maze-graybox')]
    [string]$Map = 'forest-graybox',
    [switch]$CaptureMinigames,
    [switch]$NaturalMinigames,
    [switch]$CeremonyTie,
    [ValidateRange(300, 14400)]
    [int]$TimeoutSeconds = 7200
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $repoRoot 'Builds\Windows-Development\MazeParty.exe'
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($RunDirectory)) {
    if ($CaptureMinigames) {
        $day = Get-Date -Format 'yyyy-MM-dd'
        $RunDirectory = Join-Path $repoRoot `
            "Builds\TestArtifacts\BuildMinigameQA\$day\FullMatch-$stamp"
    } else {
        $RunDirectory = Join-Path $repoRoot "Logs\FullMatchE2E-$stamp"
    }
}
$RunDirectory = [System.IO.Path]::GetFullPath($RunDirectory)
if (Test-Path -LiteralPath $RunDirectory) {
    if (@(Get-ChildItem -LiteralPath $RunDirectory -Force).Count -ne 0) {
        throw "Run directory must be new or empty: $RunDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $RunDirectory -Force | Out-Null
}

$profilePrefix = "fullmatch-$stamp"
$processes = @{}

function Start-FullMatchPlayer {
    param([ValidateRange(0, 3)] [int]$Index)

    $role = if ($Index -eq 0) { 'host' } else { 'client' }
    $logPath = Join-Path $RunDirectory "unity-player-$Index.log"
    $arguments = @(
        '-e2e-full-match',
        '-e2e-role', $role,
        '-e2e-player', $Index,
        '-e2e-run-dir', ('"' + $RunDirectory + '"'),
        '-e2e-map', $Map,
        '-auth-profile', "$profilePrefix-p$Index",
        '-screen-fullscreen', '0',
        '-screen-width', '1280',
        '-screen-height', '720',
        '-logFile', ('"' + $logPath + '"')
    )
    if ($CaptureMinigames) {
        $arguments += '-e2e-capture-minigames'
    }
    if ($NaturalMinigames) {
        $arguments += '-e2e-natural-minigames'
    }
    if ($CeremonyTie) {
        $arguments += '-e2e-ceremony-tie'
    }

    return Start-Process -FilePath $Executable -ArgumentList $arguments `
        -PassThru -WindowStyle Normal
}

function Assert-NoFailure {
    $failure = Get-ChildItem -LiteralPath $RunDirectory `
        -Filter 'failed-*.marker' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $failure) {
        throw "Full-match E2E failed: $($failure.FullName)"
    }
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

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
try {
    for ($index = 0; $index -lt 4; $index++) {
        $processes[$index] = Start-FullMatchPlayer -Index $index
        Start-Sleep -Milliseconds 300
    }

    while ((Get-Date) -lt $deadline) {
        Assert-NoFailure
        Assert-NoUnexpectedProcessExit
        $complete = $true
        for ($index = 0; $index -lt 4; $index++) {
            if (!(Test-Path -LiteralPath `
                    (Join-Path $RunDirectory "passed-$index.marker"))) {
                $complete = $false
                break
            }
        }
        if ($complete -and
            (Test-Path -LiteralPath (Join-Path $RunDirectory 'summary.json'))) {
            break
        }
        Start-Sleep -Milliseconds 500
    }

    Assert-NoFailure
    for ($index = 0; $index -lt 4; $index++) {
        if (!(Test-Path -LiteralPath `
                (Join-Path $RunDirectory "passed-$index.marker"))) {
            throw "Timed out waiting for player $index PASS. Evidence: $RunDirectory"
        }
    }
    if (!(Test-Path -LiteralPath (Join-Path $RunDirectory 'summary.json'))) {
        throw "Timed out waiting for summary.json. Evidence: $RunDirectory"
    }

    if ($CaptureMinigames) {
        $screenshots = @(Get-ChildItem -LiteralPath `
            (Join-Path $RunDirectory 'minigame-screenshots') `
            -Filter '*.png' -File)
        if ($screenshots.Count -ne 15) {
            throw "Expected 15 minigame screenshots, found $($screenshots.Count)."
        }
        foreach ($screenshot in $screenshots) {
            if ($screenshot.Length -le 0) {
                throw "Screenshot is empty: $($screenshot.FullName)"
            }
        }
    }

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
