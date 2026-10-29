<#
.SYNOPSIS
    Builds a release of Sea Power Topographic Map and stages the folder that goes to the Steam Workshop.

.DESCRIPTION
    Release builds without the automatic deploy, then dist\SeaPowerTopo\ with exactly what players get:
    SeaPowerTopo.dll (the globe build), SeaPowerTopoFlat.dll (the flat-map build for Sea Power 0.8.7, the same source
    with FLATMAP defined), _info.ini, LICENSE.txt and preview.jpg – the Workshop preview, which the game's upload
    screen only lets you pick from inside StreamingAssets. Each build stays off on the game version the other is for.
    The flat-map build is compiled against the 0.8.7 assemblies in
    -FlatManagedDir (a copy of 0.8.7's Sea Power_Data\Managed); -NoFlat leaves it out.
    With -Install the staged folder also replaces <GameDir>\Sea Power_Data\StreamingAssets\SeaPowerTopo\, the folder
    to pick in the game's Create Mod screen. Sea Power must be closed for that.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Install
#>
param(
    [string]$GameDir = $(if ($env:GameDir) { $env:GameDir } else { 'E:\Games\steamapps\common\Sea Power' }),
    [string]$FlatManagedDir = $(if ($env:FlatManagedDir) { $env:FlatManagedDir } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\_gamerefs\SeaPower-0.8.7-public\Managed')) }),
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
    # The same source with FLATMAP defined, against 0.8.7's assemblies, under its own assembly name (SeaPowerTopoFlat.dll) next to the globe one.
    dotnet build $project -c Release -p:Deploy=false -p:GameDir="$GameDir" -p:GameBuild=Flat -p:FlatManagedDir="$FlatManagedDir" --no-incremental
    if ($LASTEXITCODE -ne 0) { throw 'Flat-map build failed.' }
    Copy-Item (Join-Path $out 'SeaPowerTopoFlat.dll') $dist
}
# Each DLL against both games' assemblies (the workspace's compat-check): nothing missing on its own game, and on the other
# game its entry point and every type's base/interfaces/fields (--classload: Unity's ECS start-up loads every type of every
# loaded assembly, so one field of a type the other game lacks stops all missions from loading there although the DLL
# stays off - found 2026-10-28).
$tool = Join-Path $root '..\tools\compat-check'
if (Test-Path $tool) {
    dotnet build $tool -c Release -v q | Out-Null
    $globeManaged = if ($GlobeManagedDir) { $GlobeManagedDir } else { Join-Path $GameDir 'Sea Power_Data\Managed' }
    $gates = @('--gate', 'SeaPowerTopo.Plugin::TriggerEntryPoint', '--gate', 'SeaPowerTopo.Plugin::.ctor')
    foreach ($b in @(@('SeaPowerTopo.dll', $globeManaged, $FlatManagedDir), @('SeaPowerTopoFlat.dll', $FlatManagedDir, $globeManaged))) {
        $dll = Join-Path $dist $b[0]
        if (-not (Test-Path $dll)) { continue }
        dotnet run --project $tool -c Release --no-build -- $dll --against $b[1] --quiet
        if ($LASTEXITCODE -ne 0) { throw "$($b[0]) references things its own game does not have." }
        dotnet run --project $tool -c Release --no-build -- $dll --against $b[2] --quiet --classload @gates
        if ($LASTEXITCODE -ne 0) { throw "$($b[0]): the entry point or a type's fields would fail on the other game." }
    }
}
else { Write-Warning 'tools\compat-check not found (the workspace next to this repo) - the DLLs were not checked against both games.' }
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
