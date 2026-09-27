<#
.SYNOPSIS
    Builds a downloadable 7SeasLauncher release.

.EXAMPLE
    pwsh -File scripts/package-release.ps1 -Version 1.2.0
    pwsh -File scripts/package-release.ps1 -Version 1.2.0 -SelfContained
#>
param(
    [string]$Version = '1.2.0',
    [string]$Configuration = 'Release',
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $repoRoot 'release'
$suffix = if ($SelfContained) { 'standalone' } else { 'needs-dotnet8' }
$stage = Join-Path $releaseRoot "7SeasLauncher-$Version-$suffix"
$zipPath = Join-Path $releaseRoot "7SeasLauncher-$Version-win-x64-$suffix.zip"

Write-Host "Packaging 7SeasLauncher $Version ($suffix)"

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$project = Join-Path $repoRoot 'src/SevenSeas.Launcher/SevenSeas.Launcher.csproj'
$selfContainedArg = if ($SelfContained) { 'true' } else { 'false' }
& dotnet publish $project -c $Configuration -r win-x64 --self-contained $selfContainedArg -o $stage --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# Symbols are build debris, not part of a download.
$pdbs = Get-ChildItem $stage -Recurse -File -Filter *.pdb
if ($pdbs) { $pdbs | Remove-Item -Force; Write-Host "removed $($pdbs.Count) symbol files" }

if ($SelfContained) {
    $needsBlock = @'
  - Windows 10 or 11, 64-bit
  - Microsoft Edge WebView2 Runtime (already present on Windows 11 and most Windows 10
    machines). If the Browse tab is blank, install the Evergreen Bootstrapper from
    https://developer.microsoft.com/microsoft-edge/webview2/

  Nothing else. The .NET files this app needs are already inside this folder.
'@
} else {
    $needsBlock = @'
  - Windows 10 or 11, 64-bit
  - .NET 8 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/8.0
    (if it is missing, Windows tells you when you run the app)
  - Microsoft Edge WebView2 Runtime (already present on Windows 11 and most Windows 10
    machines). If the Browse tab is blank, install the Evergreen Bootstrapper from
    https://developer.microsoft.com/microsoft-edge/webview2/

  Downloading the "standalone" build instead removes the .NET requirement.
'@
}

@"
7SeasLauncher
=============

WHAT IT IS
  A desktop app with a built-in browser for finding game downloads. You click the download
  link yourself; 7SeasLauncher catches it, checks it, unpacks it and adds it to your library.

RUNNING IT
  1. Extract this folder anywhere (Desktop or Documents is fine).
  2. Run 7SeasLauncher.exe
  3. Windows SmartScreen will warn you because the app is not signed.
     Click "More info", then "Run anyway".
  4. That's it. No installer, no account.

WHAT YOU NEED
$needsBlock

GETTING STARTED
  - Open the Browse tab and go to a site you have permission to use.
  - Click "Save this page". 7SeasLauncher fills in the name, address and search URL for you.
  - Then search for a game and click its download link. Watch the Jobs tab to see progress.

WHERE YOUR FILES GO
  %APPDATA%\7SeasLauncher\      settings, database, logs
  %USERPROFILE%\7SeasLauncher\  Games, Temp, Trash (changeable in Settings)

  Settings -> Advanced -> Free up space deletes leftover archives once you are done.
"@ | Set-Content -Path (Join-Path $stage 'READ-ME-FIRST.txt') -Encoding UTF8

# Settings must never ship: they hold the SteamGridDB API key.
$leaks = Get-ChildItem $stage -Recurse -File -Include 'settings.json','*.corrupt','7seas-diagnostics-*.zip' -EA SilentlyContinue
if ($leaks) {
    throw "refusing to package: found user data -> $($leaks.FullName -join ', ')"
}
Write-Host 'no settings or diagnostics in the package (good)'

if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal

$size = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
$files = (Get-ChildItem $stage -Recurse -File).Count
Write-Host ''
Write-Host "release zip: $zipPath ($size MB, $files files)"
