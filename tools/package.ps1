<#
.SYNOPSIS
    Builds a release of Sea Power Topographic Map and stages the folder that goes to the Steam Workshop.

.DESCRIPTION
    Release build without the automatic deploy, then dist\SeaPowerTopo\ with exactly what players get:
    SeaPowerTopo.dll, _info.ini, LICENSE.txt and the Mod Manager thumbnail (workshop\thumbnail.png, if present).
    With -Install the staged folder also replaces <GameDir>\Sea Power_Data\StreamingAssets\SeaPowerTopo\, the folder
    to pick in the game's Create Mod screen. Sea Power must be closed for that.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Install
#>
param(
    [string]$GameDir = $(if ($env:GameDir) { $env:GameDir } else { 'E:\Games\steamapps\common\Sea Power' }),
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\SeaPowerTopo\SeaPowerTopo.csproj'
$dist = Join-Path $root 'dist\SeaPowerTopo'

dotnet build $project -c Release -p:Deploy=false -p:GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$out = Join-Path $root 'src\SeaPowerTopo\bin\Release\netstandard2.1'
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $out 'SeaPowerTopo.dll') $dist
Copy-Item (Join-Path $root 'src\SeaPowerTopo\mod\_info.ini') $dist
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dist 'LICENSE.txt')
$thumbnail = Join-Path $root 'workshop\thumbnail.png'
if (Test-Path $thumbnail) { Copy-Item $thumbnail $dist } else { Write-Warning 'workshop\thumbnail.png missing - no Mod Manager picture.' }

$version = (Select-String -Path (Join-Path $root 'src\SeaPowerTopo\Plugin.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
Write-Host "Staged Sea Power Topographic Map $version in $dist"

if ($Install) {
    if (Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue) { throw 'Sea Power is running - close it first.' }
    $target = Join-Path $GameDir 'Sea Power_Data\StreamingAssets\SeaPowerTopo'
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Copy-Item -Recurse $dist $target
    Write-Host "Installed to $target - pick this folder in Mods > Create Mod."
}
