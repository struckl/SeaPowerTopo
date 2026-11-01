using System;
using System.Collections.Generic;

namespace SeaPowerTopo.Render;

/// <summary>
/// A colour theme for the overlay as a plain parameter set: a land tint (key colours spread evenly over the height
/// bands, in steps or blended, or one flat colour), relief shading with tinted shadows and highlights, contour lines
/// with an optional glow, a coastline, a sea tint (key colours over the depth bands, or the four depth shades of a
/// chart) and depth lines. Plain C# (no Unity types), so the offline preview draws with the very same code.
/// <para>Each theme belongs to the map's day or night theme: the map shows the themes of the one it is in, and a
/// switch goes through those.</para>
/// </summary>
internal sealed class Theme
{
    public string Id, Name, Note;
    /// <summary>A theme for the map's night theme (otherwise for its day theme).</summary>
    public bool Dark;
    /// <summary>
    /// The first release's look, drawn by its original rules (no tinted shadows, depth lines that follow the band
    /// colour, the user's hill shading as it is). Its output must not change.
    /// </summary>
    public bool Legacy;
    /// <summary>Read from a theme file by the player (not one that ships).</summary>
    public bool Custom;
    /// <summary>
    /// How much of the stock map the theme covers at the default opacity setting (0.7); the user's setting scales it
    /// (a setting of 0.35 halves it). The Standard themes use the setting as it is.
    /// </summary>
    public float Opacity = 1f;

    // Land
    public int[][] LandKeys;
    public bool LandStepped;
    public float LandBright = 1f;
    public int[] LandFlat;
    public float Shade = 0.6f;
    public int[] ShadowTint, HighlightTint;
    public float ShadowAmt, HighlightAmt;
    public int[] LandLine;
    public float LandMinor, LandMajor, LandHalo, LandIntervalScale = 1f;
    /// <summary>
    /// Contour lines graded by height: from <see cref="LandLine"/> at sea level to this colour at <see cref="LandLineSpan"/>
    /// metres and above, and from <see cref="LandLineLowAmt"/> of their strength to the full strength. Null = every
    /// line the same.
    /// </summary>
    public int[] LandLineHigh;
    public float LandLineSpan = 1500f, LandLineLowAmt = 1f;
    public int[] Coast;
    public float CoastAmt;

    // Sea
    public int[][] SeaKeys;
    public bool SeaStepped;
    /// <summary>Chart themes: the four depth shades (very shallow, shallow, medium, deep) instead of <see cref="SeaKeys"/>.</summary>
    public int[][] Zones;
    public float SeaShade = 0.4f;
    public int[] SeaLine, ShelfLine, SafetyLine;
    public float SeaLineAmt, SeaLineHalo, ShelfBoost = 1f;

    public bool IsChart => Zones != null;

    public Theme Clone() => (Theme)MemberwiseClone();
}

/// <summary>The themes that ship, by map theme, and the order a switch goes through them.</summary>
internal static class Themes
{
    private static int[] C(int r, int g, int b) => new[] { r, g, b };

    private static int[][] K(params int[][] keys) => keys;

    public static readonly List<Theme> Light = new List<Theme>();
    public static readonly List<Theme> Dark = new List<Theme>();

    /// <summary>Counts every change to the custom themes, so a list built from them knows to rebuild.</summary>
    public static int Revision { get; private set; }

    public static string DefaultId(bool dark) => dark ? "midnight" : "standard-day";

    /// <summary>The theme that ships with this id (of either map theme), or null.</summary>
    public static Theme Builtin(string id) => Light.Find(t => !t.Custom && t.Id == id) ?? Dark.Find(t => !t.Custom && t.Id == id);

    /// <summary>Any theme with this id, built in or from a file, or null.</summary>
    public static Theme Find(string id) => Light.Find(t => t.Id == id) ?? Dark.Find(t => t.Id == id);

