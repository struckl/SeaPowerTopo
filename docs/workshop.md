# Releasing on the Steam Workshop

The game uploads Workshop items itself: **Main menu → Mods → Create Mod**, signed in with the Steam account that
owns the item. No SteamCMD needed.

## Before a release

1. Bump the version in `src/SeaPowerTopo/Plugin.cs` (`Version`) and `src/SeaPowerTopo/SeaPowerTopo.csproj`
   (`<Version>`), add the entry to [CHANGELOG.md](../CHANGELOG.md).
2. Check `src/SeaPowerTopo/mod/_info.ini`: Mod Manager name and description, and `[Compatibility]`
   (`ApproximateVersion` = the game version tested against; it accepts later patch versions of that minor
   version).
3. Run the checklists in [testing.md](testing.md) on the current game version.
4. Close Sea Power, then stage and install the release folder:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Install
   ```

   This replaces `<Sea Power>\Sea Power_Data\StreamingAssets\SeaPowerTopo\` with exactly what players get.

## Upload

Start Sea Power → Mods → Create Mod:

| Field | Value |
| --- | --- |
| Existing item | first upload: none (creates the item); later: pick *Sea Power Topographic Map* |
| Folder | `StreamingAssets\SeaPowerTopo` (the folder `package.ps1 -Install` filled) |
| Name | Sea Power Topographic Map |
| Description | [workshop/description.bbcode](../workshop/description.bbcode) |
| Preview image | `StreamingAssets\SeaPowerTopo\preview.jpg` (put there by `package.ps1`: the picker only browses StreamingAssets and only takes .jpg; Steam wants it under 1 MB) |
| Tags | Scripting |
| Dependencies | `3380210757` (Anchor Chain) – shows as a required item on the Workshop page |
| Visibility | first upload **Hidden** (or Friends only); Public once the subscribed copy is tested |
| Change log | the CHANGELOG entry |

After the first upload, add the Workshop link to the README and the description, and the screenshots from
[workshop/screenshots/](../workshop/screenshots) on the Workshop page itself (Steam web → Edit → Add/edit images),
in their numbered order:

| Image | Shows |
| --- | --- |
| 01-relief | the Lyngen Alps and the fjords around Tromsø: height tint, hill shading, contours, cursor readout |
| 02-sea-depth | the shelf edge and the Lofoten Basin, legend open with heights and depths |
| 03-aircraft-clearance | an F-4E at 1,200 m: terrain above it hatched red, readout "90 m above F-4E …" |
| 04-submarine-layer | a Kobben at 100 m: thermal-layer tiles, water too shallow hatched red, keel readout |
| 05-overview | zoomed out over Scandinavia and the North Atlantic (the pre-drawn world layer) |
| 06-night-theme | the fjords in the map's night theme |
| 07-close-up | a fjord close in: 50 m contours, depth lines |
| 08-layers-menu | the Topography entry in the map's Layers menu |

## Remaking the screenshots

[tools/shot-director](../tools/shot-director) is a dev-only Anchor Chain plugin (never shipped) that takes commands
from `<Sea Power>\BepInEx\shotdirector.cmd` – load a mission, set the map centre and zoom, select a unit, hold it at
an altitude or depth, switch the theme, open the legend, move the mouse – and logs to `shotdirector.log`; the full
command list is at the top of `Director.cs`. Build it with `dotnet build tools/shot-director -c Release` (deploys to
`StreamingAssets\ShotDirector\`), start the game, write commands into the file, and grab the main monitor at full
resolution (the game's own `ScreenCapture` fails while the pop-out monitor is active). The current set was taken in
the mission *Trollfjorden* (NATO): Falk P995 selected for the plain map shots, F-4E 23rd TFS 'Fighting Hawks'-1
held at 1,200 m, S315 Kaura held at 100 m, map centres e.g. `center 69.45 19.45 9.15` (relief), `center 69.6 14.5
7.4` (sea), `center 66 8 5.3` (overview). Remove `StreamingAssets\ShotDirector\` afterwards.

## Testing the Workshop copy

Subscribe, then **remove your local `StreamingAssets\SeaPowerTopo` folder** (or rename it): with two copies only the
first one Anchor Chain finds starts, and the log says `Already loaded from another folder`. The subscribed copy lives
in `steamapps\workshop\content\1286220\<item id>\`. Start a mission and run the short checklist; then switch the
item to Public.

## Still to do for the first release

- A lower memory footprint for PCs with little RAM.
- Settings inside the game (most players won't edit the BepInEx config).
- Test matrix: 1080p / 1440p / 4K, UI scale, the game's own second-monitor map, terrain height multiplier, day and
  night theme, Better TacMap installed alongside.
