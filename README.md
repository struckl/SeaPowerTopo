# Sea Power Topographic Map

A mod for [Sea Power: Naval Combat in the Missile Age](https://store.steampowered.com/app/1286220/) that makes land
heights and sea depths readable on the tactical map – for low-level flying, terrain masking and radar horizons, and
for where a submarine has room to dive.

The stock map colours land only with its satellite-style texture and a faint slope shade, and paints all water one
flat colour: high ground is hard to judge and the seabed can't be seen at all. This mod adds a **Topography** layer:

- **Land tinted by height** in 13 bands (0 – 4000 m: greens, yellow, browns, grey, white), hill shading lit from the
  north-west, and contour lines whose step follows the zoom (20 m close in … 1000 m zoomed out, every fifth bold).
- **Sea tinted by depth** in 12 bands (0 – 6000 m), the deeper the darker, with faint seabed relief and a thin depth
  line at every band edge – banks, shelf edges and basins stand out like on a chart.
- **Height under the cursor** – terrain height in m and ft (or the sea depth) next to the mouse pointer.
- **Terrain above the selected aircraft** – with an aircraft or helicopter in the air selected, terrain as high as or
  higher than its altitude is hatched red, and the readout gives the clearance ("Viper 2-1 clears it by 210 m").
- **Thermal layer for the selected submarine** – teal where the submarine, at its current depth, is below the
  game's thermal layer (hidden from hull sonars above it), faint teal where the layer is deeper, a thin line where
  the layer area ends. Water shallower than the submarine is hatched red; the readout adds the layer depth and the
  water under the keel.
- **No drawing while zoomed out** – at mission start the area around all units and the whole world are drawn once
  in the background. Zoomed out they are simply shown; closer in the view is drawn live, and the pre-drawn colours
  fill the edges while it catches up with a pan.
- **Legend** behind a small button at the bottom right of the map, above the scale.

All heights and depths are the ones the simulation itself uses: the game's own terrain data (the DEM), including the
terrain height multiplier and under-water depth factor from Options → World. Unit symbols, waypoints and labels stay
on top; the stock coastline is untouched. A dimmed palette goes with the map's night theme.

## Installation

1. Install **[Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757)**, the Sea Power
   community mod loader, following its
   [installation guide](https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html) (one manual step:
   copy its preloader into the game folder).
2. Subscribe to Sea Power Topographic Map on the Steam Workshop *(link follows with the first release)*.
3. Start the game. In a mission, open the map: the layer is on. Toggle it in the map menu (≡) → Map Layers →
   **Topography**, or with **F6**.

Manual install instead of the Workshop: put `SeaPowerTopo.dll` and `_info.ini` into
`<Sea Power>\Sea Power_Data\StreamingAssets\SeaPowerTopo\`.

## Settings

`<Sea Power>\BepInEx\config\dev.sewerlabs.seapowertopo.cfg` (created on the first start; changes apply on the next
redraw):

| Setting | What it does |
| --- | --- |
| Toggle key | Key for the layer (default F6) |
| Opacity, Hill shading | How strongly the colours and the relief cover the stock map |
| Contour lines, Contour interval | Contours on/off; fixed step in metres or 0 = follows the zoom |
| Height bands, Depth bands | Lower edges of the colour bands in metres |
| Sea depth | Tint the sea by depth, or leave it to the stock map |
| Terrain above selected aircraft | The red highlight and clearance readout |
| Thermal layer for selected submarine | The thermal layer, shallow-water hatching and keel readout |
| Resolution | Overlay resolution relative to the map's (lower = faster) |
| Pre-draw mission area | Draw the mission area and the world once at mission start (less drawing while playing) |
| Height under cursor, Legend, Units | Readout on/off, legend button, metres / feet / both |

## Compatibility

- Sea Power 0.8.x. The game updates often; if an update moves something the mod hooks into, the affected part stops
  and the log says why – the game itself keeps running.
- Works together with Better TacMap (separate map layer numbers).
- No game files are changed. Uninstall by unsubscribing (or deleting the folder).

Problems: `<Sea Power>\BepInEx\LogOutput.log` has a line for every step the mod takes (`[Info :SeaPowerTopo]`);
please attach it to a bug report.

## Building from source

Requires the .NET SDK 8, Sea Power installed, and Anchor Chain subscribed (the build references the game's and
Anchor Chain's assemblies).

```bash
dotnet build src/SeaPowerTopo/SeaPowerTopo.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Sea Power"
```

The build copies the mod into `<Sea Power>\Sea Power_Data\StreamingAssets\SeaPowerTopo\` (`-p:Deploy=false` to
skip). `GameDir` can also be set as an environment variable. Release packaging and the Workshop upload are described
in [docs/workshop.md](docs/workshop.md); in-game test checklists are in [docs/testing.md](docs/testing.md).

Offline preview with the plugin's own colouring code and the game's terrain files – handy for tuning colours:

```bash
dotnet run --project tools/topo-preview -c Release -- --at 61.5,8 --span 2 --size 1600x900 --out norway.png
```

(`--at` lat,lon of the centre, `--span` view height in degrees; `--danger 1200` adds the aircraft highlight, `--dark`
the night palette, `--interval 100` a fixed contour step, `--nodepth` leaves the sea to the stock map,
`--depth-factor 2` is the game's under-water depth factor, `--sub 120` what a submarine at 120 m sees – with a
stand-in for the game's random layer depths, which need Unity – `--game <dir>` the game folder.)

## How it works

The stock map is a Unity scene of lon/lat quads that a camera renders into the texture the map window shows
(`SeaPower.LiveMap`). The mod adds transparent quads to that scene, so the overlay pans, zooms and wraps with the map
for free. Every layer is drawn the same way: heights copied out of the game's DEM on the main thread in 3 ms slices
per frame (the 30″ chunks the map has loaded, via `TerrainLoader.TryGetCached`; the 3′ world grid when zoomed out),
then resampled (Catmull-Rom when zoomed in past the DEM) and coloured on half the CPU cores (a 4K view in about
70 ms). The pre-drawn layers (mission area ×1, ×4, ×16 and the world at 0.1°, up to 8 megapixels each, with mipmaps)
stand in for the live layer while the view's scale is within 1.4× zoomed in or 3× zoomed out of theirs. The live
layer's image covers the view plus a margin (15–40 %, as much as an 8-megapixel budget allows) and is redrawn when
the view leaves it, when a pan has used up most of the margin, or when the zoom changes a lot. Layers in front cut
holes into the ones behind (their mesh is the image minus the others' rectangles), so the translucent colours never
stack. The thermal layer comes from the game's own tile cache where a tile is settled (`Environment._layerDepths`),
otherwise from the same rule and the same seeded `UnityEngine.Random` draw, with the generator's state put back.
The legend and the cursor readout are Noesis elements laid over the map control, handed over by a Harmony postfix
on `RenderedLayer.OnRender` that is applied only once NoesisGUI runs.

Depths are the simulation's: the DEM depth times the game's under-water depth factor, doubled once more where the DEM
is shallower than 48 m (as every terrain lookup of the game does) – so a thin ring of shallower water can sit just
outside a coastal strip. That is how the simulation sees the seabed, not a drawing error.

```
src/SeaPowerTopo/
  Plugin.cs, Patches.cs      entry point (Anchor Chain plugin); mission start/end hooks, the late map hook
  TopoHost.cs                per-frame driver: layer entry, toggle key, selected aircraft / submarine, which overlay
                             layers show and draw, pre-drawn areas from the units, legend/readout
  TopoLayer.cs               the Topography entry in the map's Layers menu
  Terrain/HeightReader.cs    reads DEM chunks and the world grid (main thread), height under a point
  Terrain/LayerMap.cs        the game's thermal layer per 0.2° tile
  Render/OverlayRenderer.cs  one overlay layer (live or pre-drawn): quad in the map scene, redraw pipeline, holes
  Render/Cutout.cs           an image minus the layers in front of it, as rectangles
  Render/Raster.cs           resampling, tint, hill shading, contours, red hatching, sea depth, thermal layer,
                             mipmaps (worker threads, plain C#)
  Render/Palette.cs          height and depth bands and their colours
  Ui/MapDecoration.cs        legend button, legend and cursor readout (Noesis)
  mod/_info.ini              Mod Manager entry
tools/topo-preview/          offline preview (links Raster.cs and Palette.cs)
tools/package.ps1            release build → dist/SeaPowerTopo/
```

## License

[MIT](LICENSE). Not affiliated with Triassic Games or MicroProse. The mod reads the game's terrain data at runtime;
nothing of the game is included in this repository or in the mod.
