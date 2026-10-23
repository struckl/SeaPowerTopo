using System;

namespace SeaPowerTopo.Render;

/// <summary>Terrain heights on a regular lon/lat grid, copied out of the game's DEM on the main thread.</summary>
internal sealed class SourceGrid
{
    /// <summary>Centre of cell (0, 0) and the cell size in degrees; rows run south, columns east.</summary>
    public double Lon0, Lat0, Cell;
    public int Nx, Ny;
    /// <summary>Heights in metres as the simulation uses them; negative = water.</summary>
    public float[] V;

    public void Allocate(int nx, int ny)
    {
        Nx = nx;
        Ny = ny;
        if (V == null || V.Length < nx * ny)
            V = new float[nx * ny];
    }
}

/// <summary>One overlay image: where it lies on the map, how it is coloured, and its buffers.</summary>
internal sealed class RasterJob
{
    public int Width, Height;
    /// <summary>North-west corner of the image and degrees per pixel (square in lon/lat, like the map).</summary>
    public double West, North, Dpp;

    public Palette Palette;
    public float Opacity, Hillshade;
    /// <summary>Metres between contour lines; 0 = none.</summary>
    public float Interval;
    /// <summary>Terrain at or above this height (metres) is hatched red; NaN = off.</summary>
    public float Danger = float.NaN;

    /// <summary>(Width + 2) × (Height + 2) land heights in metres with a one-pixel border; -1 = water.</summary>
    public float[] Heights;
    /// <summary>Width × Height RGBA, straight alpha, row 0 = north.</summary>
    public byte[] Rgba;

    public double South => North - Height * Dpp;
    public double East => West + Width * Dpp;
}

/// <summary>
/// Turns DEM heights into the overlay image: hypsometric tint, hill shading, contour lines and the red hatching
/// for terrain above the selected aircraft. Runs on a worker thread; plain C# so the offline preview shares it.
/// </summary>
internal static class Raster
{
    public const double MetresPerDegree = 111320.0;

    private static readonly float[] NiceIntervals = { 20, 50, 100, 200, 500, 1000 };

    /// <summary>Contour step that keeps lines a few pixels apart on moderate slopes at this map scale.</summary>
    public static float AutoInterval(double metresPerPixel)
    {
        double target = metresPerPixel * 0.5;
        foreach (float step in NiceIntervals)
        {
            if (step >= target)
                return step;
        }
        return NiceIntervals[NiceIntervals.Length - 1];
    }

    /// <summary>Vertical exaggeration for the hill shading: relief flattens out as the map zooms out.</summary>
    public static float Exaggeration(double metresPerPixel) =>
        (float)Math.Max(1.5, Math.Min(5.0, 1.5 * Math.Pow(metresPerPixel / 100.0, 0.35)));

    /// <summary>
    /// Resamples the source grid onto the image pixels (plus a one-pixel border). Smooth Catmull-Rom when the map
    /// is zoomed in past the DEM resolution, bilinear otherwise. Land/water is decided like the stock map shader
    /// does it, half-way between a land and a water sample, so the tint ends where the stock coastline is.
    /// </summary>
    public static bool Resample(SourceGrid src, RasterJob job, Func<bool> cancelled)
    {
        int gw = job.Width + 2, gh = job.Height + 2;
        if (job.Heights == null || job.Heights.Length != gw * gh)
            job.Heights = new float[gw * gh];
        float[] h = job.Heights;
        float[] v = src.V;
        int nx = src.Nx, ny = src.Ny;
        bool cubic = src.Cell > job.Dpp * 1.5;

        var colHeight = new float[nx];
        var colLand = new float[nx];
        var ci = new int[gw];
        var cf = new float[gw];
        for (int x = 0; x < gw; x++)
        {
            double u = (job.West + (x - 0.5) * job.Dpp - src.Lon0) / src.Cell;
            int i = (int)Math.Floor(u);
            ci[x] = i;
            cf[x] = (float)(u - i);
        }

        for (int y = 0; y < gh; y++)
        {
            if ((y & 15) == 0 && cancelled())
                return false;
            double t = (src.Lat0 - (job.North - (y - 0.5) * job.Dpp)) / src.Cell;
            int j = (int)Math.Floor(t);
            float ty = (float)(t - j);
            int r0 = Clamp(j, ny) * nx, r1 = Clamp(j + 1, ny) * nx;
            if (cubic)
            {
                int rm = Clamp(j - 1, ny) * nx, r2 = Clamp(j + 2, ny) * nx;
                for (int i = 0; i < nx; i++)
                    colHeight[i] = CatmullRom(Land(v[rm + i]), Land(v[r0 + i]), Land(v[r1 + i]), Land(v[r2 + i]), ty);
            }
            else
            {
                for (int i = 0; i < nx; i++)
                    colHeight[i] = Lerp(Land(v[r0 + i]), Land(v[r1 + i]), ty);
            }
            for (int i = 0; i < nx; i++)
                colLand[i] = Lerp(v[r0 + i] >= 0f ? 1f : 0f, v[r1 + i] >= 0f ? 1f : 0f, ty);

            int o = y * gw;
            for (int x = 0; x < gw; x++)
            {
                int i = ci[x];
                float tx = cf[x];
                int c0 = Clamp(i, nx), c1 = Clamp(i + 1, nx);
                if (Lerp(colLand[c0], colLand[c1], tx) < 0.5f)
                {
                    h[o + x] = -1f;
                    continue;
                }
                float value = cubic
                    ? CatmullRom(colHeight[Clamp(i - 1, nx)], colHeight[c0], colHeight[c1], colHeight[Clamp(i + 2, nx)], tx)
                    : Lerp(colHeight[c0], colHeight[c1], tx);
                h[o + x] = value > 0f ? value : 0f;
            }
        }
        return true;
    }

