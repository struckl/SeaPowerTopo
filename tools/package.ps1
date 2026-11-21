<#
.SYNOPSIS
    Builds a release of Sea Power Topographic Map and stages the folder that goes to the Steam Workshop.

.DESCRIPTION
    Release builds without the automatic deploy, then dist\SeaPowerTopo\ with exactly what players get:
    SeaPowerTopo.dll (the globe build), SeaPowerTopoFlat.dll (the flat-map build for Sea Power 0.8.7, the same source
    with FLATMAP defined), _info.ini, LICENSE.txt, CHANGELOG.md and preview.jpg (the Workshop preview, which the game's upload
    screen only lets you pick from inside StreamingAssets). Each build stays off on the game version the other is for.
    The flat-map build is compiled against the 0.8.7 assemblies in -FlatManagedDir (a copy of 0.8.7's Sea Power_Data\Managed;
    default: _gamerefs in the workspace), the globe build against the globe game's (-GlobeManagedDir when the installed game is
    0.8.7); -NoFlat leaves the flat build out.
    Every DLL is checked against both games' assemblies with the workspace's tools\compat-check.
    With -Install the staged folder also replaces <GameDir>\Sea Power_Data\StreamingAssets\SeaPowerTopo\, the folder to pick in
    the game's Create Mod screen. Sea Power must be closed for that. -Zip also writes dist\SeaPowerTopo-<version>.zip.
    The steps are the shared ones of every R.A.T.S. mod (SeaPowerShared\tools\RatsPackage.psm1); the workspace is found
    above this repo, -Workspace names it for a clone elsewhere, -RatsShared another copy of the shared repo.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Install
#>
param(
    [string]$GameDir = $(if ($env:GameDir) { $env:GameDir } else { 'E:\Games\steamapps\common\Sea Power' }),
    [string]$Workspace = $env:RatsWorkspace,
    [string]$RatsShared = $env:RatsShared,
    [string]$FlatManagedDir = $env:FlatManagedDir,
    # The globe build's game assemblies when the installed game isn't the globe version (a saved copy of its Managed).
    [string]$GlobeManagedDir = $env:GlobeManagedDir,
    [switch]$NoFlat,
    [switch]$Install,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Workspace) {
    $Workspace = $root
    while ($Workspace -and -not (Test-Path (Join-Path $Workspace 'mods.json'))) { $Workspace = Split-Path -Parent $Workspace }
    if (-not $Workspace) { throw 'No workspace (a folder with mods.json) above this repo - pass -Workspace.' }
}
if (-not $RatsShared) { $RatsShared = Join-Path $Workspace 'SeaPowerShared' }
Import-Module (Join-Path $RatsShared 'tools\RatsPackage.psm1') -Force

$name = 'SeaPowerTopo'
$project = Join-Path $root "src\$name\$name.csproj"
$dist = Join-Path $root "dist\$name"
if (-not $FlatManagedDir) { $FlatManagedDir = Join-Path $Workspace '_gamerefs\SeaPower-0.8.7-public\Managed' }
if (-not $GlobeManagedDir) { $GlobeManagedDir = Join-Path $GameDir 'Sea Power_Data\Managed' }

$env:RatsShared = $RatsShared.TrimEnd('\') + '\'      # the csproj's -p:RatsShared, also for the flat build
Build-RatsMod -Project $project -Dist $dist -GameDir $GameDir -GlobeManagedDir $GlobeManagedDir -FlatManagedDir $FlatManagedDir -Workspace $Workspace -NoFlat:$NoFlat | Out-Null
Test-RatsCompat -Workspace $Workspace -Dist $dist -RootNamespace $name -GlobeManagedDir $GlobeManagedDir -FlatManagedDir $FlatManagedDir
Copy-RatsCommon -Root $root -Dist $dist -InfoIni (Join-Path $root "src\$name\mod\_info.ini")
$version = Get-RatsModVersion -PluginFile (Join-Path $root "src\$name\Plugin.cs")
Write-Host "Staged Sea Power Topographic Map $version in $dist"
Show-RatsStage -Dist $dist
if ($Zip) { New-RatsZip -Dist $dist -Version $version }
if ($Install) { Install-RatsMod -Dist $dist -GameDir $GameDir }
