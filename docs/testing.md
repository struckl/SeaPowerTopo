# In-game test checklists

Log: `<Sea Power>\BepInEx\LogOutput.log` (overwritten on every game start). Config:
`<Sea Power>\BepInEx\config\dev.sewerlabs.seapowertopo.cfg`. Installed mod (local build):
`<Sea Power>\Sea Power_Data\StreamingAssets\SeaPowerTopo\`.

## Topographic Map – terrain heights on the map (first build, 2026-10-23)

Separate mod (`Sea Power_Data\StreamingAssets\SeaPowerTopo\`, config
`BepInEx\config\dev.sewerlabs.seapowertopo.cfg`). Anchor Chain loads it whether or not it is ticked in the Mod Manager.

1. **Start Sea Power** (fresh start). Main menu → Mods: "Sea Power Topographic Map" is listed and ticked.
2. In the log: `Sea Power Topographic Map 0.1.0 loaded.` and, once the main menu is up, `Map hook in place (legend
   and cursor readout).` – no `[Error  :SeaPowerTopo]` lines.
3. Start a mission with mountains near the action (Norway, North Cape, Kola, Iceland, …). In the log: `Map layer
   'Topography (terrain heights)' added to the Layers menu (#600), on.`, `Overlay shader: Sprites/Default`,
   `Legend and cursor readout attached to the map.`
4. On the map:
   - Land is coloured by height: green lowlands, yellow, orange and brown hills, grey, white peaks; hill shading
     lit from the north-west; brown contour lines, every fifth bold. Water looks as before and the colour ends at
     the stock coastline.
   - Legend bottom left: the bands with the height each starts at (m · ft), "Contours every 100 m · 328 ft / Bold
     every 500 m · 1,640 ft" (the step depends on the zoom).
   - Zoom and pan: the overlay stays glued to the map; once you stop it sharpens within a fraction of a second;
     the contour step changes with the zoom (20 m close in … 1000 m zoomed far out). Unit symbols, waypoint lines
     and labels stay on top of it. No stutter while panning.
   - Mouse over land: a small box next to the pointer, "Terrain 1,240 m · 4,068 ft"; over water "Sea, 1,210 m ·
     3,970 ft deep"; gone when the pointer leaves the map. Zoomed far out it says "about …" (coarse 3′ data where
     the map hasn't loaded the fine DEM).
   - Map menu (≡ in the map window's title bar) → Map Layers → **Topography (terrain heights)**: unticking hides the
     overlay, legend and readout; **F6** does the same. The state survives a restart.
   - Map menu → Theme → Dark: darker colours, lighter thin lines.
5. Select an aircraft in the air (a helicopter: more than ~30 m above the ground): terrain as high as or higher
   than its altitude is hatched red with a red edge; legend line "As high as <name> (…) or higher". Readout:
   "<name> clears it by …", or in red "… above <name>". Change its altitude: the red area follows within a second
   or so. Deselect it, or select a ship: the red goes away.
6. Close the game normally and keep the log. With `Verbose log = true` the log gets one `[dbg] Overlay WxH, DEM …,
   N ms` line per redraw (size, data used, contour step, time from start to upload).

Worth reporting: heights that look wrong (compare with the 3D view or the readout), colour not lining up with the
coastline, bands hard to tell apart (config `Opacity`, `Height bands`), stutter while panning or zooming (config
`Resolution` lower), a readout box that sits awkwardly, any `[Error  :SeaPowerTopo]` line.

## Topographic Map – sea depth, legend button, more map around the view (2026-10-23)

Same mod, second build. The config file gets three new entries (`Sea depth`, `Depth bands`, `Legend open`); the old
`Legend corner` is no longer used.

1. **Start Sea Power**, a mission with open sea and some coast (Faroes, Iceland, Norway, GIUK gap). Topography on.
2. The sea:
   - Tinted by depth, the deeper the darker: close to the stock water colour, a shade lighter near the coasts,
     near black in the deep basins (day theme: light blue-grey to navy). Banks and seamounts show up as lighter
     patches, with faint relief.
   - Thin depth lines where the colour changes (light lines on dark water, dark ones on light water); none on the
     coastline itself. Zoomed far out they thin out where they would crowd together.
   - Mouse over water: the readout's depth fits the band the colour shows (legend). A thin ring of shallower water
     just outside the coastal strip is the game's own depth doubling for DEM depths under 48 m, not a glitch.
   - Config `Sea depth = false`: water back to the stock look.
3. Legend:
   - No legend in a corner any more; instead a small **Legend** button (three colour swatches) bottom right, its right
     edge in line with the map scale, just above it – not over the scale and not over the drawing toolbar.
   - Click it: the key opens above the button – "Terrain height" and "Sea depth" side by side, the contour step
     below. Click the key or the button again: it closes. Clicks on the button/key do nothing on the map behind.
   - Close and reopen the map, or restart the game: the key is open or closed as you left it.
   - Maximise / resize the map window: the button stays bottom right above the scale.
4. Panning: drag the map steadily in one direction. The coloured area should run ahead of the view – no uncoloured
   strip coming in at the edge (or only briefly when dragging very fast). Zooming still sharpens a moment after you
   stop. With `Verbose log = true` the `[dbg] Overlay WxH (margin 0.40) …` lines show the margin (0.15–0.40,
   smaller on very large map windows).

Worth reporting: sea colours that look off next to the stock map or hide the grid lines too much (config `Opacity`),
depth lines that are too busy, the button sitting in a bad spot (where?), uncoloured edges while panning.

## Topographic Map – pre-drawn layers and the submarine layer (2026-10-24)

Version 0.2.0. Turn on `Verbose log` in `dev.sewerlabs.seapowertopo.cfg` for this test: every redraw is logged
with its layer name and time.

1. **Start a mission.** Log: `Sea Power Topographic Map 0.2.0 loaded.`, then within a few seconds of the mission
   start `Pre-drawn layer 1: … m per pixel` (up to 3 of them) and `Pre-drawn layers for N units, plus the world at
   11 km per pixel.` With verbose log: `Overlay mission x1 …`, `mission x4`, `mission x16`, `world` once each.
2. **Zoomed out**: open the map and zoom out step by step until the whole ocean shows. The colours are simply
   there – no redraw lines in the log (`Overlay live …`) while you stay zoomed out and pan around the mission
   area. Further out the world layer takes over; colours and contour lines look the same as before, a little
   softer at some zoom levels.
3. **Zoomed in** (closer than about 350 km across on a 4K map): the live layer draws the view as before
   (`Overlay live …` lines, now about 3× faster). Pan quickly: the edge never shows uncoloured map – the
   pre-drawn colours are there first, the sharp ones follow. No doubled (darker) colours where layers meet.
4. **Submarine**: select a submarine. Its area gets the thermal layer: teal squares where the sub at its current
   depth is below the layer, faint teal where the layer is deeper, a thin line where the layer area ends (towards
   shallow water), red hatching and edge where the water is shallower than the sub. Dive or surface: the teal
   squares follow its depth. Cursor readout over the sea: `Layer at …` (or `No layer here`), `… of water under
   <sub>` (red `Bottom … above <sub>` where it's too shallow). Compare the layer depth under the sub with the
   game's own layer depth display for the selected unit – they should match. The legend (button bottom right)
   has a "Thermal layer · <sub> at … m" section. Deselect the sub: all of it disappears.
5. Day/night map theme switch: the pre-drawn layers redraw once in the new colours (verbose log), the map stays
   usable meanwhile.
6. Close the game normally – no `[Error  :SeaPowerTopo]` lines expected.

Worth reporting: stutter when the mission starts or when a pre-drawn layer finishes (a short hitch is the
texture upload), any visible seam or darker band where the sharp view ends, a layer depth that disagrees with
the game's own number.

## 1.0.0 – release check (2026-10-24)

Same build as 0.2.0 apart from the version and the menu text.

1. Log: `Sea Power Topographic Map 1.0.0 loaded.` and `Map layer 'Topography (heights and depths)' added to the
   Layers menu (#600)`.
2. Map menu (≡) → Map Layers: the last entry reads **Topography (heights and depths)**, ticked; unticking hides the
   overlay, legend button and readout, F6 brings them back. The on/off state from before the update is kept.
3. Run sections 2–4 of the 0.2.0 check above in any mission – no `[Error  :SeaPowerTopo]` lines.