    /// <summary>Swaps the themes from files for these (each into the list of its map theme, after the built-in ones).</summary>
    public static void ReplaceCustom(List<Theme> custom)
    {
        Light.RemoveAll(t => t.Custom);
        Dark.RemoveAll(t => t.Custom);
        foreach (Theme t in custom)
            (t.Dark ? Dark : Light).Add(t);
        Revision++;
    }

    public static List<Theme> Of(bool dark) => dark ? Dark : Light;

    /// <summary>The theme with this id among those of the map theme; the default if there is none.</summary>
    public static Theme Get(string id, bool dark)
    {
        List<Theme> list = Of(dark);
        foreach (Theme t in list)
        {
            if (t.Id == id)
                return t;
        }
        return list.Find(t => t.Id == DefaultId(dark)) ?? list[0];
    }

    /// <summary>The theme after <paramref name="current"/> among those of its map theme.</summary>
    public static Theme Next(Theme current)
    {
        List<Theme> list = Of(current.Dark);
        int at = list.IndexOf(current);
        return list[(at + 1) % list.Count];
    }

    public static string[] Ids(bool dark) => Of(dark).ConvertAll(t => t.Id).ToArray();

    // Day-for-night grading of a palette: saturation, a tint pulled over it, then brightness.
    private static int[][] Grade(int[][] keys, float bright, float sat, int[] tint, float tintAmt)
    {
        var o = new int[keys.Length][];
        for (int i = 0; i < keys.Length; i++)
        {
            float r = keys[i][0], g = keys[i][1], b = keys[i][2];
            float gray = 0.3f * r + 0.59f * g + 0.11f * b;
            r = gray + (r - gray) * sat;
            g = gray + (g - gray) * sat;
            b = gray + (b - gray) * sat;
            if (tint != null)
            {
                r += (tint[0] - r) * tintAmt;
                g += (tint[1] - g) * tintAmt;
                b += (tint[2] - b) * tintAmt;
            }
            o[i] = new[] { Clamp((int)(r * bright)), Clamp((int)(g * bright)), Clamp((int)(b * bright)) };
        }
        return o;
    }

    // The mean of two palettes with the same number of keys.
    private static int[][] Mean(int[][] a, int[][] b)
    {
        var o = new int[a.Length][];
        for (int i = 0; i < a.Length; i++)
            o[i] = new[] { (a[i][0] + b[i][0]) / 2, (a[i][1] + b[i][1]) / 2, (a[i][2] + b[i][2]) / 2 };
        return o;
    }

    private static int Clamp(int v) => Math.Max(0, Math.Min(255, v));

