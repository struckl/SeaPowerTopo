# Changelog

## Unreleased (first Workshop release in preparation)

- Topography layer in the map's Layers menu (F6): land tinted by height in 13 bands, hill shading, zoom-dependent
  contour lines.
- Sea tinted by depth in 12 bands with seabed relief and depth lines at the band edges.
- Terrain height / sea depth under the cursor, from the simulation's own data.
- Terrain at or above the selected aircraft's altitude hatched red, with the clearance in the readout.
- Legend behind a button at the bottom right of the map.
- Night-theme palette.
- Thermal layer for a selected submarine: where it is below the layer, where the layer area ends, water too shallow
  for it, layer depth and water under the keel in the readout.
- Mission area and world drawn once at mission start: nothing is drawn while zoomed out, and the pre-drawn colours
  fill the edges while the live view catches up with a pan. Colouring on half the CPU cores (about 3× faster).
