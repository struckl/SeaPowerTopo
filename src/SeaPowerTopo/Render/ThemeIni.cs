using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SeaPowerTopo.Render;

/// <summary>A theme file read but not yet built: its values by key, and the theme it starts from.</summary>
internal sealed class ThemeFile
{
    /// <summary>The file's name without extension, the id if the file has none.</summary>
    public string FileName;
    public string Id, BaseId;
    public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The theme file format: plain text in the style of the game's own ini files, one <c>Key=Value</c> per line, comment
/// lines starting with <c>;</c> or <c>#</c>. Colours are <c>r,g,b</c> or <c>#rrggbb</c>, lists of colours are separated by
/// <c>;</c>. <c>Base=</c> starts from another theme (built in or from another file) and the file only lists what it
/// changes; <c>none</c> removes a colour the base has. Plain C#, shared with the offline preview, which tests it by
/// writing every built-in theme out and reading it back.
/// </summary>
internal static class ThemeIni
{
    // A key of the format: how a file's value is read into a theme, and how a theme writes it (null = the line is left
    // out). Every key is listed once, here, in the order a saved file has them.
    private sealed class Key
    {
        public readonly string Name;
        public readonly Func<string, Theme, bool> Read;
        public readonly Func<Theme, string> Write;

        public Key(string name, Func<string, Theme, bool> read, Func<Theme, string> write)
        {
            Name = name;
            Read = read;
            Write = write;
        }
    }

    private static readonly Key[] Head =
    {
        new Key("Dark", (v, t) => Bool(v, ref t.Dark), t => B(t.Dark)),
        new Key("Opacity", (v, t) => Num(v, 0f, 1f, ref t.Opacity), t => F(t.Opacity)),
    };

    private static readonly Key[] Land =
    {
        new Key("LandColours", (v, t) => Colours(v, 2, MaxKeys, ref t.LandKeys), t => CL(t.LandKeys)),
        new Key("LandStepped", (v, t) => Bool(v, ref t.LandStepped), t => B(t.LandStepped)),
        new Key("LandBrightness", (v, t) => Num(v, 0.05f, 3f, ref t.LandBright), t => F(t.LandBright)),
        new Key("LandFlat", (v, t) => Colour(v, ref t.LandFlat), t => t.LandFlat != null ? C(t.LandFlat) : null),
        new Key("Relief", (v, t) => Num(v, 0f, 1.5f, ref t.Shade), t => F(t.Shade)),
        new Key("ShadowColour", (v, t) => Colour(v, ref t.ShadowTint), t => C(t.ShadowTint)),
        new Key("ShadowAmount", (v, t) => Num(v, 0f, 1f, ref t.ShadowAmt), t => F(t.ShadowAmt)),
        new Key("HighlightColour", (v, t) => Colour(v, ref t.HighlightTint), t => C(t.HighlightTint)),
        new Key("HighlightAmount", (v, t) => Num(v, 0f, 1f, ref t.HighlightAmt), t => F(t.HighlightAmt)),
        new Key("LandLines", (v, t) => Colour(v, ref t.LandLine), t => C(t.LandLine)),
        new Key("LandLinesMinor", (v, t) => Num(v, 0f, 1f, ref t.LandMinor), t => F(t.LandMinor)),
        new Key("LandLinesMajor", (v, t) => Num(v, 0f, 1f, ref t.LandMajor), t => F(t.LandMajor)),
        new Key("LandLinesGlow", (v, t) => Num(v, 0f, 1f, ref t.LandHalo), t => F(t.LandHalo)),
        new Key("LandLinesSpacing", (v, t) => Num(v, 0.1f, 5f, ref t.LandIntervalScale), t => F(t.LandIntervalScale)),
        new Key("LandLinesHighColour", (v, t) => Colour(v, ref t.LandLineHigh), t => C(t.LandLineHigh)),
        new Key("LandLinesHighAt", (v, t) => Num(v, 10f, 9000f, ref t.LandLineSpan), t => F(t.LandLineSpan)),
        new Key("LandLinesLowStrength", (v, t) => Num(v, 0f, 1f, ref t.LandLineLowAmt), t => F(t.LandLineLowAmt)),
        new Key("Coast", (v, t) => Colour(v, ref t.Coast), t => C(t.Coast)),
        new Key("CoastStrength", (v, t) => Num(v, 0f, 1f, ref t.CoastAmt), t => F(t.CoastAmt)),
    };

