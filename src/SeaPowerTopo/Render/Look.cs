using System;

namespace SeaPowerTopo.Render;

/// <summary>
/// The player's look settings where the drawing code needs them: their defaults (the config entries are made with
/// these) and how a setting scales a theme's own values. A theme's Opacity, Relief and SeaRelief are what it shows at
/// the default settings, so the defaults here are part of every theme's look. Plain C#, shared with the offline preview.
/// </summary>
internal static class Look
{
    public const float DefaultOpacity = 0.7f, DefaultHillshade = 0.6f;
    public const int DefaultChartSafety = 30;

    /// <summary>Lower edges of the colour bands in metres, as the config holds them.</summary>
    public const string DefaultHeightBands = "0,50,100,200,300,500,750,1000,1500,2000,2500,3000,4000";
    public const string DefaultDepthBands = "0,50,100,200,300,500,1000,1500,2000,3000,4000,6000";

    /// <summary>
    /// The Standard looks shade the seabed at half the user's hill shading; a theme file made from them says so with a
    /// SeaRelief of that at the default setting.
    /// </summary>
    public const float LegacySeabedShading = 0.5f, LegacySeaRelief = LegacySeabedShading * DefaultHillshade;

    /// <summary>How much of the stock map a theme covers at this opacity setting; the Standard looks take it as it is.</summary>
    public static float Opacity(Theme theme, float setting) =>
        theme.Legacy ? setting : Math.Min(1f, theme.Opacity * setting / DefaultOpacity);

    /// <summary>Hill shading strength on land at this setting (0 = off).</summary>
    public static float LandShading(Theme theme, float setting) =>
        theme.Legacy ? setting : setting <= 0f ? 0f : Math.Min(1.3f, theme.Shade * setting / DefaultHillshade);

    /// <summary>Shading strength of the seabed at this setting (0 = off).</summary>
    public static float SeaShading(Theme theme, float setting) =>
        theme.Legacy ? setting * LegacySeabedShading : setting <= 0f ? 0f : Math.Min(1.2f, theme.SeaShade * setting / DefaultHillshade);

    /// <summary>
    /// A chart's water shades from the safety contour setting: very shallow up to a sixth of it, shallow up to it,
    /// medium up to three and a third times it, deep beyond.
    /// </summary>
    public static void ChartDepths(float setting, out float shallow, out float safety, out float deep)
    {
        safety = Math.Max(2f, setting);
        shallow = Math.Max(2f, safety / 6f);
        deep = safety * 100f / 30f;
    }
}
