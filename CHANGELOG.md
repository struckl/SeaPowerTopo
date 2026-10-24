# Changelog

## 1.2.0 – unreleased

- New night look **Black**: dark-grey relief on black with neutral grey contour lines (every fifth bold), a thin light
  coastline, and a sea in colour that fades to black in the deep. Themes can now also grade their land contour lines
  by height (no built-in look uses it yet).

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
