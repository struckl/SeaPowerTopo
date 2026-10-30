# Changelog

## 1.4.1 – 2026-10-30 – fix: the mission editor on 0.8.7 drew only the coarse world grid

1.4.0 gave the flat map of 0.8.7 the mission editor layer, but the editor has no terrain manager on 0.8.7, so the mod's
request for the 20° DEM chunks went nowhere and the editor stayed on the 3 arc-minute world grid however far you zoomed in.
The mod now reads the chunks itself from the terrain files the game uses (`y?_x??.lz4`, inflated by the game's own LZ4; the
last six are kept) and redraws when they are in. **Run in the game on 0.8.7 (2026-10-30, with the Radio, Borders, Pop-Out
and other Workshop mods in the same folder set):** a mission (Trollfjorden) – the title bar buttons, the layer, the
live, mission and world layers drawn from the DEM, Topography and Cursor height buttons switch; the mission editor – map
found, world grid read from `lowres.raw`, the chunks read as the view zooms in (Tromsø, Lofoten, the Alps) and drawn
with contour lines at 500 / 200 / 1000 m, the buttons there too. Only the globe build was not run (the installed game
is 0.8.7); it is unchanged apart from the shared refactoring.

## 1.4.0 – 2026-10-30 – the flat map of 0.8.7 gets every feature of the globe build

Until now `SeaPowerTopoFlat.dll` (Sea Power 0.8.7) was the 1.2.0 code. It is built from the same source as the globe build
now (`-p:GameBuild=Flat`, `FLATMAP` defined; only the map drawing and a few game members differ), so on 0.8.7 there are:

- **the buttons in the map's title bar**: Topography on/off, the look (opens the list), Legend, Cursor height – instead of
  the legend and look buttons at the bottom right; where a map window has no title bar they sit at its top right;
- **Cursor height** button: the readout next to the pointer on/off, also with the layer off;
- **the mission editor**: its map gets the layer, the buttons, the legend and the readout. It has no map scene on 0.8.7 and
  the game loads no DEM for it, so the mod reads the world grid (`lowres.raw`) itself and asks the game's loader for the 20°
  chunks the view needs (the last six kept) – **not run in the game yet**;
- the readout and legend switches no longer make the overlay redraw; the aircraft highlight, the submarine layer, the 18 looks
  and theme files work as before.