    private static readonly Key[] Sea =
    {
        new Key("SeaColours", (v, t) => Colours(v, 2, MaxKeys, ref t.SeaKeys), t => CL(t.SeaKeys)),
        new Key("SeaStepped", (v, t) => Bool(v, ref t.SeaStepped), t => B(t.SeaStepped)),
        new Key("SeaZones", (v, t) => Colours(v, 4, 4, ref t.Zones), t => t.Zones != null ? CL(t.Zones) : null),
        new Key("SeaRelief", (v, t) => Num(v, 0f, 1.5f, ref t.SeaShade), t => F(t.Legacy ? Look.LegacySeaRelief : t.SeaShade)),
        new Key("SeaLines", (v, t) => Colour(v, ref t.SeaLine), t => C(t.SeaLine)),
        new Key("SeaLinesStrength", (v, t) => Num(v, 0f, 1f, ref t.SeaLineAmt), t => F(t.SeaLineAmt)),
        new Key("SeaLinesGlow", (v, t) => Num(v, 0f, 1f, ref t.SeaLineHalo), t => F(t.SeaLineHalo)),
        new Key("ShelfLines", (v, t) => Colour(v, ref t.ShelfLine), t => C(t.ShelfLine)),
        new Key("ShelfBoost", (v, t) => Num(v, 0.5f, 3f, ref t.ShelfBoost), t => F(t.ShelfBoost)),
        new Key("SafetyLine", (v, t) => Colour(v, ref t.SafetyLine), t => C(t.SafetyLine)),
    };

    // The keys that name the theme and its base, read by TryRead and Start.
    private static readonly string[] NameKeys = { "Id", "Name", "Note", "Base" };

    private static readonly Dictionary<string, Key> Keys = ByName();

    private static Dictionary<string, Key> ByName()
    {
        var keys = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);
        foreach (Key[] group in new[] { Head, Land, Sea })
        {
            foreach (Key key in group)
                keys[key.Name] = key;
        }
        return keys;
    }

    private static bool Known(string key) =>
        Keys.ContainsKey(key) || Array.Exists(NameKeys, k => k.Equals(key, StringComparison.OrdinalIgnoreCase));

    private const int MaxKeys = 64;

    /// <summary>Reads the text of a theme file. False (with a message) if it cannot be a theme at all.</summary>
    public static bool TryRead(string text, string fileName, out ThemeFile file, List<string> messages)
    {
        file = new ThemeFile { FileName = fileName };
        int lineNo = 0;
        foreach (string raw in text.TrimStart('﻿').Split('\n'))
        {
            lineNo++;
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[')
                continue;
            int eq = line.IndexOf('=');
            if (eq < 1)
            {
                messages.Add($"line {lineNo} is not Key=Value, ignored");
                continue;
            }
            string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
            if (!Known(key))
            {
                messages.Add($"line {lineNo}: unknown key '{key}', ignored");
                continue;
            }
            file.Values[key] = value;
        }

        string id = file.Values.TryGetValue("Id", out string v) && v.Length > 0 ? v : fileName;
        id = id.Trim().ToLowerInvariant().Replace(' ', '-');
        if (!ValidId(id))
        {
            messages.Add($"the id '{id}' is not usable (letters, digits, - and _, at most 40 characters)");
            return false;
        }
        file.Id = id;
        file.BaseId = file.Values.TryGetValue("Base", out v) && v.Length > 0 ? v.Trim().ToLowerInvariant() : null;
        return true;
    }

    private static bool ValidId(string id)
    {
        if (id.Length == 0 || id.Length > 40)
            return false;
        foreach (char c in id)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                return false;
        }
        return true;
    }