    /// <summary>Colours the resampled heights into <see cref="RasterJob.Rgba"/>. Water stays transparent.</summary>
    public static bool Shade(RasterJob job, Func<bool> cancelled)
    {
        int w = job.Width, ht = job.Height, gw = w + 2;
        float[] h = job.Heights;
        if (job.Rgba == null || job.Rgba.Length != w * ht * 4)
            job.Rgba = new byte[w * ht * 4];
        byte[] px = job.Rgba;
        Palette pal = job.Palette;
        byte[] rgb = pal.Rgb;
        float tint = job.Opacity, shading = job.Hillshade;
        float minor = job.Interval, major = job.Interval * 5f;
        bool contours = minor > 0f;
        float danger = job.Danger;
        bool hatch = !float.IsNaN(danger);

        float my = (float)(job.Dpp * MetresPerDegree);
        float z = Exaggeration(my);
        float lr, lg, lb, minorAlpha, majorAlpha;
        if (pal.Dark) { lr = 178; lg = 168; lb = 148; minorAlpha = 0.3f; majorAlpha = 0.6f; }
        else { lr = 72; lg = 46; lb = 26; minorAlpha = 0.45f; majorAlpha = 0.8f; }

        for (int y = 0; y < ht; y++)
        {
            if ((y & 31) == 0 && cancelled())
                return false;
            double lat = job.North - (y + 0.5) * job.Dpp;
            float mx = my * (float)Math.Max(0.02, Math.Cos(lat * Math.PI / 180.0));
            float sx = z / (2f * mx), sy = z / (2f * my);
            int row = (y + 1) * gw + 1;
            int o = y * w * 4;
            for (int x = 0; x < w; x++, o += 4)
            {
                int i = row + x;
                float h0 = h[i];
                if (h0 < 0f)
                {
                    px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 0;
                    continue;
                }
                float hl = h[i - 1], hr = h[i + 1], hu = h[i - gw], hd = h[i + gw];
                bool landL = hl >= 0f, landR = hr >= 0f, landU = hu >= 0f, landD = hd >= 0f;
                if (!landL) hl = 0f;
                if (!landR) hr = 0f;
                if (!landU) hu = 0f;
                if (!landD) hd = 0f;

                int band = pal.BandOf(h0) * 3;
                float r = rgb[band], g = rgb[band + 1], b = rgb[band + 2];
                float a = tint;

                if (shading > 0f)
                {
                    // Surface normal (-dx, -dy, 1) against light from the north-west, 45° up; flat ground = 1.
                    float dx = (hr - hl) * sx, dy = (hu - hd) * sy;
                    float lit = (dx - dy + 1.41421356f) / (1.41421356f * (float)Math.Sqrt(dx * dx + dy * dy + 1f));
                    if (lit < 0f) lit = 0f;
                    float s = 1f + (lit - 1f) * shading;
                    r *= s;
                    g *= s;
                    b *= s;
                }

                if (hatch && h0 >= danger)
                {
                    float k = (((x + y) >> 2) & 1) == 0 ? 0.8f : 0.5f;
                    r += (232f - r) * k;
                    g += (36f - g) * k;
                    b += (28f - b) * k;
                    if (a < 0.75f) a = 0.75f;
                }

                // Distance in pixels from this pixel to a contour = height difference / height change per pixel.
                // That gives anti-aliased lines of the same width at every zoom; where lines would merge on steep
                // ground they fade out (bold ones hold on five times longer).
                float gxp = (hr - hl) * 0.5f, gyp = (hd - hu) * 0.5f;
                float grad = (float)Math.Sqrt(gxp * gxp + gyp * gyp);
                if (contours && grad > 1e-3f)
                {
                    float level = (float)Math.Round(h0 / minor) * minor;
                    float boldLevel = (float)Math.Round(h0 / major) * major;
                    float perPixel = grad / minor;
                    float la = 0f;
                    if (boldLevel > 0f)
                        la = Clamp01(1.6f - Math.Abs(h0 - boldLevel) / grad) * majorAlpha * Fade(perPixel / 5f);
                    if (level > 0f)       // level 0 is the coastline, which the stock map draws
                        la = Math.Max(la, Clamp01(1.1f - Math.Abs(h0 - level) / grad) * minorAlpha * Fade(perPixel));
                    if (la > 0f)
                    {
                        r += (lr - r) * la;
                        g += (lg - g) * la;
                        b += (lb - b) * la;
                        a += la * (1f - a);
                    }
                }

                if (hatch && grad > 1e-3f)
                {
                    // Solid red edge where the terrain reaches the aircraft's altitude.
                    float edge = Clamp01(1.8f - Math.Abs(h0 - danger) / grad);
                    if (edge > 0f)
                    {
                        r += (205f - r) * edge;
                        g += (18f - g) * edge;
                        b += (18f - b) * edge;
                        a += edge * (0.95f - a);
                    }
                }

                px[o] = ToByte(r);
                px[o + 1] = ToByte(g);
                px[o + 2] = ToByte(b);
                px[o + 3] = ToByte(a * 255f);
            }
        }
        return true;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    /// <summary>Line strength from how many lines meet per pixel: full up to 0.4, gone at 1.</summary>
    private static float Fade(float perPixel) => Math.Max(0f, Math.Min(1f, (1f - perPixel) / 0.6f));

    private static float Land(float v) => v > 0f ? v : 0f;

    private static int Clamp(int i, int n) => i < 0 ? 0 : (i >= n ? n - 1 : i);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

    private static byte ToByte(float v) => v <= 0f ? (byte)0 : (v >= 255f ? (byte)255 : (byte)(v + 0.5f));
}
