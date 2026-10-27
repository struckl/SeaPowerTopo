# Changelog

## 1.3.0 – unreleased (for the 3D globe map)

For the Sea Power version with the new 3D globe map: the public beta of 26 Oct 2026 (build 261026), due in the
11 November release. On the older game it stays off with a line in the log; 1.2.0 is the one for 0.8.7. On the beta,
1.2.0 stops at the first mission (`Update failed: MissingMethodException: … GizmoLayerManager.get_Instance()`) and
shows nothing.

- **On the globe**: the overlay lies on the new spherical map, cut along the stock map's own 1° grid and drawn in the
  planes of its triangles, so it sits exactly on the stock tiles at every zoom, at high latitudes, across the date
  line and over the poles. Only the side of the globe facing the camera is drawn.
- **Fewer pixels at high latitudes**: the globe shows a degree of longitude cos(latitude) times as wide as one of
  latitude, and the images are now that much narrower – a third of the pixels at 70° N for the same sharpness.
- **Buttons in the map's title bar**, left of the map menu (≡): **Topography** (on/off), the **look** (opens the list of
  looks), **Legend**, **Cursor height** (the readout next to the pointer, on/off). The look list, the legend and the
  notices drop down below them. They replace the legend and look buttons at the bottom right.
- **Mission editor**: the editor's map gets the layer too, with the same buttons, legend and cursor readout.
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