    /// <summary>Builds the theme of a file on top of its base theme (null = from scratch). False if the result is unusable.</summary>
    public static bool TryBuild(ThemeFile file, Theme baseTheme, out Theme theme, List<string> messages)
    {
        Theme t = Start(file, baseTheme);
        theme = null;
        foreach (KeyValuePair<string, string> kv in file.Values)
        {
            if (Keys.TryGetValue(kv.Key, out Key key) && !key.Read(kv.Value, t))
                messages.Add($"'{kv.Key}={kv.Value}' is not usable here, ignored");
        }
        if (t.LandFlat == null && (t.LandKeys == null || t.LandKeys.Length < 2))
        {
            messages.Add("the theme needs LandColours (at least two) or LandFlat, or a Base that has them");
            return false;
        }
        if (t.Zones == null && (t.SeaKeys == null || t.SeaKeys.Length < 2))
        {
            messages.Add("the theme needs SeaColours (at least two) or SeaZones, or a Base that has them");
            return false;
        }
        theme = t;
        return true;
    }

    // The base theme's copy (or a new theme) with the file's id, name and note, before its values are read.
    private static Theme Start(ThemeFile file, Theme baseTheme)
    {
        // From scratch, lines and glow have sensible strengths in case the file gives only a colour.
        Theme t = baseTheme != null
            ? baseTheme.Clone()
            : new Theme { Opacity = 1f, LandMinor = 0.3f, LandMajor = 0.7f, SeaLineAmt = 0.5f };
        t.Custom = true;
        // The Standard looks have no SeaRelief of their own: they shade the seabed by their own rule.
        if (baseTheme != null && baseTheme.Legacy)
            t.SeaShade = Look.LegacySeaRelief;
        t.Legacy = false;
        t.Id = file.Id;
        t.Name = file.Values.TryGetValue("Name", out string name) && name.Length > 0 ? name : file.Id;
        t.Note = file.Values.TryGetValue("Note", out string note) ? note : (baseTheme?.Note ?? "");
        return t;
    }

    private static bool Bool(string v, ref bool target)
    {
        switch (v.Trim().ToLowerInvariant())
        {
            case "true": case "yes": case "on": case "1": target = true; return true;
            case "false": case "no": case "off": case "0": target = false; return true;
            default: return false;
        }
    }

