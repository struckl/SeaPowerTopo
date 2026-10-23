using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeaPowerTopo.Render;

/// <summary>
/// The hypsometric tint: height bands and their colours. Band colours are spread evenly over a fixed ramp from
/// lowland green over yellow and browns to rock grey and snow white, so any number of bands stays distinct.
/// Plain C# (no Unity types) so the offline preview renders with the very same code.
/// </summary>
internal sealed class Palette
{
    // Ramp stops: position 0..1, r, g, b (day theme). Thirteen stops, so the thirteen default bands each get one
    // exactly; neighbours differ in hue as well as lightness, because the tint sits on the tan stock map.
    private static readonly float[,] Ramp =
    {
        { 0f / 12,  78, 146,  76 },
        { 1f / 12, 118, 170,  84 },
        { 2f / 12, 162, 194, 100 },
        { 3f / 12, 204, 214, 118 },
        { 4f / 12, 240, 226, 138 },
        { 5f / 12, 244, 196, 112 },
        { 6f / 12, 230, 156,  84 },
        { 7f / 12, 206, 120,  64 },
        { 8f / 12, 174,  92,  54 },
        { 9f / 12, 140,  72,  58 },
        { 10f / 12, 120,  84, 104 },
        { 11f / 12, 168, 160, 174 },
        { 12f / 12, 250, 250, 250 },
    };

    private const float NightDim = 0.6f;
    private const int LutMax = 9000;

    public static readonly float[] DefaultLimits = { 0, 50, 100, 200, 300, 500, 750, 1000, 1500, 2000, 2500, 3000, 4000 };

    /// <summary>Lower edge of every band in metres, ascending; the first is 0.</summary>
    public readonly float[] Limits;
    /// <summary>r, g, b per band.</summary>
    public readonly byte[] Rgb;
    public readonly bool Dark;

    private readonly byte[] lut = new byte[LutMax + 1];

    public Palette(float[] limits, bool dark)
    {
        Limits = limits;
        Dark = dark;
        int n = limits.Length;
        Rgb = new byte[n * 3];
        for (int i = 0; i < n; i++)
        {
            RampColour(n == 1 ? 0f : i / (float)(n - 1), out float r, out float g, out float b);
            float k = dark ? NightDim : 1f;
            Rgb[i * 3] = (byte)(r * k);
            Rgb[i * 3 + 1] = (byte)(g * k);
            Rgb[i * 3 + 2] = (byte)(b * k);
        }
        int band = 0;
        for (int m = 0; m <= LutMax; m++)
        {
            while (band + 1 < n && m >= limits[band + 1])
                band++;
            lut[m] = (byte)band;
        }
    }

    public int Count => Limits.Length;

    /// <summary>Band of a land height in metres (negative counts as the lowest band).</summary>
    public int BandOf(float metres)
    {
        if (metres <= 0f)
            return 0;
        if (metres >= LutMax)
            return lut[LutMax];
        return lut[(int)metres];
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

    private static void RampColour(float t, out float r, out float g, out float b)
    {
        int last = Ramp.GetLength(0) - 1;
        for (int i = 0; i < last; i++)
        {
            float t0 = Ramp[i, 0], t1 = Ramp[i + 1, 0];
            if (t <= t1 || i == last - 1)
            {
                float f = t1 > t0 ? Math.Max(0f, Math.Min(1f, (t - t0) / (t1 - t0))) : 0f;
                r = Ramp[i, 1] + (Ramp[i + 1, 1] - Ramp[i, 1]) * f;
                g = Ramp[i, 2] + (Ramp[i + 1, 2] - Ramp[i, 2]) * f;
                b = Ramp[i, 3] + (Ramp[i + 1, 3] - Ramp[i, 3]) * f;
                return;
            }
        }
        r = Ramp[last, 1];
        g = Ramp[last, 2];
        b = Ramp[last, 3];
    }
}
