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
| Preview image | `workshop\preview.jpg` (the picker only takes .jpg; Steam wants it under 1 MB) |
| Tags | Scripting |
| Dependencies | `3380210757` (Anchor Chain) – shows as a required item on the Workshop page |
| Visibility | first upload **Hidden** (or Friends only); Public once the subscribed copy is tested |
| Change log | the CHANGELOG entry |

After the first upload, add the Workshop link to the README and the description, and more screenshots on the
Workshop page itself (Steam web → Edit → Add/edit images).

## Testing the Workshop copy

Subscribe, then **remove your local `StreamingAssets\SeaPowerTopo` folder** (or rename it): with two copies only the
first one Anchor Chain finds starts, and the log says `Already loaded from another folder`. The subscribed copy lives
in `steamapps\workshop\content\1286220\<item id>\`. Start a mission and run the short checklist; then switch the
item to Public.

## Still to do for the first release

- Screenshots: `workshop/preview.jpg` (Workshop preview) and `workshop/thumbnail.png` (Mod Manager picture), taken
  in game – land with contours, sea depth, the red aircraft highlight, the legend open.
- Rendering decision (pre-drawn world and mission area vs. GPU shader) and a lower memory footprint for PCs with
  little RAM.
- Settings inside the game (most players won't edit the BepInEx config).
- Test matrix: 1080p / 1440p / 4K, UI scale, the game's own second-monitor map, terrain height multiplier, day and
  night theme, Better TacMap installed alongside.