    private static bool Num(string v, float min, float max, ref float target)
    {
        if (!float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) || float.IsNaN(f) || float.IsInfinity(f))
            return false;
        target = Math.Max(min, Math.Min(max, f));
        return true;
    }

    private static bool Colour(string v, ref int[] target)
    {
        if (IsNone(v))
        {
            target = null;
            return true;
        }
        if (!TryColour(v, out int[] c))
            return false;
        target = c;
        return true;
    }

    private static bool Colours(string v, int min, int max, ref int[][] target)
    {
        if (IsNone(v))
        {
            target = null;
            return true;
        }
        string[] parts = v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < min || parts.Length > max)
            return false;
        var list = new int[parts.Length][];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!TryColour(parts[i], out list[i]))
                return false;
        }
        target = list;
        return true;
    }

    private static bool IsNone(string v) => v.Trim().Length == 0 || v.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

    private static bool TryColour(string s, out int[] c)
    {
        c = null;
        s = s.Trim();
        if (s.Length == 7 && s[0] == '#')
        {
            if (!int.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                return false;
            c = new[] { (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255 };
            return true;
        }
        string[] p = s.Split(',');
        if (p.Length != 3)
            return false;
        var o = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(p[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return false;
            o[i] = Math.Max(0, Math.Min(255, n));
        }
        c = o;
        return true;
    }

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static string C(int[] c) => c == null ? "none" : $"{c[0]},{c[1]},{c[2]}";

    private static string CL(int[][] list)
    {
        if (list == null)
            return "none";
        var sb = new StringBuilder();
        for (int i = 0; i < list.Length; i++)
        {
            if (i > 0)
                sb.Append("; ");
            sb.Append(C(list[i]));
        }
        return sb.ToString();
    }

    private static string B(bool v) => v ? "true" : "false";

    // The upper band edge of the default bands, as the file's comments name it.
    private static string Top(float[] limits) => limits[limits.Length - 1].ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The whole definition of a theme as a theme file (no Base), ready to edit; <paramref name="savedFrom"/> names the look it was made from.</summary>
    public static string Write(Theme t, string savedFrom)
    {
        var sb = new StringBuilder();
        void Line(string key, string value) => sb.Append(key).Append('=').Append(value).Append('\n');
        void Comment(string text) => sb.Append("; ").Append(text).Append('\n');
        void Lines(Key[] keys)
        {
            foreach (Key key in keys)
            {
                string value = key.Write(t);
                if (value != null)
                    Line(key.Name, value);
            }
        }

        Comment($"Sea Power Topographic Map theme, saved from \"{savedFrom}\". Edit and save: the map reloads it by itself.");
        Comment("Colours are r,g,b or #rrggbb; lists are colour; colour; ... Lines starting with ; or # are comments.");
        Comment("See docs/themes.md in the mod's repository (github.com/struckl/SeaPowerTopo) for what every line does.");
        if (t.Legacy)
            Comment("The Standard looks are drawn by the mod's original rules; this copy follows them closely, not to the pixel.");
        sb.Append('\n');
        Line("Id", t.Id);
        Line("Name", t.Name);
        Line("Note", t.Note ?? "");
        Lines(Head);
        sb.Append('\n');
        Comment($"Land: key colours spread over the height bands (0 to {Top(Palette.DefaultLimits)} m), or one flat colour");
        Lines(Land);
        sb.Append('\n');
        Comment($"Sea: key colours spread over the depth bands (0 to {Top(Palette.DefaultDepthLimits)} m), or the four shades of a chart");
        Lines(Sea);
        return sb.ToString();
    }

    /// <summary>The documented example file the mod puts into the themes folder (ignored: its name starts with an underscore).</summary>
    public const string Example = @"; Sea Power Topographic Map - theme file example
;
; Copy this file to e.g. mytheme.ini (files whose names start with an underscore are ignored), change it and save:
; the map reloads it by itself and your theme shows up in the look list, marked ""file"".
; Lines starting with ; or # are comments. Colours are r,g,b (0-255) or #rrggbb, lists are colour; colour; colour ...
; Leave a line out to take the value from Base (or the default). Write none to remove a colour that Base has.
; Looks you made with the look button's ""Save this look as a theme file"" are complete, without Base.

Id=my-black
Name=My Black
Note=Black with warm contour lines
; Base starts from an existing theme (its id: black, genshtab, midnight, ecdis-day, ... or another file's Id).
Base=black
; Dark=true is for the map's night theme, false for its day theme. Taken from Base if left out.
Dark=true
LandLines=170,150,120

; ---------------------------------------------------------------------------------------------------------
; Everything a theme can say (these lines are comments; copy one up and change it):
;
; Opacity=1                 how much of the stock map the theme covers at the default opacity setting (0-1)
; LandColours=...           key colours from low land to high mountains, spread evenly over the height bands
; LandStepped=false         true: every band one flat colour, false: the colour runs on through each band
; LandBrightness=1          multiplier for the land colours
; LandFlat=250,232,176      one flat colour for all land (instead of LandColours)
; Relief=0.9                strength of the hill shading (0-1.5; the Hill shading setting scales it)
; ShadowColour=0,0,0        what shadowed slopes fall towards; ShadowAmount=0.8 how much (0-1)
; HighlightColour=200,200,210   what lit slopes go towards; HighlightAmount=0.25 how much (0-1)
; LandLines=125,125,132     colour of the contour lines on land (none = the default lines)
; LandLinesMinor=0.45       strength of the thin contour lines (0-1); LandLinesMajor=0.95 of every fifth
; LandLinesGlow=0.08        a soft glow around the lines (0-1)
; LandLinesSpacing=1        1 = the usual spacing, 0.5 = twice as many lines (0.1-5)
; LandLinesHighColour=255,244,222   lines change towards this colour with height ...
; LandLinesHighAt=1400      ... reaching it at this height in metres
; LandLinesLowStrength=0.45 ... and grow from this share of their strength at sea level to the full strength
; Coast=235,235,240         a coastline drawn on the land side of the shore; CoastStrength=0.9 (0-1)
; SeaColours=...            key colours from the coast to the deepest water, spread over the depth bands
; SeaStepped=false          as LandStepped, for the sea
; SeaZones=a; b; c; d       a chart: four colours for very shallow, shallow, medium and deep water (instead of SeaColours)
; SeaRelief=0.5             strength of the shading of the seabed (0-1.5)
; SeaLines=85,210,225       colour of the depth lines; SeaLinesStrength=0.5 (0-1); SeaLinesGlow=0.14 (0-1)
; ShelfLines=165,250,248    colour of the line at 200 m; ShelfBoost=1.6 makes it stronger (0.5-3)
; SafetyLine=30,80,170      for charts: colour of the bold safety contour
";
}
