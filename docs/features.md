# R.A.T.S. Topographic Map: feature detail

Everything the mod does, in full. The short list is in the [README](../README.md).

## Land and sea

- Land is tinted by height in 13 bands (0 - 4000 m: greens, yellow, browns, grey, white).
- Hill shading is lit from the north-west.
- Contour lines follow the zoom: 20 m close in, 1000 m zoomed out, every fifth line bold.
- The sea is tinted by depth in 12 bands (0 - 6000 m), the deeper the darker.
  - Faint seabed relief and a thin depth line at every band edge make banks, shelf edges and basins stand out.
- Unit symbols, waypoints and labels stay on top. The stock coastline is untouched.

## Looks

- 19 looks, light and dark. The map theme you are in decides which list you see.
- Day looks: Standard, Army tactical map, Soviet General Staff map, Bathymetric chart, ECDIS day, Desert storm, Satellite,
  1940s chart, Glacier.
- Night looks: Midnight (the default), Standard, Black, Recon photo, Survey sonar, Combat display, Phosphor green, White
  phosphor, Amber display, Red light.
- ECDIS day draws land flat with contours and coastline, and the sea in four chart depth shades with a bold safety contour,
  in IHO S-52 colours. Survey sonar draws depth as a rainbow, like a multibeam survey.
- Pick a look in the title bar menu (Look), or press Shift+F6 for the next one.

## Looks of your own

- Look > Save this look as a theme file writes the current look to `BepInEx\config\SeaPowerTopo\themes`.
- Edit the file and save: the map reloads it at once.
- A file can start from another look with `Base=` and list only what differs.
- Format and every key: [themes.md](themes.md).

## Readouts and highlights

- The terrain height in m and ft (or the sea depth) shows next to the mouse pointer. The Cursor height entry switches it.
- With an aircraft or helicopter in the air selected, terrain as high as or higher than its altitude is hatched red.
  - The readout gives the clearance, for example "Viper 2-1 clears it by 210 m".
- With a submarine selected, the game's thermal layer is shown.
  - Teal tint: the submarine, at its current depth, is below the layer (hidden from hull sonars above it).
  - Faint teal: the layer is deeper. A thin line marks where the layer area ends.
  - Water shallower than the submarine is hatched red.
  - The readout adds the layer depth and the water under the keel.

## Map menu and where it works

- One menu button (the mountain mark) sits in the map's title bar, left of the map menu.
  - Its list has Topography on/off, Look, Legend and Cursor height.
  - Switches show On or Off and the list stays open. A switched-on entry is orange.
  - Look opens the list of looks in the same menu, with a Back row on top.
- The legend sits fixed at the map's top left, clear of the game's time controls.
- The mission editor's map gets the layer, the button, the legend and the cursor readout too.
- On the 3D globe map the overlay lies on the stock globe at every zoom, at high latitudes, across the date line and over
  the poles.
- At mission start the area around all units and the whole world are drawn once in the background, so nothing is drawn
  while zoomed out. Closer in, the view is drawn live.

## Where the data comes from

- Heights and depths are the ones the simulation uses: the game's own terrain data (the DEM).
- The terrain height multiplier and under-water depth factor from Options > World are applied.
- The layer is display-only. The mission's own zones and rules decide what counts in play.
- No game files are changed.

## Compatibility

- Declared for Sea Power 0.9.0 up to every 0.9.x (`_info.ini`).
- Run in the game on 0.9.1 (build 261112) under Keel, in Red Sea Breakout (1.6.2). Other builds, 0.9.3 included, were checked offline.
- Needs R.A.T.S. Keel, the mod loader, and R.A.T.S. Rigging, the part the R.A.T.S. mods share (a copy comes with this mod for now). [tools/anchorchain-beta-shim](../tools/anchorchain-beta-shim) started Anchor Chain on the
  globe beta for development and is no longer needed.
- The game updates often. If an update moves something the mod hooks into, the affected part stops and the log says why.
  The game keeps running.
- Works together with Better TacMap (separate map layer numbers).

## All settings

File: `<Sea Power>\BepInEx\config\dev.sewerlabs.seapowertopo.cfg`, created on the first start. Changes apply on the next redraw.

| Setting | What it does |
| --- | --- |
| Toggle key | Key for the layer (default F6) |
| Look (day map), Look (night map), Look key | The look for each map theme (Standard by day, Midnight by night; also set by the menu and Shift+F6). A look from a theme file is named by its `Id` |
| Chart safety contour | For the chart looks: the bold contour in metres (default 30). The shallowest water shade ends at a sixth of it, the deepest begins at three and a third times it |
| Opacity, Hill shading | How strongly the colours and the relief cover the stock map |
| Contour lines, Contour interval | Contours on/off; a fixed step in metres, or 0 to follow the zoom |
| Height bands, Depth bands | Lower edges of the colour bands in metres |
| Sea depth | Tint the sea by depth, or leave it to the stock map |
| Terrain above selected aircraft | The red highlight and clearance readout |
| Thermal layer for selected submarine | The thermal layer, shallow-water hatching and keel readout |
| Resolution | Overlay resolution relative to the map's (lower is faster) |
| Pre-draw mission area | Draw the mission area and the world once at mission start |
| Height under cursor, Legend, Units | Readout with the layer / always / off; the legend button; metres, feet or both |
