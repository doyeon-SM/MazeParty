[CmdletBinding()]
param(
    [string]$SdkSourcePath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'StovePCSDK_Full_net46_CSharp_3.5.3.0_x64')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$gitRoot = git -C $repoRoot rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot) -ne [IO.Path]::GetFullPath($repoRoot)) {
    throw 'Run the copy of this script in the MazeParty repository Tools folder.'
}

$privatePaths = @('Assets/Stove&Steam/', 'Assets/Stove&Steam.meta')
$tracked = @(git -C $repoRoot ls-files -- $privatePaths)
if ($LASTEXITCODE -ne 0 -or $tracked.Count -ne 0) {
    throw 'The private platform directory must not contain Git-tracked files. No files were changed.'
}

$sdkDirectory = Join-Path $repoRoot 'Assets/Stove&Steam/STOVE/x86_64'
$settingsDirectory = Join-Path $repoRoot 'Assets/Stove&Steam/.local'
$libraries = @(
    @{ Name = 'BaseSDK.dll'; Guid = '297dd338686e4a044b472d59d71046a5'; Hash = '9CDD65384A99F8905477B853370C81F268D94D5BA765D5781590E6FFBED0D86A' },
    @{ Name = 'BaseSDK_NET.dll'; Guid = 'e9def1cf714fd59439e038ee31996ccb'; Hash = 'F4CC8254018FE34402FB5521C93A8C36F15599E41A81686F54C1A9DEDB48F3A3' }
)

# Verify all inputs before restoring anything. Existing DLLs are never overwritten.
foreach ($library in $libraries) {
    $library.Destination = Join-Path $sdkDirectory $library.Name
    $library.Source = Join-Path (Join-Path $SdkSourcePath 'x86_64') $library.Name
    $candidate = if (Test-Path -LiteralPath $library.Destination -PathType Leaf) { $library.Destination } else { $library.Source }
    if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw ('Missing SDK file: ' + $library.Name + '. Pass -SdkSourcePath with the extracted 3.5.3.0 Full x64 package root.')
    }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($candidate)
    if ($version.FileMajorPart -ne 3 -or $version.FileMinorPart -ne 5 -or $version.FileBuildPart -ne 3 -or $version.FilePrivatePart -ne 0 -or
        (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ne $library.Hash) {
        throw ('SDK version or checksum mismatch: ' + $library.Name + '. No files were changed.')
    }
}

$encoding = New-Object System.Text.UTF8Encoding($false)
$excludePath = git -C $repoRoot rev-parse --path-format=absolute --git-path info/exclude
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($excludePath)) { throw 'Cannot locate repository-local Git exclusions.' }
$excludeText = if (Test-Path -LiteralPath $excludePath) { [IO.File]::ReadAllText($excludePath) } else { '' }
$excludeLines = $excludeText -split '\r?\n'
$missingRules = @('/Assets/Stove&Steam/', '/Assets/Stove&Steam.meta') | Where-Object { $_ -notin $excludeLines }
if ($missingRules) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($excludePath)) | Out-Null
    [IO.File]::AppendAllText($excludePath, "`n# Private platform files: retained across branch switches.`n" + ($missingRules -join "`n") + "`n", $encoding)
}

[IO.Directory]::CreateDirectory($sdkDirectory) | Out-Null
[IO.Directory]::CreateDirectory($settingsDirectory) | Out-Null
foreach ($library in $libraries) {
    if (!(Test-Path -LiteralPath $library.Destination)) {
        [IO.File]::Copy($library.Source, $library.Destination, $false)
    }
    $metaPath = $library.Destination + '.meta'
    if (!(Test-Path -LiteralPath $metaPath)) {
        $meta = @"
fileFormatVersion: 2
guid: $($library.Guid)
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints:
  - MAZEPARTY_STOVE
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Windows
  - first:
      Standalone: Win64
    second:
      enabled: 1
      settings:
        CPU: x86_64
  userData:
  assetBundleName:
  assetBundleVariant:
"@
        [IO.File]::WriteAllText($metaPath, $meta + "`n", $encoding)
    }
}

$templates = @{
    'stove.json' = [ordered]@{ ugsEnvironment = 'production'; stoveEnvironment = 'LIVE'; stoveGameId = ''; stoveAppKey = '' }
    'steam.json' = [ordered]@{ ugsEnvironment = 'production' }
}
foreach ($name in $templates.Keys) {
    $path = Join-Path $settingsDirectory $name
    if (!(Test-Path -LiteralPath $path)) {
        [IO.File]::WriteAllText($path, ($templates[$name] | ConvertTo-Json) + "`n", $encoding)
    }
}

foreach ($library in $libraries) {
    if ((Get-FileHash -LiteralPath $library.Destination -Algorithm SHA256).Hash -ne $library.Hash) { throw 'Restored SDK checksum verification failed.' }
}
foreach ($relativePath in @('Assets/Stove&Steam/.local/stove.json', 'Assets/Stove&Steam/.local/steam.json', 'Assets/Stove&Steam/STOVE/x86_64/BaseSDK.dll', 'Assets/Stove&Steam.meta')) {
    git -C $repoRoot check-ignore -q -- $relativePath
    if ($LASTEXITCODE -ne 0) { throw 'A restored private path is not ignored by Git.' }
}

Write-Host 'Platform local files restored. Existing settings and metadata were preserved.'
Write-Host 'Let Unity finish importing. Then run MazeParty > Platform > Set up profiles and local settings.'
Write-Host 'Fill Game ID and Application Key in Assets/Stove&Steam/.local/stove.json. Do not add Application Secret.'
