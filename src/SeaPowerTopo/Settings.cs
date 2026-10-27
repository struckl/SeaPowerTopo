using System.IO;
using BepInEx;
using BepInEx.Configuration;
using SeaPowerTopo.Render;
using UnityEngine;

namespace SeaPowerTopo;

internal enum ReadoutMode { WithOverlay, Always, Off }

internal enum HeightUnits { Both, Metres, Feet }

/// <summary>
/// BepInEx config at <c>BepInEx/config/dev.sewerlabs.seapowertopo.cfg</c>. Sections are numbered so they sort in
/// the order a player would look for them. Changes made while the game runs apply on the next redraw.
/// </summary>
internal static class Settings
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<bool> VerboseLog;

    public static ConfigEntry<bool> Visible;
    public static ConfigEntry<KeyboardShortcut> ToggleKey;
    public static ConfigEntry<float> Opacity;
    public static ConfigEntry<float> Hillshade;
    public static ConfigEntry<bool> Contours;
    public static ConfigEntry<int> ContourInterval;
    public static ConfigEntry<string> Bands;
    public static ConfigEntry<bool> SeaDepth;
    public static ConfigEntry<string> DepthBands;
    public static ConfigEntry<bool> AircraftClearance;
    public static ConfigEntry<bool> SubmarineLayer;
    public static ConfigEntry<float> Resolution;
    public static ConfigEntry<bool> Predraw;
    public static ConfigEntry<string> DayLook;
    public static ConfigEntry<string> NightLook;
    public static ConfigEntry<KeyboardShortcut> LookKey;
    public static ConfigEntry<int> ChartSafety;

    public static ConfigEntry<ReadoutMode> Readout;
    public static ConfigEntry<bool> Legend;
    public static ConfigEntry<bool> LegendOpen;
    public static ConfigEntry<HeightUnits> Units;

    /// <summary>Bumped on every config change, so the overlay and the legend know to redraw.</summary>
    public static int Version { get; private set; }

    /// <summary>Makes everything that depends on the settings redraw (the theme files changed).</summary>
    public static void Touch() => Version++;

    // "id (Name: note), ..." for a config description; your own theme files are chosen by their Id as well.
    private static string Describe(bool dark) =>
        string.Join("; ", Themes.Of(dark).FindAll(t => !t.Custom).ConvertAll(t => $"{t.Id} ({t.Name}: {t.Note})")) +
        ". Theme files in BepInEx/config/SeaPowerTopo/themes add their own: use their Id";

    public static void Init()
    {
        var file = new ConfigFile(Path.Combine(Paths.ConfigPath, Plugin.Guid + ".cfg"), saveOnInit: true);

        Enabled = file.Bind("1. General", "Enable mod", true,
            "Master switch. Needs a restart.");
        VerboseLog = file.Bind("1. General", "Verbose log", false,
            "Write debug detail (every redraw of the overlay with its timing) to BepInEx/LogOutput.log.");

        Visible = file.Bind("2. Overlay", "Visible", true,
            "Whether the Topography map layer is on. Written by the mod when you toggle it in the map's " +
            "Layers menu or with the toggle key.");
        ToggleKey = file.Bind("2. Overlay", "Toggle key", new KeyboardShortcut(KeyCode.F6),
            "Turns the Topography map layer on and off (same as its entry in the map's Layers menu).");
        Opacity = file.Bind("2. Overlay", "Opacity", 0.7f,
            new ConfigDescription("How strongly the height colours cover the stock map (0 = invisible, 1 = opaque).",
                                  new AcceptableValueRange<float>(0.1f, 1f)));
        Hillshade = file.Bind("2. Overlay", "Hill shading", 0.6f,
            new ConfigDescription("Strength of the relief shading (light from the north-west). 0 turns it off.",
                                  new AcceptableValueRange<float>(0f, 1f)));
        Contours = file.Bind("2. Overlay", "Contour lines", true,
            "Draw contour lines; every fifth one is drawn bolder.");
        ContourInterval = file.Bind("2. Overlay", "Contour interval", 0,
            new ConfigDescription("Height step between contour lines in metres. 0 = automatic: the step grows as " +
                                  "you zoom out so the lines stay readable (20 m up to 1000 m).",
                                  new AcceptableValueRange<int>(0, 5000)));
        Bands = file.Bind("2. Overlay", "Height bands", "0,50,100,200,300,500,750,1000,1500,2000,2500,3000,4000",
            "Lower edges of the colour bands in metres, ascending, starting at 0. Each band gets its own colour " +
            "from green (low) over yellow and brown to white (high).");
        SeaDepth = file.Bind("2. Overlay", "Sea depth", true,
            "Shade the sea by depth – the stock map's water colour, lighter near the coast, darker the deeper – " +
            "with a thin depth line at every band edge (drawn with the contour lines) and faint seabed relief.");
        DepthBands = file.Bind("2. Overlay", "Depth bands", "0,50,100,200,300,500,1000,1500,2000,3000,4000,6000",
            "The depth each of the sea's colour bands starts at, in metres, ascending, starting at 0. Depths as the simulation " +
            "has them: the DEM's depth times the game's under-water depth factor (Options → World), and doubled " +
            "again where the DEM is shallower than 48 m.");
        AircraftClearance = file.Bind("2. Overlay", "Terrain above selected aircraft", true,
            "While an aircraft or helicopter is selected, terrain as high as or higher than its current altitude " +
            "is hatched red, and the cursor readout shows the clearance.");
        SubmarineLayer = file.Bind("2. Overlay", "Thermal layer for selected submarine", true,
            "While a submarine is selected: the game's thermal layer (tinted by its depth, outlined where it ends), " +
            "stripes where the submarine at its current depth is below it, water shallower than the submarine's " +
            "depth hatched red, and layer and water under the keel in the cursor readout.");
        Resolution = file.Bind("2. Overlay", "Resolution", 1f,
            new ConfigDescription("Overlay resolution relative to the map's own. Lower is faster, higher is sharper.",
                                  new AcceptableValueRange<float>(0.25f, 2f)));
        Predraw = file.Bind("2. Overlay", "Pre-draw mission area", true,
            "At mission start, draw the area of all units (and three times larger areas around it, and the whole " +
            "world) once. Zoomed out the map then needs no drawing while you play, and closer in the pre-drawn " +
            "colours fill the edges until the sharp view has caught up with a pan.");

        // No list of allowed values: the player's own theme files come and go while the game runs.
        DayLook = file.Bind("2. Overlay", "Look (day map)", Themes.DefaultId(false),
            "The theme the layer uses while the map is in its day theme. Also set by the look button " +
            "on the map and by the look key. Themes: " + Describe(false));
        NightLook = file.Bind("2. Overlay", "Look (night map)", Themes.DefaultId(true),
            "The theme the layer uses while the map is in its night theme. Also set by the look button " +
            "on the map and by the look key. Themes: " + Describe(true));
        LookKey = file.Bind("2. Overlay", "Look key", new KeyboardShortcut(KeyCode.F6, KeyCode.LeftShift),
            "Switches to the next theme of the map's current theme (day or night), or turns the layer on if it is off.");
        ChartSafety = file.Bind("2. Overlay", "Chart safety contour", 30,
            new ConfigDescription("For the chart themes (ECDIS, paper chart): the depth in metres drawn as the bold safety contour " +
                                  "and where the shallow water ends; the shallowest shade ends at a sixth of it, the deepest begins at " +
                                  "three and a third times it. Depths as the simulation has them.",
                                  new AcceptableValueRange<int>(5, 500)));

        Readout = file.Bind("3. Readout and legend", "Height under cursor", ReadoutMode.WithOverlay,
            "Show the terrain height under the mouse pointer on the map: WithOverlay (while the Topography layer " +
            "is on), Always, or Off. The Cursor height button in the map's title bar is lit while the readout shows and " +
            "switches it: off, or on (WithOverlay while the layer is on, Always while it is off).");
        Legend = file.Bind("3. Readout and legend", "Legend", true,
            "Show the Legend button in the map's title bar while the layer is on; it opens the colour key with the " +
            "contour interval.");
        LegendOpen = file.Bind("3. Readout and legend", "Legend open", false,
            "Whether the legend is open. Written by the mod when you click the legend button.");
        Units = file.Bind("3. Readout and legend", "Units", HeightUnits.Both,
            "Height units in the readout and the legend: Both (metres and feet), Metres or Feet.");

        // Turning the layer, the legend or the readout on and off is not a reason to redraw the overlay.
        file.SettingChanged += (_, e) =>
        {
            if (e.ChangedSetting != Visible && e.ChangedSetting != LegendOpen && e.ChangedSetting != Readout)
                Version++;
        };
    }
}
