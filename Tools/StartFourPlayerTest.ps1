[CmdletBinding()]
param(
    [string]$Executable,
    [ValidatePattern('^[A-Za-z0-9_-]{1,20}$')]
    [string]$ProfilePrefix = 'local4'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $repoRoot 'Builds/Windows-Development/MazeParty.exe'
}
if (!(Test-Path -LiteralPath $Executable -PathType Leaf)) {
    throw 'Build Select Development > Windows Development (Mono x64) first, or pass -Executable.'
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$runDirectory = Join-Path $repoRoot ('Logs/ManualFourPlayer-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

# Interactive windows for the user; no automatic login, room creation or gameplay.
foreach ($index in 1..4) {
    $arguments = @(
        '-auth-profile', "$ProfilePrefix-p$index",
        '-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '540',
        '-logFile', ('"' + (Join-Path $runDirectory "player-$index.log") + '"')
    )
    Start-Process -FilePath $Executable -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $Executable) -WindowStyle Normal
}
Write-Host 'Four game windows opened. Create one room and join it from the other three windows using its code.'
Write-Host "Per-player logs: $runDirectory"