The live map of a mission is drawn as in 1.2.0 (a quad in the map's scene), with the 1.3 fixes of the layer code. The globe
build is unchanged apart from the shared refactoring. Checked offline with `tools/compat-check` (own game: nothing missing;
other game: entry point and class load fine, `--classload`); the in-game steps are in `docs/testing.md`.

## 1.3.1 – 2026-10-28 – fix: 1.3.0 could stop every mission from loading on 0.8.7

**1.3.0 must not be used on 0.8.7.** Its globe DLL stays off there, but Unity's ECS start-up loads every type of every loaded
assembly, and two types of the globe DLL had a field of a type that exists only in the globe game (`GameApi`:
`Func<MapPalette, bool>`; the closure of `DemCache.TryGet`: `FileLayerSnapshot`). On 0.8.7 that throws a `TypeLoadException` at
start-up, ECS fails, and every mission fails to load. Seen in the game with the identical `GameApi` field of the Radio mod's
globe DLL (2026-10-28); the new check `tools/compat-check --classload` (run by `tools/package.ps1`) finds both fields in the 1.3.0
globe DLL and nothing in the flat DLL. Fixed: the first is a plain `Delegate`, the second is captured as `object`. The globe
DLL passes the check on both games now. **Run in the game on 0.8.7 (2026-10-28, Dangerous Straits 1985, with the Radio and Cities and
Borders mods in the same folder set):** the mission loads, the flat-map build runs and the globe build stays off, the layer
entry (#600) and the overlay in day and night colours are there, and the Standard/Legend buttons show (not clicked). The globe build is not re-run with
this change yet. The flat DLL is the unchanged 1.3.0 build and still logs 1.3.0.

## 1.3.0 – 2026-10-27 (Workshop; for both maps: 0.8.7 and the 3D globe)

**Known problem: on 0.8.7 missions may not load with this version - use 1.3.1.**

For the Sea Power version with the new 3D globe map – the public beta of 26 Oct 2026 (build 261026), due in the
11 November release – and still for 0.8.7. The package carries two builds: `SeaPowerTopo.dll` for the globe map and
`SeaPowerTopoFlat.dll` (the 1.2.0 code, from the `flat-map` branch) for the flat map of 0.8.7. Each stays off on the
game the other is for (one line in the log says which runs), so the Workshop item works on both and switches over by
itself when the globe map reaches the default branch. On the beta, 1.2.0 stops at the first mission
(`Update failed: MissingMethodException: … GizmoLayerManager.get_Instance()`) and shows nothing.

- **On the globe**: the overlay lies on the new spherical map, cut along the stock map's own 1° grid and drawn in the
  planes of its triangles, so it sits exactly on the stock tiles at every zoom, at high latitudes, across the date
  line and over the poles. Only the side of the globe facing the camera is drawn.
- **Fewer pixels at high latitudes**: the globe shows a degree of longitude cos(latitude) times as wide as one of
  latitude, and the images are now that much narrower – a third of the pixels at 70° N for the same sharpness.
- **Buttons in the map's title bar**, left of the map menu (≡): **Topography** (on/off), the **look** (opens the list of
  looks), **Legend**, **Cursor height** (the readout next to the pointer, on/off). The look list, the legend and the
  notices drop down below them. They replace the legend and look buttons at the bottom right.
- **Mission editor**: the editor's map gets the layer too, with the same buttons, legend and cursor readout.
- The **Cursor height** button is lit only while the readout really shows, and a click turns it on even with the layer
  off (setting *Always*) or off – before, it could look on while the readout was hidden with the layer.
- No double colours along the date line when the view reaches past it (a Pacific mission, the whole globe): a layer's
  hole for the one in front is cut a turn east and west as well.
- With the whole globe in view the pre-drawn world layer is enough – no live redraw of the whole globe.
- The globe map keeps its terrain on the graphics card only, so the mod reads what it needs itself: the 3′ world grid
  once (52 MB) and the 20° DEM chunks of the view the 3D terrain hasn't loaded (the last six, 11.5 MB each), from the
  files the game uses – a mod's replacements included.
- The cursor readout shows nothing off the edge of the globe.
- The readout and legend switches no longer make the overlay redraw.

## 1.2.0 – 2026-10-25 (Workshop)

- New night look **Black**: dark-grey relief on black with neutral grey contour lines (every fifth bold), a thin light
  coastline, and a sea in colour that fades to black in the deep. Themes can now also grade their land contour lines
  by height (no built-in look uses it yet).
- **Theme files**: make looks of your own as small text files in `BepInEx\config\SeaPowerTopo\themes`. They appear in
  the look list (marked "file") and the Shift+F6 cycle, start from any built-in look or another file with `Base=`, and
  are reloaded within a couple of seconds of saving. The look list has two new entries: **Save this look as a theme
  file** (writes the look on the map as an editable file and switches to it) and **Open the themes folder**. Bad lines
  and files are skipped with a line in the log; a file that breaks keeps its last working version. Format and keys:
  [docs/themes.md](docs/themes.md); the folder also gets a commented `_example.ini`.
- The two *Look* settings now take any id, including those of theme files (they used to be a fixed list).
- The offline preview takes `--theme-file`, `--themes-dir` and `--check-themes` (every built-in look written out and read
  back must draw exactly what the original does).

## 1.1.0 – 2026-10-24 (Workshop)

- **17 looks** for the layer, light and dark. The look button at the bottom right of the map (left of **Legend**)
  names the look and opens a list of the looks of the map's current theme; **Shift+F6** goes to the next one. By
  default the day map keeps **Standard** and the night map gets **Midnight**: cool, rich colours, stronger relief,
  glowing contour and depth lines.
  - Day: Standard, Atlas, Army tactical map, Paper sea chart, ECDIS day (IHO S-52), Parchment, Glacier.
  - Night: Midnight, Standard, Quiet, Atlas dusk, ECDIS dusk (IHO S-52), Combat display, Phosphor green, Red light,
    Thermal imager, Neon.
- The chart looks (Paper sea chart, ECDIS day / dusk) draw land flat with its contours and coastline, and the sea in
  four depth shades with contours at the standard chart depths and a bold safety contour (setting **Chart safety
  contour**, default 30 m). The legend shows the chart's key.
- New settings: **Look (day map)**, **Look (night map)**, **Look key**, **Chart safety contour**.
- The legend names the look; the aircraft highlight, the submarine layer and the cursor readout work in every look.
- The Standard looks are drawn exactly as before (checked byte for byte, day and night, with the aircraft highlight
  and the submarine layer on).
- The thermal layer is drawn softly: the game's 0.2° squares blend into each other as rounded patches instead of hard
  blocks. The change from one square to the next still lies on their common edge, and the thin outline where the
  layer area ends is as before.
- The offline preview takes `--theme`.

## 1.0.0 – 2026-10-24 (first Workshop release)

Tested on Sea Power 0.8.7.

- Topography layer in the map's Layers menu – "Topography (heights and depths)" – or F6: land tinted by height in
  13 bands, hill shading, zoom-dependent contour lines.
- Sea tinted by depth in 12 bands with seabed relief and depth lines at the band edges.
- Terrain height / sea depth under the cursor, from the simulation's own data.
- Terrain at or above the selected aircraft's altitude hatched red, with the clearance in the readout.
- Legend behind a button at the bottom right of the map.
- Night-theme palette.
- Thermal layer for a selected submarine: where it is below the layer, where the layer area ends, water too shallow
  for it, layer depth and water under the keel in the readout.
- Mission area and world drawn once at mission start: nothing is drawn while zoomed out, and the pre-drawn colours
  fill the edges while the live view catches up with a pan. Colouring on half the CPU cores (about 3× faster).
