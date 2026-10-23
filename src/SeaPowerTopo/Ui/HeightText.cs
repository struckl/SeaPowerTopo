using System;
using System.Globalization;

namespace SeaPowerTopo.Ui;

/// <summary>Heights as text in the configured units.</summary>
internal static class HeightText
{
    public const float FeetPerMetre = 3.28084f;

    /// <summary>"1,240 m · 4,068 ft", "1,240 m" or "4,068 ft"; <paramref name="feetStep"/> rounds the feet.</summary>
    public static string Format(float metres, HeightUnits units, int feetStep = 1)
    {
        string m = Number(metres, 1) + " m";
        string ft = Number(metres * FeetPerMetre, feetStep) + " ft";
        switch (units)
        {
            case HeightUnits.Metres: return m;
            case HeightUnits.Feet: return ft;
            default: return m + " · " + ft;
        }
    }

    private static string Number(float value, int step)
    {
        double rounded = Math.Round(value / step) * step;
        return rounded.ToString("#,0", CultureInfo.InvariantCulture);
    }
}