    static Themes()
    {
        // The first release's colours: thirteen height bands from lowland green over yellow and browns to rock grey
        // and snow white, five sea ramp stops from the stock map's own water colour to near black, in steps.
        int[][] standardLand = K(C(78, 146, 76), C(118, 170, 84), C(162, 194, 100), C(204, 214, 118), C(240, 226, 138), C(244, 196, 112),
                                 C(230, 156, 84), C(206, 120, 64), C(174, 92, 54), C(140, 72, 58), C(120, 84, 104), C(168, 160, 174), C(250, 250, 250));

        // ------------------------------------------------------------------------------------------------- light
        Light.Add(new Theme
        {
            Id = "standard-day", Name = "Standard", Note = "height and depth colours", Legacy = true, Opacity = 0.7f,
            LandKeys = standardLand, LandStepped = true,
            SeaKeys = K(C(214, 228, 236), C(168, 190, 208), C(118, 144, 172), C(66, 92, 130), C(22, 40, 76)), SeaStepped = true,
        });

        Light.Add(new Theme
        {
            Id = "army-map", Name = "Army tactical map", Note = "printed hypsometric tints",
            LandKeys = K(C(172, 214, 150), C(188, 222, 150), C(206, 228, 152), C(226, 232, 152), C(240, 232, 150), C(244, 218, 140),
                         C(238, 198, 130), C(224, 174, 120), C(206, 152, 110), C(188, 130, 98), C(168, 110, 88), C(152, 98, 80), C(140, 90, 78)),
            LandStepped = true, Shade = 0.35f, ShadowTint = C(70, 60, 40), ShadowAmt = 0.35f,
            LandLine = C(130, 84, 40), LandMinor = 0.35f, LandMajor = 0.7f, LandIntervalScale = 0.6f,
            Coast = C(50, 100, 170), CoastAmt = 0.9f,
            SeaKeys = K(C(176, 214, 238), C(168, 208, 236), C(160, 204, 233), C(154, 200, 231), C(148, 196, 228), C(142, 192, 226),
                        C(136, 188, 224), C(130, 184, 222), C(124, 180, 220), C(118, 176, 218), C(114, 172, 216), C(110, 170, 216)),
            SeaStepped = true, SeaShade = 0f, SeaLine = C(80, 140, 200), SeaLineAmt = 0.55f,
        });

        Light.Add(new Theme
        {
            Id = "ecdis-day", Name = "ECDIS day", Note = "civil ship chart, IHO S-52",
            LandFlat = C(201, 185, 122), Shade = 0f,
            LandLine = C(139, 102, 31), LandMinor = 0.35f, LandMajor = 0.8f,
            Coast = C(82, 90, 92), CoastAmt = 1f,
            Zones = K(C(115, 182, 239), C(152, 197, 242), C(186, 213, 225), C(212, 234, 238)),
            SeaShade = 0f, SeaLine = C(125, 137, 140), SeaLineAmt = 0.9f, SafetyLine = C(82, 90, 92),
        });

        Theme glacier = new Theme
        {
            Id = "glacier", Name = "Glacier", Note = "snow over turquoise water",
            LandKeys = K(C(240, 246, 252), C(236, 244, 251), C(232, 242, 250), C(228, 240, 250), C(224, 238, 249), C(220, 236, 248),
                         C(216, 232, 246), C(212, 230, 246), C(210, 228, 246), C(208, 226, 246), C(206, 224, 246), C(206, 224, 248), C(252, 254, 255)),
            Shade = 1.0f, ShadowTint = C(80, 118, 176), ShadowAmt = 0.75f, HighlightTint = C(255, 255, 255), HighlightAmt = 0.6f,
            LandLine = C(120, 156, 200), LandMinor = 0.3f, LandMajor = 0.55f,
            Coast = C(50, 100, 150), CoastAmt = 0.9f,
            SeaKeys = K(C(178, 236, 232), C(150, 224, 226), C(120, 208, 222), C(92, 190, 216), C(70, 168, 208), C(52, 146, 200),
                        C(38, 122, 186), C(28, 100, 170), C(20, 80, 150), C(14, 62, 128), C(10, 46, 104), C(6, 34, 82)),
            SeaShade = 0.6f, SeaLine = C(255, 255, 255), SeaLineAmt = 0.5f, ShelfBoost = 1.3f,
        };
        glacier.SeaKeys = Grade(glacier.SeaKeys, 0.97f, 0.72f, null, 0f);   // calmer water: the snow is the subject
        Light.Add(glacier);

        // -------------------------------------------------------------------------------------------------- dark
        Dark.Add(new Theme
        {
            Id = "midnight", Name = "Midnight", Note = "cool, rich colours, glowing lines", Dark = true, Opacity = 0.86f,
            LandKeys = K(C(28, 96, 92), C(36, 112, 96), C(60, 132, 100), C(104, 150, 104), C(150, 160, 104), C(190, 150, 90),
                         C(206, 122, 76), C(200, 94, 78), C(170, 76, 92), C(138, 74, 120), C(120, 100, 164), C(170, 178, 214), C(240, 246, 255)),
            LandBright = 0.62f, Shade = 0.95f, ShadowTint = C(8, 16, 44), ShadowAmt = 0.55f,
            LandLine = C(150, 220, 238), LandMinor = 0.26f, LandMajor = 0.6f, LandHalo = 0.10f,
            SeaKeys = K(C(56, 128, 150), C(48, 114, 140), C(40, 100, 128), C(34, 92, 124), C(27, 75, 111), C(20, 58, 98),
                        C(16, 45, 82), C(11, 32, 66), C(9, 26, 56), C(7, 20, 46), C(5, 14, 36), C(4, 10, 28)),
            SeaShade = 0.85f, SeaLine = C(90, 215, 238), SeaLineAmt = 0.5f, SeaLineHalo = 0.12f, ShelfLine = C(150, 245, 255), ShelfBoost = 1.7f,
        });

        Dark.Add(new Theme
        {
            Id = "standard-night", Name = "Standard", Note = "height and depth colours, dimmed", Dark = true, Legacy = true, Opacity = 0.7f,
            LandKeys = standardLand, LandStepped = true, LandBright = 0.6f,
            SeaKeys = K(C(86, 104, 122), C(58, 72, 88), C(36, 45, 57), C(18, 25, 35), C(4, 7, 13)), SeaStepped = true,
        });

        // Black: dark-grey relief on black with neutral grey contour lines, a light coastline, a sea in colour between
        // Midnight's blue and a teal, fading to black in the deep.
        Dark.Add(new Theme
        {
            Id = "black", Name = "Black", Note = "black, grey relief and lines, a coloured sea", Dark = true,
            LandKeys = K(C(17, 18, 19), C(19, 20, 21), C(22, 22, 23), C(25, 25, 26), C(29, 29, 30), C(34, 34, 35),
                         C(41, 41, 42), C(50, 50, 51), C(61, 61, 62), C(74, 74, 75), C(90, 90, 91), C(108, 108, 110), C(130, 130, 132)),
            Shade = 0.9f, ShadowTint = C(0, 0, 0), ShadowAmt = 0.8f, HighlightTint = C(200, 200, 210), HighlightAmt = 0.25f,
            LandLine = C(125, 125, 132), LandMinor = 0.45f, LandMajor = 0.95f, LandHalo = 0.08f,
            Coast = C(235, 235, 240), CoastAmt = 0.9f,
            SeaKeys = Mean(Grade(Dark.Find(t => t.Id == "midnight").SeaKeys, 0.8f, 1f, null, 0f),
                           K(C(30, 96, 104), C(26, 86, 96), C(22, 76, 88), C(19, 68, 82), C(16, 58, 74), C(13, 48, 66),
                             C(10, 38, 56), C(8, 30, 46), C(6, 22, 36), C(4, 16, 28), C(3, 11, 22), C(2, 8, 16))),
            SeaShade = 0.5f, SeaLine = C(85, 210, 225), SeaLineAmt = 0.5f, SeaLineHalo = 0.14f,
            ShelfLine = C(165, 250, 248), ShelfBoost = 1.6f,
        });

        Dark.Add(new Theme
        {
            Id = "ecdis-dusk", Name = "ECDIS dusk", Note = "civil ship chart, IHO S-52 dusk table", Dark = true,
            LandFlat = C(44, 41, 27), Shade = 0f,
            LandLine = C(76, 56, 17), LandMinor = 0.5f, LandMajor = 0.9f,
            Coast = C(54, 60, 61), CoastAmt = 1f,
            Zones = K(C(22, 35, 47), C(21, 27, 33), C(12, 14, 15), C(7, 7, 7)),
            SeaShade = 0f, SeaLine = C(41, 46, 46), SeaLineAmt = 0.9f, SafetyLine = C(54, 60, 61),
        });

        Dark.Add(new Theme
        {
            Id = "combat-display", Name = "Combat display", Note = "navy console, bright coastline", Dark = true,
            LandKeys = K(C(34, 46, 64), C(38, 52, 72), C(42, 58, 80), C(48, 66, 90), C(56, 76, 102), C(66, 88, 116),
                         C(78, 102, 132), C(92, 116, 148), C(108, 132, 164), C(126, 150, 180), C(146, 168, 196), C(170, 188, 212), C(210, 222, 238)),
            Shade = 0.8f, ShadowTint = C(6, 10, 24), ShadowAmt = 0.6f, HighlightTint = C(150, 190, 230), HighlightAmt = 0.2f,
            LandLine = C(72, 112, 156), LandMinor = 0.22f, LandMajor = 0.5f,
            Coast = C(90, 215, 240), CoastAmt = 1f,
            SeaKeys = K(C(14, 36, 74), C(12, 30, 64), C(10, 26, 56), C(8, 22, 48), C(7, 18, 42), C(6, 15, 36),
                        C(5, 12, 31), C(4, 10, 26), C(4, 8, 22), C(3, 7, 19), C(3, 6, 16), C(2, 5, 13)),
            SeaShade = 0.5f, SeaLine = C(40, 90, 160), SeaLineAmt = 0.5f, SeaLineHalo = 0.1f, ShelfLine = C(90, 200, 235), ShelfBoost = 1.6f,
        });

        Dark.Add(new Theme
        {
            Id = "phosphor", Name = "Phosphor green", Note = "radar scope, night vision", Dark = true,
            LandKeys = K(C(4, 22, 8), C(6, 30, 12), C(8, 38, 14), C(10, 46, 18), C(12, 56, 22), C(16, 68, 26),
                         C(20, 82, 32), C(26, 98, 38), C(34, 118, 46), C(46, 144, 58), C(64, 176, 74), C(96, 214, 100), C(170, 255, 170)),
            Shade = 0.8f, ShadowTint = C(0, 6, 0), ShadowAmt = 0.6f, HighlightTint = C(120, 255, 140), HighlightAmt = 0.25f,
            LandLine = C(40, 170, 70), LandMinor = 0.22f, LandMajor = 0.6f, LandHalo = 0.1f,
            Coast = C(150, 255, 160), CoastAmt = 1f,
            SeaKeys = K(C(2, 12, 6), C(2, 10, 5), C(2, 9, 4), C(1, 8, 4), C(1, 7, 3), C(1, 6, 3),
                        C(1, 5, 2), C(1, 5, 2), C(0, 4, 2), C(0, 3, 1), C(0, 3, 1), C(0, 2, 1)),
            SeaShade = 0.3f, SeaLine = C(30, 190, 70), SeaLineAmt = 0.5f, SeaLineHalo = 0.15f, ShelfBoost = 1.6f,
        });

        Dark.Add(new Theme
        {
            Id = "red-light", Name = "Red light", Note = "command centre or bridge at night", Dark = true,
            LandKeys = K(C(26, 2, 2), C(34, 4, 3), C(44, 6, 4), C(54, 8, 6), C(66, 10, 8), C(80, 12, 10),
                         C(96, 16, 12), C(116, 20, 14), C(140, 26, 18), C(170, 36, 24), C(206, 52, 32), C(240, 90, 56), C(255, 170, 130)),
            Shade = 0.8f, ShadowTint = C(8, 0, 0), ShadowAmt = 0.6f, HighlightTint = C(255, 110, 80), HighlightAmt = 0.25f,
            LandLine = C(180, 30, 22), LandMinor = 0.35f, LandMajor = 0.7f, LandHalo = 0.1f, LandIntervalScale = 0.8f,
            Coast = C(255, 110, 84), CoastAmt = 1f,
            SeaKeys = K(C(10, 1, 1), C(9, 1, 1), C(8, 1, 1), C(7, 0, 0), C(6, 0, 0), C(6, 0, 0),
                        C(5, 0, 0), C(5, 0, 0), C(4, 0, 0), C(3, 0, 0), C(3, 0, 0), C(2, 0, 0)),
            SeaShade = 0.3f, SeaLine = C(170, 26, 20), SeaLineAmt = 0.5f, SeaLineHalo = 0.15f, ShelfBoost = 1.6f,
        });
    }
}
