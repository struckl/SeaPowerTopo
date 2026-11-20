# R.A.T.S. Topographic Map: building

Build notes, the offline preview and the source layout. Short form in the [README](../README.md).

## Building

- Needs the .NET SDK 8, Sea Power installed, and the R.A.T.S. Shared repo next to this one (`SeaPowerShared`): the build compiles
  it into the DLL and takes Anchor Chain's library from its `lib/`.
- `GameDir` can also be set as an environment variable.

```bash
dotnet build src/SeaPowerTopo/SeaPowerTopo.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Sea Power"
```

- The build copies the mod into `<Sea Power>\Sea Power_Data\StreamingAssets\SeaPowerTopo\` (`-p:Deploy=false` skips that).
- `tools/package.ps1` builds the release package. It adds the flat-map build (`-p:GameBuild=Flat`, `FLATMAP`), compiled
  against a copy of 0.8.7's `Sea Power_Data\Managed` (`-FlatManagedDir`).
- Packaging and the Workshop upload: [workshop.md](workshop.md). In-game test checklists: [testing.md](testing.md).
- How the overlay is drawn: [how-it-works.md](how-it-works.md).

## Offline preview

Offline preview with the plugin's own colouring code and the game's terrain files, handy for tuning colours:

```bash
dotnet run --project tools/topo-preview -c Release -- --at 61.5,8 --span 2 --size 1600x900 --out norway.png
```

- `--at` is lat,lon of the centre, `--span` the view height in degrees, `--game <dir>` the game folder.
- `--globe` uses the globe map's narrower pixels (0.9 and later); without it the flat map's (0.8.7).
- `--theme <id>` takes any id from `src/SeaPowerTopo/Render/Theme.cs`; without it Standard by day, `--dark` Midnight.
- `--theme-file my.ini`, or `--themes-dir <folder>` with `--theme <id>`, previews theme files; `--check-themes` tests them.
- `--chart-safety 50`, `--danger 1200` (aircraft highlight), `--interval 100` (fixed contour step), `--nodepth` (sea left to the stock map).
- `--depth-factor 2` is the game's under-water depth factor; `--sub 120` shows what a submarine at 120 m sees, with a stand-in for the game's random layer depths.

## Layout

```
src/SeaPowerTopo/
  Plugin.cs, Hooks.cs        entry point (the Anchor Chain entry, which Keel runs too; ModEntry of the shared skeleton does the game version check,
                             settings, hooks and host); the late map hook
  ModInfo.cs                 what the shared skeleton reads (GUID, name, version, log source)
  Settings.cs                the config entries (Bind), the change counter the overlay redraws on
  TopoHost.cs                per-frame driver (ModHost): layer entry, keys, selected aircraft / submarine, which overlay
                             layers show and draw, pre-drawn areas from the units, title bar buttons/legend/readout, the
                             mission editor's map
  Terrain/HeightReader.cs    reads DEM chunks and the world grid (main thread), height under a point
  Terrain/LowresGrid.cs      the 3' world grid, read from the game's file
  Terrain/DemCache.cs        the DEM chunks the overlay draws from, read on a worker thread
  Terrain/EditorTerrain.cs   0.8.7 only: the mission editor's terrain folder, read by the mod itself
  Terrain/LayerMap.cs        the game's thermal layer per 0.2 degree tile
  Render/OverlayRenderer.cs  one overlay layer (live or pre-drawn): the map camera's view, mesh in the globe scene (or
                             a picture on the editor's map), redraw pipeline, holes
  Render/GlobeMesh.cs        an image's mesh on the stock map's globe
  Render/FlatMapRef.cs       the mission editor's flat map as a place to draw
  Render/Cutout.cs           an image minus the layers in front of it, as rectangles
  Render/Raster.cs           source grid, resampling, thermal layer tiles, mipmaps (worker threads, plain C#)
  Render/RasterShade.cs      tint, hill shading, contours, red hatching, sea depth, thermal layer per pixel
  Render/Look.cs             the look settings' defaults and how they scale a theme; chart depths
  Render/DemGrid.cs          the game's two height grids and where their samples sit
  Render/GeoRect.cs          a lon/lat rectangle
  Render/Palette.cs          height and depth bands and their colours
  Render/Theme*.cs           the looks that ship, theme files (format, loading)
  Ui/MapDecoration.cs        title bar buttons, look list, legend and cursor readout (Noesis)
  mod/_info.ini              Mod Manager entry
tools/topo-preview/          offline preview: compiles the plugin's plain C# Render files as they are
tools/package.ps1            release build -> dist/SeaPowerTopo/
tools/shot-director/         dev-only plugin that scripts the Workshop screenshots and in-game tests, and the scripts
                             for the Workshop preview and the post pictures (workshop.md)
tools/anchorchain-beta-shim/ dev-only BepInEx plugin that starts Anchor Chain on the globe beta (testing.md)
workshop/                    Workshop description, preview.jpg, screenshots/
```
