[CmdletBinding()]
param(
    [string]$Executable,
    [string]$RunDirectory,
    [string]$ArtifactDirectory,
    [ValidateRange(180, 1800)]
    [int]$TimeoutSeconds = 720
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $repoRoot `
        'Builds\Windows-Development\MazeParty.exe'
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($RunDirectory)) {
    $RunDirectory = Join-Path $repoRoot "Logs\ItemMultiplayerQA-$stamp"
}
$RunDirectory = [System.IO.Path]::GetFullPath($RunDirectory)
if (Test-Path -LiteralPath $RunDirectory) {
    if (@(Get-ChildItem -LiteralPath $RunDirectory -Force).Count -ne 0) {
        throw "Run directory must be new or empty: $RunDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $RunDirectory -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $day = Get-Date -Format 'yyyy-MM-dd'
    $ArtifactDirectory = Join-Path $repoRoot `
        "Builds\TestArtifacts\ItemMultiplayerQA\$day\$stamp"
}
$ArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $ArtifactDirectory) {
    if (@(Get-ChildItem -LiteralPath $ArtifactDirectory -Force).Count -ne 0) {
        throw "Artifact directory must be new or empty: $ArtifactDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $ArtifactDirectory -Force |
        Out-Null
}

$profilePrefix = "itemqa-$stamp"
$processes = @{}

function Start-ItemQaPlayer {
    param([ValidateRange(0, 3)] [int]$Index)

    $role = if ($Index -eq 0) { 'host' } else { 'client' }
    $logPath = Join-Path $RunDirectory "unity-item-player-$Index.log"
    $arguments = @(
        '-e2e-item-qa',
        '-e2e-role', $role,
        '-e2e-player', $Index,
        '-e2e-run-dir', ('"' + $RunDirectory + '"'),
        '-e2e-artifact-dir', ('"' + $ArtifactDirectory + '"'),
        '-auth-profile', "$profilePrefix-p$Index",
        '-screen-fullscreen', '0',
        '-screen-width', '1280',
        '-screen-height', '720',
        '-logFile', ('"' + $logPath + '"')
    )
    return Start-Process -FilePath $Executable -ArgumentList $arguments `
        -PassThru -WindowStyle Normal
}

function Assert-NoItemQaFailure {
    $failure = Get-ChildItem -LiteralPath $RunDirectory `
        -Filter 'item-failed-*.marker' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $failure) {
        $details = Get-Content -LiteralPath $failure.FullName -Raw
        throw "Item multiplayer QA failed: $($failure.FullName)`n$details"
    }
}

function Assert-NoUnexpectedProcessExit {
    foreach ($index in @($processes.Keys)) {
        $process = $processes[$index]
        $passPath = Join-Path $RunDirectory "item-passed-$index.marker"
        if ($null -ne $process -and $process.HasExited -and
            !(Test-Path -LiteralPath $passPath)) {
            throw "Item QA player $index process $($process.Id) exited unexpectedly with $($process.ExitCode)."
        }
    }
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
try {
    for ($index = 0; $index -lt 4; $index++) {
        $processes[$index] = Start-ItemQaPlayer -Index $index
        Start-Sleep -Milliseconds 300
    }

    while ((Get-Date) -lt $deadline) {
        Assert-NoItemQaFailure
        Assert-NoUnexpectedProcessExit
        $complete = $true
        for ($index = 0; $index -lt 4; $index++) {
            if (!(Test-Path -LiteralPath (Join-Path $RunDirectory `
                    "item-passed-$index.marker"))) {
                $complete = $false
                break
            }
        }

        if ($complete -and
            (Test-Path -LiteralPath `
                (Join-Path $RunDirectory 'summary.json'))) {
            break
        }
        Start-Sleep -Milliseconds 500
    }

    Assert-NoItemQaFailure
    for ($index = 0; $index -lt 4; $index++) {
        $passPath = Join-Path $RunDirectory "item-passed-$index.marker"
        if (!(Test-Path -LiteralPath $passPath)) {
            throw "Timed out waiting for player $index PASS. Evidence: $RunDirectory"
        }

        $manifestPath = Join-Path $ArtifactDirectory "manifest-p$index.json"
        if (!(Test-Path -LiteralPath $manifestPath)) {
            throw "Missing screenshot manifest for player ${index}: $manifestPath"
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw |
            ConvertFrom-Json
        if (@($manifest.screenshots).Count -eq 0) {
            throw "Screenshot manifest is empty for player ${index}: $manifestPath"
        }
        foreach ($capture in @($manifest.screenshots)) {
            if ($capture.width -ne 1280 -or $capture.height -ne 720 -or
                $capture.nonBlackPixels -le 0 -or
                $capture.luminanceRange -lt 8) {
                throw "Screenshot manifest reports an invalid rendered frame: $manifestPath / $($capture.file)"
            }
        }
    }

    $summaryPath = Join-Path $RunDirectory 'summary.json'
    if (!(Test-Path -LiteralPath $summaryPath)) {
        throw "Timed out waiting for summary.json. Evidence: $RunDirectory"
    }

    $screenshots = @(Get-ChildItem -LiteralPath $ArtifactDirectory `
        -Filter '*.png' -File)
    if ($screenshots.Count -lt 20) {
        throw "Expected at least 20 item QA screenshots, found $($screenshots.Count)."
    }
    foreach ($screenshot in $screenshots) {
        if ($screenshot.Length -le 0) {
            throw "Screenshot is empty: $($screenshot.FullName)"
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

    [pscustomobject]@{
        RunDirectory = $RunDirectory
        ArtifactDirectory = $ArtifactDirectory
        Summary = $summaryPath
        ScreenshotCount = $screenshots.Count
    }
} finally {
    foreach ($process in $processes.Values) {
        if ($null -ne $process -and !$process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
