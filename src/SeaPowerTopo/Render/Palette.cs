using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeaPowerTopo.Render;

/// <summary>
/// A tint: height bands (land) or depth bands (sea) and their colours, from a theme's key colours spread evenly over
/// the bands – in steps (every band one colour) or blended (the colour runs on through each band) – so any number of
/// bands stays distinct. Plain C# (no Unity types) so the offline preview renders with the very same code.
/// </summary>
internal sealed class Palette
{
    private const int LutMax = 9000;

    public static readonly float[] DefaultLimits = { 0, 50, 100, 200, 300, 500, 750, 1000, 1500, 2000, 2500, 3000, 4000 };
    public static readonly float[] DefaultDepthLimits = { 0, 50, 100, 200, 300, 500, 1000, 1500, 2000, 3000, 4000, 6000 };

    /// <summary>Lower edge of every band in metres (height or depth), ascending; the first is 0.</summary>
    public readonly float[] Limits;
    /// <summary>r, g, b per band: the colour at the band's lower edge (what a legend shows).</summary>
    public readonly byte[] Rgb;
    public readonly bool Dark;
    /// <summary>Depth bands of the sea rather than height bands of the land.</summary>
    public readonly bool Sea;
    public readonly Theme Theme;

    private readonly byte[] lut = new byte[LutMax + 1];
    private readonly byte[] colours = new byte[(LutMax + 1) * 3];

    public Palette(float[] limits, Theme theme, bool sea = false)
    {
        Limits = limits;
        Theme = theme;
        Dark = theme.Dark;
        Sea = sea;

        // Themes without a tint of their own (a chart's flat land and depth shades) still need the bands for the
        // legend and the depth lines: the Standard colours of their map theme stand in.
        int[][] keys = sea ? theme.SeaKeys : theme.LandKeys;
        float bright = sea ? 1f : theme.LandBright;
        bool stepped = sea ? theme.SeaStepped : theme.LandStepped;
        if (keys == null)
        {
            Theme standard = Themes.Of(theme.Dark).Find(t => t.Legacy);
            keys = sea ? standard.SeaKeys : standard.LandKeys;
            bright = sea ? 1f : standard.LandBright;
            stepped = true;
        }

        float[,] ramp = Ramp(keys);
        int n = limits.Length;
        Rgb = new byte[n * 3];
        for (int i = 0; i < n; i++)
        {
            RampColour(ramp, n == 1 ? 0f : i / (float)(n - 1), out float r, out float g, out float b);
            Rgb[i * 3] = (byte)(r * bright);
            Rgb[i * 3 + 1] = (byte)(g * bright);
            Rgb[i * 3 + 2] = (byte)(b * bright);
        }

        int band = 0;
        for (int m = 0; m <= LutMax; m++)
        {
            while (band + 1 < n && m >= limits[band + 1])
                band++;
            lut[m] = (byte)band;
            if (stepped || n == 1)
            {
                colours[m * 3] = Rgb[band * 3];
                colours[m * 3 + 1] = Rgb[band * 3 + 1];
                colours[m * 3 + 2] = Rgb[band * 3 + 2];
            }
            else
            {
                // Blended: the position among the bands runs on through each band.
                float pos = band + (band + 1 < n ? (m - limits[band]) / (limits[band + 1] - limits[band]) : 0f);
                RampColour(ramp, pos / (n - 1), out float r, out float g, out float b);
                colours[m * 3] = (byte)(r * bright);
                colours[m * 3 + 1] = (byte)(g * bright);
                colours[m * 3 + 2] = (byte)(b * bright);
            }
        }
    }

    public int Count => Limits.Length;

    /// <summary>Band of a height (or depth) in metres; negative counts as the first band.</summary>
    public int BandOf(float metres)
    {
        if (metres <= 0f)
            return 0;
        if (metres < LutMax)
            return lut[(int)metres];
        int band = Limits.Length - 1;
        while (band > 0 && metres < Limits[band])
            band--;
        return band;
    }

    /// <summary>The colour of a height (or depth) in metres.</summary>
    public void Colour(float metres, out float r, out float g, out float b)
    {
        if (metres >= LutMax)
        {
            int band = BandOf(metres) * 3;
            r = Rgb[band];
            g = Rgb[band + 1];
            b = Rgb[band + 2];
            return;
        }
        int i = (metres <= 0f ? 0 : (int)metres) * 3;
        r = colours[i];
        g = colours[i + 1];
        b = colours[i + 2];
    }

    /// <summary>Band limits from the config string; null if it isn't a usable ascending list starting at 0.</summary>
    public static float[] ParseLimits(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return null;
        var limits = new List<float>();
        foreach (string part in spec.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return null;
            if (limits.Count > 0 && v <= limits[limits.Count - 1])
                return null;
            limits.Add(v);
        }
        if (limits.Count < 2 || limits.Count > 64 || limits[0] != 0f)
            return null;
        return limits.ToArray();
    }

    // Ramp stops (position 0..1, r, g, b): the theme's key colours, evenly spaced.
    private static float[,] Ramp(int[][] keys)
    {
        int n = keys.Length;
        var ramp = new float[n, 4];
        for (int i = 0; i < n; i++)
        {
            ramp[i, 0] = n == 1 ? 0f : i / (float)(n - 1);
            ramp[i, 1] = keys[i][0];
            ramp[i, 2] = keys[i][1];
            ramp[i, 3] = keys[i][2];
        }
        return ramp;
    }

    private static void RampColour(float[,] ramp, float t, out float r, out float g, out float b)
    {
        int last = ramp.GetLength(0) - 1;
        if (last == 0)
        {
            r = ramp[0, 1];
            g = ramp[0, 2];
            b = ramp[0, 3];
            return;
        }
        for (int i = 0; i < last; i++)
        {
            float t0 = ramp[i, 0], t1 = ramp[i + 1, 0];
            if (t <= t1 || i == last - 1)
            {
                float f = t1 > t0 ? Math.Max(0f, Math.Min(1f, (t - t0) / (t1 - t0))) : 0f;
                r = ramp[i, 1] + (ramp[i + 1, 1] - ramp[i, 1]) * f;
                g = ramp[i, 2] + (ramp[i + 1, 2] - ramp[i, 2]) * f;
                b = ramp[i, 3] + (ramp[i + 1, 3] - ramp[i, 3]) * f;
                return;
            }
        }
        r = ramp[last, 1];
        g = ramp[last, 2];
        b = ramp[last, 3];
    }
}
