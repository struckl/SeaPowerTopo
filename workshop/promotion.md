# Sea Power: Rats Topographic Map 1.3.0 – posts

Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3826122523 (item 3826122523, 1.3.0 uploaded 2026-10-27)
Source: https://github.com/struckl/SeaPowerTopo
Pictures, ready to attach in this order: `workshop/reddit/` (gallery, 1–9), `workshop/discord/` (BuOrd: 1–4,
MicroProse: 1–2). Made with [tools/shot-director/make_post_pictures.py](../tools/shot-director/make_post_pictures.py)
from the 4K grabs of the Workshop screenshots (Trollfjorden on the globe beta, build 261026): the game's FPS/CPU/GPU
overlay is painted out of the title bar, the aircraft and submarine pictures are cut from the 4K grab at full size so
the readout can be read on a phone. No other players' names or Discord overlay in any of them.

**Every post keeps the version paragraph** (marked below): one item for both game versions; the new parts (globe,
title bar buttons, mission editor) come with the globe map – on 0.8.7 the mod stays as in 1.2.0; and Anchor Chain
doesn't start on the public beta yet ([AnchorChain#19](https://github.com/seapower-modders/AnchorChain/issues/19), still
open on 2026-10-27), so beta players must not expect the mod to show up there today. Check the issue before posting –
drop or change that sentence once Anchor Chain is fixed.

---

## 1. Reddit – r/SeaPower_NCMA (image gallery post)

Gallery, in this order (captions go into the caption field of each image, 180 characters at most):

| # | File in `workshop/reddit/` | Caption |
| --- | --- | --- |
| 1 | `1-globe-standard.jpg` | The new 3D globe with the Topography layer in Standard: land by height, sea by depth. The red square is the play area of the Trollfjorden mission |
| 2 | `2-globe-combat-display.jpg` | The same view in Combat display, the navy console look for the night map – here with the depth readout over the Black Sea shelf |
| 3 | `3-looks.jpg` | 18 looks for the day and night map. Save any of them as a small text file, change the colours, and the map follows when you save |
| 4 | `4-title-bar.jpg` | New: the buttons sit in the map's title bar next to the map menu – Topography on/off, the look, Legend, and Cursor height on/off |
| 5 | `5-tromso.jpg` | The Lyngen Alps and the fjords around Tromsø (70° N). Contour lines follow the zoom; the cursor shows the terrain height |
| 6 | `6-lofoten-legend.jpg` | Lofoten and the shelf edge in Combat display, legend open: the height and depth bands in m and ft |
| 7 | `7-aircraft.jpg` | Select an aircraft: terrain at or above its altitude is hatched red. Under the cursor this ridge is 88 m above the F-4E at 1,200 m |
| 8 | `8-submarine.jpg` | Select a submarine: teal where it's below the thermal layer, red where the water is shallower than its depth; the readout gives the layer and the water under the keel |
| 9 | `9-mission-editor.jpg` | New: the mission editor's map gets the layer too, with the same buttons, legend and cursor readout |

No flair needed (the sub has none).

**Title**

Topographic Map 1.3.0 – land heights and sea depths on the new 3D globe map, buttons in the title bar, and now in the mission editor

**Body**

Sea Power's public beta of 26 October swapped the flat tactical map for a 3D globe – and the old version of this mod stopped working there. 1.3.0 is ready for it.

The stock map shows land as a satellite picture and all water in one flat colour. The **Topography** layer tints land by height and the sea by depth, with hill shading, contour lines that follow the zoom and depth lines like on a chart – for low-level flying, terrain masking, and knowing where your submarine has room to dive.

**New in 1.3.0**

- **On the 3D globe**: the layer lies exactly on the stock map at every zoom, at high latitudes, across the date line and over the poles
- **Buttons in the map's title bar**, next to the map menu (≡): Topography on/off, the look, Legend, and **Cursor height** – the height/depth readout next to the pointer can now be switched off (some of you asked for that)
- **Mission editor**: the editor's map gets the layer too, with the same buttons, legend and readout
- Lighter at high latitudes: a third of the pixels at 70° N for the same sharpness

**Still in**

- Height or depth under the cursor, in m and ft
- Select an aircraft: terrain at or above its altitude is hatched red, and the readout gives the clearance
- Select a submarine: where it is below the thermal layer, where the layer ends, water too shallow for it, and the water under the keel
- 18 looks for the day and night map (Standard, Atlas, ECDIS, paper sea chart, Combat display, Thermal imager, Neon …), plus your own as small theme files
- All heights and depths are the game's own terrain data – what the simulation uses

**Which game version?** *(version paragraph – keep)* Both. The Workshop item carries a build for 0.8.7 and one for the globe map, and the right one switches on by itself – nothing to do. On 0.8.7 the mod stays as it was in 1.2.0 (buttons at the bottom right, no editor); the new parts come with the globe map, for everyone with the 11 November release. **On the beta right now**, Anchor Chain itself doesn't start yet ([Anchor Chain issue #19](https://github.com/seapower-modders/AnchorChain/issues/19)), so no Anchor Chain mod loads there until it's updated – this one is ready for the moment it is. The screenshots are from the beta, taken with a test-only workaround.

