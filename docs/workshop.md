# Releasing on the Steam Workshop

The game uploads Workshop items itself: **Main menu → Mods → Create Mod**, signed in with the Steam account that
owns the item. No SteamCMD needed.

## Before a release

**Two builds in one package (1.3.0 on).** Sea Power 0.8.7 (flat map) and the globe map (public beta from 26 Oct 2026,
the 11 November release) need different code, so the package carries both: `SeaPowerTopo.dll` from this branch and
`SeaPowerTopoFlat.dll`, the 1.2.0 code with a version check, from the `flat-map` branch. Each stays off on the game the
other is for. `tools\package.ps1` builds the flat one in a temporary worktree against a copy of 0.8.7's
`Sea Power_Data\Managed` (default `E:\Project\Mods\_gamerefs\SeaPower-0.8.7-public\Managed`, or `-FlatManagedDir`). Run
the release checks on both game versions (Steam → Sea Power → Properties → Betas). Once the globe map is on the default
branch the flat-map build can go (`-NoFlat`).

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

## Upload from the command line (no game needed)

Updates an existing item through the running, signed-in Steam client – the same Steam calls as the game's Create Mod
screen, without starting the game and without a password. Steam must be running and signed in with the account that owns
the item (the tool refuses any other).

```powershell
dotnet build tools\workshop-upload -c Release
# read-only: shows the item's title, owner, visibility and last update
dotnet run --project tools\workshop-upload -c Release --no-build -- check 3826122523
# content folder + change note (+ optional new description, main picture, page pictures); title, tags, visibility stay
dotnet run --project tools\workshop-upload -c Release --no-build -- upload 3826122523 "<Sea Power>\Sea Power_Data\StreamingAssets\SeaPowerTopo" workshop\changenote.txt workshop\description.bbcode --preview workshop\preview.jpg --screenshots workshop\screenshots
```

`Result: k_EResultOK` = done; the entry shows on the item's Change Notes page. The same tool is in the Pop-Out and Radio
repos (1.2.0 of this mod went out with an in-game variant of it, 2026-10-25).

## Upload in the game

Start Sea Power → Mods → Create Mod:

| Field | Value |
| --- | --- |
| Existing item | pick *Sea Power Topographic Map* – Workshop item [3826122523](https://steamcommunity.com/sharedfiles/filedetails/?id=3826122523), first uploaded 2026-10-24 as 1.0.0 |
| Folder | `StreamingAssets\SeaPowerTopo` (the folder `package.ps1 -Install` filled). **Double-click** `SeaPowerTopo` so the picker is *inside* it, then confirm: the picker takes the folder it is in, a single click only highlights. Confirming in `StreamingAssets` itself gets "… is a forbidden path!" (the game refuses `StreamingAssets` and `StreamingAssets\original`) |
| Name | Sea Power Topographic Map |
| Description | [workshop/description.bbcode](../workshop/description.bbcode) |
| Preview image | `StreamingAssets\SeaPowerTopo\preview.jpg` (put there by `package.ps1`: the picker only browses StreamingAssets and only takes .jpg; Steam wants it under 1 MB) |
| Tags | Scripting |
| Dependencies | `3380210757` (Anchor Chain) – shows as a required item on the Workshop page |
| Visibility | first upload **Hidden** (or Friends only); Public once the subscribed copy is tested |
| Change log | [workshop/changenote.txt](../workshop/changenote.txt) – a few lines, plain ASCII. The Create Mod screen's change log field becomes unusable with long text (the full CHANGELOG entry froze it), so keep it short; details belong in CHANGELOG.md and the description |

After the first upload the Workshop link went into the README. Add the screenshots from
[workshop/screenshots/](../workshop/screenshots) on the Workshop page itself: open the item while signed in as its
owner (Steam client or browser) → right column **Owner Controls** → **Add/edit images & videos** → choose the files
(one at a time or several), then drag them into their numbered order:

| Image | Shows |
| --- | --- |
| 01-overview-standard | the whole globe in Standard, the mission's play area over the Norwegian Sea |
| 02-overview-combat | the same in Combat display (night map), depth readout over the Black Sea shelf |
| 03-closeup-standard | the Lyngen Alps and the fjords around Tromsø, readout "Terrain 1,090 m" |
| 04-closeup-combat | Lofoten and the shelf edge in Combat display, legend open, depth readout |
| 05-aircraft | an F-4E held at 1,200 m: terrain above it hatched red, readout "88 m above F-4E …" |
| 06-submarine | a Kobben held at 100 m: thermal layer, shallow water hatched red, layer and keel readout |
| 07-controls | the title bar buttons with the night map's look list open (top right, full resolution) |
| 08-editor | the mission editor's map over the Adriatic in Standard, depth readout |

All from the globe beta (build 261026) with `tools/shot-director` (`watermark off` hides the beta's version text,
`symbols off` the unit symbols for the preview); `preview.jpg` is the globe at minimum zoom in Standard, cut out of
the map with everything outside it black.

`screenshots/midnight-renders/` has five 2560x1440 pictures of the Midnight look (the night default of 1.1.0): fjords,
shelf edge, aircraft clearance, submarine layer, overview. They are renders from `tools/topo-preview` (the mod's own
drawing code over a flat stand-in for the stock map, no unit symbols or legend; the submarine one uses stand-in layer
depths), not in-game captures.

## Remaking the screenshots

[tools/shot-director](../tools/shot-director) is a dev-only Anchor Chain plugin (never shipped) that takes commands
from `<Sea Power>\BepInEx\shotdirector.cmd` – load a mission, set the map centre and zoom, select a unit, hold it at
an altitude or depth, switch the theme, open the legend, move the mouse – and logs to `shotdirector.log`; the full
command list is at the top of `Director.cs` (it also drives in-game tests: `waitload`, `play` to leave the briefing, `press TEXT` for a button or menu entry, `tree` to dump the UI). Build it with `dotnet build tools/shot-director -c Release` (deploys to
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
