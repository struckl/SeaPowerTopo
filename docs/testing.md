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

## Themes – 17 looks and the look button (1.1.0, 2026-10-24)

Version 1.1.0. Turn on `Verbose log` in `dev.sewerlabs.seapowertopo.cfg` for this test.

1. **Start a mission** with the map in its night theme. Log: `Sea Power Topographic Map 1.1.0 loaded.` The layer
   shows **Midnight** (deep blue-black sea with glowing cyan depth lines, teal-green to rust land, cool shadows).
2. **Look button**: bottom right, left of **Legend**, reads "Midnight". Click it: a list "Look for the night map" with
   10 looks, each with three colour swatches, Midnight in bold. Click **Quiet**: the list closes, a line "Quiet:
   grey land, colour on high ground" shows above the buttons for a couple of seconds, the layer redraws (verbose log:
   new `Overlay …` lines), the button reads "Quiet". The config file now has `Look (night map) = quiet`.
3. **Shift+F6** goes to the next look (Atlas dusk, ECDIS dusk, Combat display, Phosphor green, Red light, Thermal
   imager, Neon, Midnight, Standard, Quiet …) and shows its name; F6 alone still toggles the layer – Shift+F6 must not
   toggle it. With the layer off, Shift+F6 turns it on without changing the look.
4. Go through every look once on a coast: nothing missing (no transparent holes, no flat colour where there should be
   relief), land and sea clearly apart, the legend (open it) shows that look's key. **ECDIS dusk / paper chart**: land
   flat with brown contour lines and a coastline, sea in four shades, a bold safety contour at 30 m (change `Chart
   safety contour` in the config to 60: the contour and the shade edges move after the next redraw).
5. **Standard (night)** must look exactly like the 1.0.0 night look.
6. **Day map**: switch the map to its day theme. The look button reads "Standard" and its list has 7 looks (Standard,
   Atlas, Army tactical map, Paper sea chart, ECDIS day, Parchment, Glacier); the night choice is remembered for when
   the map goes dark again.
7. With an aircraft selected (altitude highlight) and with a submarine selected (thermal layer, keel line): both
   still show in at least Midnight, Atlas and ECDIS dusk, with their legend rows. The thermal layer's tint is made of
   soft rounded patches, not hard squares; where the submarine's depth crosses a square's layer depth the tint
   switches at the square's edge (check with the cursor readout `Layer at …` on both sides of an edge).
8. Zoom out to the whole world and back, pan: the pre-drawn layers redraw once after a look change (a second or so),
   no error lines.
9. Close the game normally – no `[Error  :SeaPowerTopo]` lines expected.

Worth reporting: a look that is too dark or too bright to use, the picker or the notice overlapping the legend or the
map scale, the look button text cut off, a look change that doesn't redraw the pre-drawn layers, the picker staying
open.

## Black look (1.2.0, 2026-10-25)

Version 1.2.0. Everything of the 1.1.0 checklist above still applies; this adds the new look.

1. Night map, look button → the list now has 11 looks, **Black** after **Quiet**. Pick it (or Shift+F6 to it).
2. Land: dark-grey relief on black with neutral grey contour lines (not white), every fifth line bolder. A thin
   light coastline. The sea is blue-teal in the shallows, fading to black in the deep, with light blue depth lines
   and a brighter line at the shelf edge.
3. With an aircraft selected (red hatching above its altitude) and with a submarine selected (thermal layer, keel
   line): both stay clearly visible on the black.
4. Unit symbols and labels stay readable on it.

## Theme files (1.2.0, 2026-10-25)

Needs a mission with a map. The offline checks (`dotnet run --project tools/topo-preview -- --check-themes`) already
cover reading, writing, inheriting, saving, reloading and the broken-file cases; this is what only the game shows.

1. After the first start with 1.2.0 the folder `<Sea Power>\BepInEx\config\SeaPowerTopo\themes` exists and holds
   `_example.ini`; `LogOutput.log` has `Theme files: 0 loaded from …`.
2. Night map, look button: below the looks there is a thin line and two italic entries, **Save this look as a theme
   file** and **Open the themes folder**. The second opens the folder in the file explorer.
3. Choose **Midnight**, then **Save this look as a theme file**: a notice (about 7 s, readable, wrapped) says
   `Saved my-midnight.ini …`, the look button now reads **My Midnight**, and the map looks exactly as before. The list now
   has **My Midnight** with a small "file" tag, in bold.
4. Open `my-midnight.ini`, change `LandLines=` to `255,0,0`, save – within about 2 seconds the notice says
   `Theme files: 1 loaded` and the contour lines on land turn red. No restart, no error lines.
5. Write an invalid colour (`LandLines=banana`) and save: the notice says `1 with problems`, the map keeps the red lines
   (the last version that worked), the log has `my-midnight.ini: 'LandLines=banana' is not usable here, ignored`.
   Break the whole file (delete every line but `Id=my-midnight`): the map keeps the last working version, the log says
   so. Fix it: normal again.
6. Save the same look again: a second file `my-midnight-2.ini`, **My Midnight 2** in the list.
7. A new file with just `Id=thin`, `Base=black`, `LandLines=170,150,120` (copy it into the folder while the game runs): it
   appears in the list within 2 seconds and shows Black with warm lines. Shift+F6 goes through it.
8. Delete the file of the look that is selected: the map falls back to Midnight (log line, no error); the look button
   updates.
9. Switch the map to its day theme: the file looks made from night looks are not in its list; a day look saved as a file
   shows up only there.
