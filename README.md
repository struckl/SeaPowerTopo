# Sea Power: R.A.T.S. Topographic Map

Land heights and sea depths on the tactical map of [Sea Power: Naval Combat in the Missile Age](https://store.steampowered.com/app/1286220/), for low-level flying, terrain masking, radar horizons and room to dive a submarine.

| | |
| --- | --- |
| Questions | [R.A.T.S. Discord](https://discord.gg/CzuHd4f8Yf): help, ideas, talk; the first place to ask |
| Version | `1.7.0` (2026-11-20) |
| Game | Sea Power 0.9.1 (build 261112) under Keel tested in the game; flat map build (0.8.7) run in the game with 1.5.0; other 0.9.x checked offline |
| Needs | a mod loader: [R.A.T.S. Keel](https://steamcommunity.com/sharedfiles/filedetails/?id=3835527459) or [Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757), either one |
| Get it | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3826122523) |
| Settings | the map's title bar menu, or `BepInEx\config\dev.sewerlabs.seapowertopo.cfg` |
| Bug reports | [R.A.T.S. feedback board](https://hub.sewerlabs.dev/s/topo) |
| Source | R.A.T.S. Source License, see `LICENSE` |

![The fjords around Tromso with the Topography layer](workshop/screenshots/03-closeup-standard.jpg)

## Features

- Land tinted by height in 13 bands (0 - 4000 m), hill shading lit from the north-west, contour lines that follow the zoom.
- The sea tinted by depth in 12 bands (0 - 6000 m), with faint seabed relief and a depth line at every band edge.
- 19 looks, light and dark, from Standard and Midnight to ECDIS, Survey sonar and Soviet General Staff map ([list](docs/features.md)).
- Your own looks from theme files, reloaded when you save ([docs/themes.md](docs/themes.md)).
- Terrain height or sea depth in m and ft next to the mouse pointer.
- With an aircraft selected, terrain at or above its altitude is hatched red, with the clearance in the readout.
- With a submarine selected, the thermal layer and shallow water under it are shown.
- One menu button in the map's title bar: Topography on/off, Look, Legend, Cursor height; also in the mission editor.
- Works on the 3D globe map at every zoom, over the poles and across the date line; the flat 0.8.7 map has its own build.
- Heights and depths are the ones the simulation uses: the game's own terrain data (DEM), no game files changed.
- The layer is display-only: the mission's own zones and rules decide what counts in play.

Full detail: [docs/features.md](docs/features.md).

## Install

1. Install a mod loader, either one: [R.A.T.S. Keel](https://steamcommunity.com/sharedfiles/filedetails/?id=3835527459) (subscribe, switch it on in the Mods menu, copy its one stub file once) or [Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757) with its [installation guide](https://seapower-modders.github.io/AnchorChain/install-anchor-chain.html).
2. Subscribe to [R.A.T.S. Topographic Map](https://steamcommunity.com/sharedfiles/filedetails/?id=3826122523) on the Workshop.
3. Start the game and open the map in a mission. The layer is on.
4. Topography on/off: the menu button or F6. Next look: Shift+F6.

Manual install: put `SeaPowerTopo.dll` and `_info.ini` into `<Sea Power>\Sea Power_Data\StreamingAssets\SeaPowerTopo\`. To uninstall, unsubscribe or delete the folder.

## Settings

| Setting | Default | What |
| --- | --- | --- |
| Toggle key | F6 | key for the layer |
| Look (day map), Look (night map) | Standard, Midnight | the look for each map theme; also set by the menu and Shift+F6 |
| Opacity, Hill shading | per look | how strongly colours and relief cover the stock map |
| Contour lines, Contour interval | on, follows zoom | contours on/off; a fixed step in metres |
| Sea depth | on | tint the sea by depth, or leave it to the stock map |
| Terrain above selected aircraft | on | red highlight and clearance readout |
| Thermal layer for selected submarine | on | thermal layer, shallow-water hatching, keel readout |
| Units | both | metres, feet or both |

- All settings: `<Sea Power>\BepInEx\config\dev.sewerlabs.seapowertopo.cfg`, created on the first start; changes apply on the next redraw. The full table is in [docs/features.md](docs/features.md).
- Bugs: link `<Sea Power>\BepInEx\LogOutput.log` (a line for every step, `[Info :SeaPowerTopo]`).

## Building

- Needs the .NET SDK 8, Sea Power installed and the [SeaPowerKeel](https://github.com/struckl/SeaPowerKeel) repo next to this one.
- Two game builds from one source tree: `-p:GameBuild=Flat` builds the 0.8.7 flat map DLL.

```bash
dotnet build src/SeaPowerTopo/SeaPowerTopo.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Sea Power"
```

Details, the offline preview tool and the source layout: [docs/building.md](docs/building.md). Releases: [docs/workshop.md](docs/workshop.md); how the overlay is drawn: [docs/how-it-works.md](docs/how-it-works.md).

## Credits and license

- [R.A.T.S. Source License](LICENSE): read it, fork it, send fixes; no re-uploads, no reuse in other projects.
- The mod reads the game's terrain data at runtime. Nothing of the game is included in this repository or in the mod.
- Works together with Better TacMap (separate map layer numbers).
- Not affiliated with Triassic Games or MicroProse.
