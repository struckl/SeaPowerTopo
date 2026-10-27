<#
.SYNOPSIS
    Builds a release of Sea Power Topographic Map and stages the folder that goes to the Steam Workshop.

.DESCRIPTION
    Release builds without the automatic deploy, then dist\SeaPowerTopo\ with exactly what players get:
    SeaPowerTopo.dll (the globe build, this branch), SeaPowerTopoFlat.dll (the flat-map build for Sea Power 0.8.7,
    from the flat-map branch), _info.ini, LICENSE.txt and preview.jpg – the Workshop preview, which the game's upload
    screen only lets you pick from inside StreamingAssets. Each build stays off on the game version the other is for.
    The flat-map build is compiled in a temporary worktree of the flat-map branch against the 0.8.7 assemblies in
    -FlatManagedDir (a copy of 0.8.7's Sea Power_Data\Managed); -NoFlat leaves it out.
    With -Install the staged folder also replaces <GameDir>\Sea Power_Data\StreamingAssets\SeaPowerTopo\, the folder
    to pick in the game's Create Mod screen. Sea Power must be closed for that.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Install
#>
param(
    [string]$GameDir = $(if ($env:GameDir) { $env:GameDir } else { 'E:\Games\steamapps\common\Sea Power' }),
    [string]$FlatManagedDir = $(if ($env:FlatManagedDir) { $env:FlatManagedDir } else { 'E:\Project\Mods\_gamerefs\SeaPower-0.8.7-public\Managed' }),
    # The globe build's game assemblies when the installed game isn't the globe version (a saved copy of its Managed).
    [string]$GlobeManagedDir = $env:GlobeManagedDir,
    [switch]$NoFlat,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\SeaPowerTopo\SeaPowerTopo.csproj'
$dist = Join-Path $root 'dist\SeaPowerTopo'

$globeRefs = @()
if ($GlobeManagedDir) { $globeRefs = @("-p:ManagedDir=$GlobeManagedDir") }
dotnet build $project -c Release -p:Deploy=false -p:GameDir="$GameDir" @globeRefs
if ($LASTEXITCODE -ne 0) { throw 'Build failed (with the game on 0.8.7, pass -GlobeManagedDir with the globe version''s assemblies).' }

$out = Join-Path $root 'src\SeaPowerTopo\bin\Release\netstandard2.1'
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $out 'SeaPowerTopo.dll') $dist

if (-not $NoFlat) {
    if (-not (Test-Path (Join-Path $FlatManagedDir 'Seapower-Scripts.dll'))) { throw "No 0.8.7 assemblies in $FlatManagedDir (or pass -NoFlat)." }
    $flat = Join-Path ([System.IO.Path]::GetTempPath()) "seapowertopo-flat-$PID"
    git -C $root worktree add --detach $flat flat-map | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No flat-map branch.' }
    try {
        dotnet build (Join-Path $flat 'src\SeaPowerTopo\SeaPowerTopo.csproj') -c Release -p:Deploy=false -p:GameDir="$GameDir" -p:ManagedDir="$FlatManagedDir"
        if ($LASTEXITCODE -ne 0) { throw 'Flat-map build failed.' }
        Copy-Item (Join-Path $flat 'src\SeaPowerTopo\bin\Release\netstandard2.1\SeaPowerTopoFlat.dll') $dist
    }
    finally {
        git -C $root worktree remove --force $flat | Out-Null
    }
}
Copy-Item (Join-Path $root 'src\SeaPowerTopo\mod\_info.ini') $dist
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dist 'LICENSE.txt')
Copy-Item (Join-Path $root 'workshop\preview.jpg') $dist

$version = (Select-String -Path (Join-Path $root 'src\SeaPowerTopo\Plugin.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
Write-Host "Staged Sea Power Topographic Map $version in $dist"

if ($Install) {
    if (Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue) { throw 'Sea Power is running - close it first.' }
    $target = Join-Path $GameDir 'Sea Power_Data\StreamingAssets\SeaPowerTopo'
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Copy-Item -Recurse $dist $target
    Write-Host "Installed to $target - pick this folder in Mods > Create Mod."
}