10. Close the game: the config's `Look (night map)` holds the id of the look that was selected; start again – it is
    still selected.

Worth reporting: a notice that is cut off or hides the buttons, the picker staying open after a click, the folder not
opening, a file change that is not noticed, a redraw that flickers or shows the old colours.

## 3D globe map, title bar buttons, mission editor (1.3.0, 2026-10-27)

Needs Sea Power on the **public beta** with the globe map (Steam → Sea Power → Properties → Betas → "stable – Public
Beta", build 261026 or later).

**Anchor Chain on the beta.** Anchor Chain 1.1.0 doesn't start there: its preloader looks for the mod folders before
the game lists them, the log ends with `Could not find AnchorChain chainloader`, and no Anchor Chain mod loads
([seapower-modders/AnchorChain#19](https://github.com/seapower-modders/AnchorChain/issues/19)). It also loops forever
(the game freezes) once any mod's entry point throws, which some mods do on the beta. Until it is fixed, test with the
dev shim: `dotnet build tools/anchorchain-beta-shim -c Release` puts `AnchorChainBetaShim.dll` into
`<Sea Power>\BepInEx\plugins`; it starts Anchor Chain once the start-up load screen is done and ends the stuck loop. To
test this mod alone, `<Sea Power>\BepInEx\config\dev.sewerlabs.anchorchainbetashim.only.txt` with the lines
`SeaPowerTopo` and `ShotDirector` limits Anchor Chain to those folders. Delete the DLL (and that file) to go back.
If the game was killed during a start, the next start asks whether to disable all mods: answer **Ignore**.

1. Start the game. Log: `Sea Power Topographic Map 1.3.0 loaded.`, `Map hook in place …`. On the old game (0.8.7) the
   log says instead that 1.3.0 needs the globe map, and nothing else happens.
2. Start a mission (Trollfjorden), open the map. Log: `Map layer 'Topography (heights and depths)' added …`,
   `Topography buttons placed in the map's title bar.`, `World height grid read from …lowres.raw`, `Pre-drawn layer …`.
3. Title bar, left of ≡: **Topography** (lit), the look's name, **Legend**, **Cursor height** (lit). Topography turns the
   layer off and on (so does F6 and the Layers menu entry; all three stay in step). The look button opens the list of
   looks below it; Legend opens the key below the look button, clear of the time controls of a full-screen map.
   Cursor height switches the readout next to the pointer off and back on.
4. Zoom in on a coast (Tromsø, 69.65° N 18.95° E, to a few km across) and toggle the layer: the coastline, islands and
   fjords of the overlay sit exactly on the stock map's. Pan and zoom: the overlay turns with the globe, sharpens a
   moment after you stop.
5. Zoom out until the whole globe shows: the overlay covers the side facing you only – nothing mirrored or doubled,
   nothing from the far side shining through at the edge. Turn the globe: the overlay follows without gaps.
6. Across the date line (52° N 175° E, Aleutians) and over the North Pole: the overlay is continuous, no seam or gap
   along 180° or around the pole.
7. Mouse over land and sea: "Terrain …" / "Sea, … deep"; off the edge of the globe: nothing.
8. Select an aircraft in the air: terrain at or above its altitude hatched red, the readout gives the clearance.
   Select a submarine: thermal layer tint and stripes, shallow water hatched red, layer depth and water under the keel
   in the readout.
9. Main menu → Mission Editor: the editor's map shows the layer (Standard look), the four buttons at its top right, the
   legend below them; north is up (Split's mainland north of its islands), the readout works. Zoom in: the view sharpens
   to DEM detail (the chunks are read in the background, a moment after the first coarse picture). Back to the menu:
   log `Mission editor map closed.`

Worth reporting: any `[Error  :SeaPowerTopo]` line, the overlay off the coastline by more than a pixel when zoomed in,
anything on the far side of the globe, a button row that overlaps the game's own buttons, a drop-down hidden behind
something, stutter while turning the zoomed-out globe.

### The combined package (1.3.0)

`powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Install` puts both builds into
`StreamingAssets\SeaPowerTopo` (`SeaPowerTopo.dll`, `SeaPowerTopoFlat.dll`). Start the game on each branch:

- **Globe beta:** the log has `Sea Power Topographic Map 1.3.0 loaded.` and `This game has the 3D globe map: the globe
  build runs, the flat-map build stays off.`; Anchor Chain lists both plugins (`dev.sewerlabs.seapowertopo` and
  `…seapowertopo.flatmap`). The checklist above applies.
- **0.8.7 (default branch):** `This game has the flat map: the flat-map build runs, the globe build stays off.` and
  `Sea Power Topographic Map (flat map) 1.4.0 loaded.`; a mission shows the overlay and the Topography / look / Legend /
  Cursor height buttons in the map's title bar (or, without one, at the map's top right: `No title bar on this map`), the
  cursor readout toggles with its button, no `[Error  :SeaPowerTopo]` line. Then the checklist above (looks, legend,
  aircraft highlight, submarine layer). **Mission editor** (new on 0.8.7 in 1.4.0; run in the game 2026-10-30, see the changelog): open a
  mission in the editor – `Mission editor map found` in the log, the layer on the map, the same buttons; zoom in past the
  stock tiles' detail and check the heights are drawn (the editor loads no DEM itself: the mod asks the game's loader for
  the chunks, up to six); leave the editor and start a mission: `Mission editor map closed`.
