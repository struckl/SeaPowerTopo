# Theme files

Since 1.2.0 you can make looks of your own. A theme is a small text file; the map shows it in the look list next to the
built-in looks (marked **file**) and in the **Shift+F6** cycle. Saving the file is enough: the map reloads it within
a couple of seconds, no restart.

The files live in

```
<Sea Power>\BepInEx\config\SeaPowerTopo\themes\
```

(the mod creates the folder on its first start and puts `_example.ini` into it, a commented file listing everything
below; files whose names start with an underscore are ignored).

## The quick way

1. Pick the look you like best as a starting point, on the map: look button → **Save this look as a theme file**.
   That writes the whole look as `my-<look>.ini` and switches the map to it. (**Open the themes folder** is right below.)
2. Open the file in any text editor, change a colour, save. Look at the map.

Or write a short file that starts from a look and only says what is different:

```ini
Id=warm-black
Name=Warm Black
Note=Black with warm contour lines
Base=black
LandLines=170,150,120
```

## Format

- One `Key=Value` per line. Lines starting with `;` or `#` are comments; keys are not case sensitive.
- **Colours** are `r,g,b` (0–255 each) or `#rrggbb`. **Lists** of colours are separated by `;`:
  `LandColours=#204020; 96,160,96; #f0f0e0`.
- A line you leave out keeps the value of `Base` (or a default if there is no `Base`).
  `none` removes a colour the base has (`Coast=none` draws no coastline).
- `Id` is how the look is named in the config and in `Base=` of other files: letters, digits, `-` and `_`, at most 40
  characters, unique. Without an `Id` the file name is used. It must not be the id of a look that ships with the mod.
- `Base` is the id of a built-in look (`black`, `midnight`, `atlas`, `ecdis-day`, … – the ids are in the config file's
  description of the two *Look* settings) or the `Id` of another theme file (up to 8 levels, no circles).
- `Dark=true` makes it a look for the map's night theme, `false` for its day theme. Left out, it follows `Base`
  (day, if there is none).

## Keys

Land: the height bands are the ones of the *Height bands* setting (13 by default, 0 to 4000 m).

| Key | What it does |
|---|---|
| `LandColours` | Key colours (at least 2), spread evenly from low land to high mountains |
| `LandStepped` | `true`: each band one flat colour; `false`: the colour runs on smoothly |
| `LandBrightness` | Multiplier for the land colours (0.05–3) |
| `LandFlat` | One flat colour for all land, instead of `LandColours` (charts use this) |
| `Relief` | Strength of the hill shading (0–1.5); the *Hill shading* setting scales it (its default 0.6 = the theme's own) |
| `ShadowColour`, `ShadowAmount` | What shadowed slopes fall towards, and how much (0–1). `none`: slopes just get darker |
| `HighlightColour`, `HighlightAmount` | The same for lit slopes |
| `LandLines` | Colour of the contour lines on land; `none` = the stock lines of the Standard look |
| `LandLinesMinor`, `LandLinesMajor` | Strength (0–1) of the thin lines and of every fifth, bold line |
| `LandLinesGlow` | A soft glow around the lines (0–1) |
| `LandLinesSpacing` | `1` = the usual spacing, `0.5` = twice as many lines (0.1–5) |
| `LandLinesHighColour`, `LandLinesHighAt`, `LandLinesLowStrength` | Lines that change with height: towards this colour, reaching it at this height in metres (10–9000), starting at this share of their strength at sea level |
| `Coast`, `CoastStrength` | A coastline on the land side of the shore (0–1); `Coast=none` for none |

Sea: the depth bands are the ones of the *Depth bands* setting (12 by default, 0 to 6000 m).

| Key | What it does |
|---|---|
| `SeaColours` | Key colours (at least 2), spread from the coast to the deepest water |
| `SeaStepped` | As `LandStepped`, for the sea |
| `SeaZones` | A chart: exactly four colours for very shallow, shallow, medium and deep water, instead of `SeaColours`. The *Chart safety contour* setting moves where they change |
| `SeaRelief` | Strength of the seabed shading (0–1.5) |
| `SeaLines`, `SeaLinesStrength`, `SeaLinesGlow` | Colour, strength (0–1) and glow (0–1) of the depth lines; `SeaLines=none` = the stock depth lines |
| `ShelfLines`, `ShelfBoost` | Colour of the line at 200 m and how much stronger it is (0.5–3) |
| `SafetyLine` | For charts: colour of the bold safety contour |

General: `Opacity` (0–1) is how much of the stock map the look covers at the default *Opacity* setting; the setting still
scales it.

Numbers outside their range are pulled into it; a value that is not a number or colour is ignored with a line in the log.

## When something is wrong

Nothing a theme file says can break the map. A line the mod cannot read is ignored, a file it cannot use at all is
skipped, and each of them gets a line in `BepInEx\LogOutput.log` (`[Warning:SeaPowerTopo] my-black.ini: …`); the map
shows "Theme files: 3 loaded, 1 with problems" after a reload. If you break a file that worked before, the map keeps
the last version that worked until you fix it. A look that is selected in the config but has no file any more shows
the default look (Standard by day, Midnight by night).

Limits: 100 files, 64 KB each.

## Sharing

A theme file is self-contained (unless it names another file as `Base`): copy it into someone else's themes folder.
Looks saved with **Save this look as a theme file** have no `Base`, so one file is all they need.

## For modders

The reader, writer and loader are plain C# without game types (`Render/ThemeIni.cs`, `Render/ThemeLibrary.cs`); the offline
preview uses them as they are:

```
dotnet run --project tools/topo-preview -- --theme-file mytheme.ini --out mine.png
dotnet run --project tools/topo-preview -- --themes-dir some\folder --theme child --out child.png
dotnet run --project tools/topo-preview -- --check-themes
```

`--check-themes` writes every built-in look out as a theme file, reads it back, and checks that it draws exactly what
the original does (and the same for a file that only names its `Base`), plus the saving, reloading and
keep-the-last-good-version behaviour.