Needs Anchor Chain. Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3826122523
Source (MIT): https://github.com/struckl/SeaPowerTopo

Screenshots: Trollfjorden (1985) and the mission editor on the globe beta, 4K. Found a bug? Please post `BepInEx\LogOutput.log` on the Workshop page.

---

## 2. Steam – Sea Power Modding sub-forum

https://steamcommunity.com/app/1286220/discussions/3/ – a reply in the mod's thread if there is one, else Start a New
Discussion. Steam threads take no attachments: the pictures are on the Workshop page.

**Title**

[Mod] Topographic Map 1.3.0 – land heights and sea depths on the new 3D globe map, now also in the mission editor

**Body** (Steam forum BBCode)

Sea Power's public beta of 26 October replaced the flat tactical map with a 3D globe. Topographic Map 1.3.0 is ready for it: the Topography layer tints land by height and the sea by depth, with hill shading, contour lines and depth lines.

[b]New in 1.3.0[/b]
[list]
[*][b]On the 3D globe[/b] – exactly on the stock map at every zoom, at high latitudes, across the date line and over the poles
[*][b]Buttons in the map's title bar[/b], next to the map menu (≡): Topography on/off, the look, Legend, and [b]Cursor height[/b] – the readout next to the pointer can now be switched off
[*][b]Mission editor[/b] – the editor's map gets the layer, the buttons, the legend and the readout
[*]Lighter at high latitudes: a third of the pixels at 70° N for the same sharpness
[/list]

Still in: height or depth under the cursor, terrain above the selected aircraft hatched red, the thermal layer and the water under the keel for the selected submarine, 18 looks and your own theme files.

[b]Which game version?[/b] Both – the item carries a build for 0.8.7 and one for the globe map, and the right one switches on by itself. On 0.8.7 the mod stays as in 1.2.0; the new parts come with the globe map (11 November release). [b]On the beta right now[/b] Anchor Chain doesn't start yet ([url=https://github.com/seapower-modders/AnchorChain/issues/19]Anchor Chain issue #19[/url]), so no Anchor Chain mod loads there until it's updated – this one is ready when it is.

Requires [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757]Anchor Chain[/url].

[url=https://steamcommunity.com/sharedfiles/filedetails/?id=3826122523]Workshop page[/url] · [url=https://github.com/struckl/SeaPowerTopo]Source code (MIT)[/url]

Bug reports welcome here or on the Workshop page – please attach BepInEx\LogOutput.log.

---

## 3. Discord – BuOrd: Naval Modding Community, `#sp-mods` (Naval Technicians)

https://discord.gg/vc8uAVH6Sh – attach `workshop/discord/1-globe-standard.jpg`, `2-title-bar.jpg`,
`3-mission-editor.jpg` and `4-looks.jpg` (the `< >` around the GitHub links keeps Discord from adding more preview
boxes; the Workshop link keeps its box with the thumbnail). Under Discord's 2000-character limit.

```
## Topographic Map 1.3.0 – ready for the new 3D globe map
Land heights and sea depths on the tactical map, now on the **3D globe** of the public beta:
- **On the globe**: exactly on the stock tiles at every zoom, at high latitudes, across the date line, over the poles
- **Buttons in the map's title bar** next to ≡: Topography, the look, Legend, **Cursor height** – the readout next to the pointer can now be switched off
- **Mission editor**: its map gets the layer, the buttons, the legend and the readout
- Still in: height/depth under the cursor, terrain above the selected aircraft hatched red, thermal layer + water under the keel for the selected sub, 18 looks + your own theme files

**One item for both versions**: a 0.8.7 build and a globe build, the right one switches on by itself. On 0.8.7 it stays as 1.2.0; the new parts come with the globe map (11 Nov release).
**Beta heads-up**: Anchor Chain doesn't start on the beta yet (<https://github.com/seapower-modders/AnchorChain/issues/19>), so no AC mod loads there until it's updated – this one is ready when it is.

Needs **Anchor Chain**.
**Workshop:** https://steamcommunity.com/sharedfiles/filedetails/?id=3826122523
**Source (MIT):** <https://github.com/struckl/SeaPowerTopo>
```

## 4. Discord – MicroProse (Official), Sea Power mods or showcase channel

https://discord.gg/CTDqUUJAVC – a mods / showcase channel if there is one, not the general chat. Attach
`workshop/discord/1-globe-standard.jpg` and `2-title-bar.jpg`.

```
Updated my **Topographic Map** mod for the new 3D globe map: land heights and sea depths on the globe, its buttons now in the map's title bar (the cursor height readout can be switched off), and the layer in the mission editor too. One Workshop item for both versions – on 0.8.7 it keeps working as before, and the globe build switches on by itself. Heads-up for beta players: Anchor Chain doesn't start on the beta yet, so AC mods (this one too) wait for its update. Needs Anchor Chain.
**Sea Power: Rats Topographic Map** – Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3826122523
```
