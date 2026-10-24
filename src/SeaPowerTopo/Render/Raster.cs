using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeaPowerTopo.Render;

/// <summary>Terrain heights on a regular lon/lat grid, copied out of the game's DEM on the main thread.</summary>
internal sealed class SourceGrid
{
    /// <summary>Centre of cell (0, 0) and the cell size in degrees; rows run south, columns east.</summary>
    public double Lon0, Lat0, Cell;
    public int Nx, Ny;
    /// <summary>Heights in metres as the simulation uses them (<see cref="Raster.SimHeight"/>); negative = water.</summary>
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
    /// <summary>Depth bands of the sea; null leaves the water to the stock map.</summary>
    public Palette Sea;
    public float Opacity, Hillshade;
    /// <summary>Metres between contour lines; 0 = none.</summary>
    public float Interval;
    /// <summary>Terrain at or above this height (metres) is hatched red; NaN = off.</summary>
    public float Danger = float.NaN;

    /// <summary>
    /// (Width + 2) × (Height + 2) heights in metres with a one-pixel border: land 0 or more, water below 0 (minus
    /// its depth, never quite 0, so the sign alone tells water from land).
    /// </summary>
    public float[] Heights;
    /// <summary>Width × Height RGBA, straight alpha, row 0 = north.</summary>
    public byte[] Rgba;
    /// <summary>With <see cref="Raster.Mipmaps"/>: the image followed by every smaller mip level, as a texture takes it.</summary>
    public byte[] Mips;

    public double South => North - Height * Dpp;
    public double East => West + Width * Dpp;
}

/// <summary>
/// Turns DEM heights into the overlay image: hypsometric tint, hill shading, contour lines and the red hatching
/// for terrain above the selected aircraft; the sea tinted by depth with depth lines at the band edges. Runs on a
/// worker thread; plain C# so the offline preview shares it.
/// </summary>
internal static class Raster
{
    public const double MetresPerDegree = 111320.0;

    /// <summary>Raw DEM water shallower than this is made twice as deep again by the simulation.</summary>
    private const float ShallowDoubling = -48f;
    /// <summary>Smallest depth a water pixel carries, so it stays negative.</summary>
    private const float MinDepth = 0.01f;
    /// <summary>The seabed is shaded this much weaker than the land, but with more exaggeration (it is flatter).</summary>
    private const float SeabedShading = 0.5f, SeabedExaggeration = 2f;

    private static readonly float[] NiceIntervals = { 20, 50, 100, 200, 500, 1000 };

    // Half the cores: the game's own threads keep theirs.
    private static readonly int Workers = Math.Max(1, Math.Min(8, Environment.ProcessorCount / 2));

    /// <summary>
    /// A raw DEM value in metres as the simulation uses it: land times the height multiplier, water depth times the
    /// under-water depth factor (Options → World) – and doubled once more where the DEM is shallower than 48 m, as
    /// every terrain lookup of the game does (TerrainUtils.GetHeightAt, the 3D terrain). So a thin ring of water
    /// just past the DEM's 48 m line is shallower than the water inshore of it.
    /// </summary>
    public static float SimHeight(float raw, float heightMultiplier, float depthFactor)
    {
        if (raw >= 0f)
            return raw * heightMultiplier;
        float depth = raw * depthFactor;
        return raw > ShallowDoubling ? depth * 2f : depth;
    }

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
    /// does it, half-way between a land and a water sample, so the tint ends where the stock coastline is. Land
    /// heights and water depths are interpolated apart, each with the other side counting as sea level.
    /// </summary>
    public static bool Resample(SourceGrid src, RasterJob job, Func<bool> cancelled)
    {
        int gw = job.Width + 2, gh = job.Height + 2;
        if (job.Heights == null || job.Heights.Length != gw * gh)
            job.Heights = new float[gw * gh];
        bool cubic = src.Cell > job.Dpp * 1.5;

        var ci = new int[gw];
        var cf = new float[gw];
        for (int x = 0; x < gw; x++)
        {
            double u = (job.West + (x - 0.5) * job.Dpp - src.Lon0) / src.Cell;
            int i = (int)Math.Floor(u);
            ci[x] = i;
            cf[x] = (float)(u - i);
        }

        return Rows(gh, cancelled, (from, to) => ResampleRows(src, job, cubic, ci, cf, from, to));
    }

    private static void ResampleRows(SourceGrid src, RasterJob job, bool cubic, int[] ci, float[] cf, int from, int to)
    {
        int gw = job.Width + 2;
        float[] h = job.Heights;
        float[] v = src.V;
        int nx = src.Nx, ny = src.Ny;
        var colHeight = new float[nx];
        var colDepth = new float[nx];
        var colLand = new float[nx];
        for (int y = from; y < to; y++)
        {
            double t = (src.Lat0 - (job.North - (y - 0.5) * job.Dpp)) / src.Cell;
            int j = (int)Math.Floor(t);
            float ty = (float)(t - j);
            int r0 = Clamp(j, ny) * nx, r1 = Clamp(j + 1, ny) * nx;
            if (cubic)
            {
                int rm = Clamp(j - 1, ny) * nx, r2 = Clamp(j + 2, ny) * nx;
                for (int i = 0; i < nx; i++)
                {
                    float a = v[rm + i], b = v[r0 + i], c = v[r1 + i], d = v[r2 + i];
                    colHeight[i] = CatmullRom(Land(a), Land(b), Land(c), Land(d), ty);
                    colDepth[i] = CatmullRom(Water(a), Water(b), Water(c), Water(d), ty);
                }
            }
            else
            {
                for (int i = 0; i < nx; i++)
                {
                    colHeight[i] = Lerp(Land(v[r0 + i]), Land(v[r1 + i]), ty);
                    colDepth[i] = Lerp(Water(v[r0 + i]), Water(v[r1 + i]), ty);
                }
            }
            for (int i = 0; i < nx; i++)
                colLand[i] = Lerp(v[r0 + i] >= 0f ? 1f : 0f, v[r1 + i] >= 0f ? 1f : 0f, ty);

            int o = y * gw;
            for (int x = 0; x < gw; x++)
            {
                int i = ci[x];
                float tx = cf[x];
                int c0 = Clamp(i, nx), c1 = Clamp(i + 1, nx);
                bool land = Lerp(colLand[c0], colLand[c1], tx) >= 0.5f;
                float[] col = land ? colHeight : colDepth;
                float value = cubic
                    ? CatmullRom(col[Clamp(i - 1, nx)], col[c0], col[c1], col[Clamp(i + 2, nx)], tx)
                    : Lerp(col[c0], col[c1], tx);
                if (land)
                    h[o + x] = value > 0f ? value : 0f;
                else
                    h[o + x] = value < -MinDepth ? value : -MinDepth;
            }
        }
    }

    /// <summary>
    /// Colours the resampled heights into <see cref="RasterJob.Rgba"/>. Water is tinted by depth, or stays
    /// transparent without a sea palette.
    /// </summary>
    public static bool Shade(RasterJob job, Func<bool> cancelled)
    {
        int w = job.Width, ht = job.Height;
        if (job.Rgba == null || job.Rgba.Length != w * ht * 4)
            job.Rgba = new byte[w * ht * 4];
        return Rows(ht, cancelled, (from, to) => ShadeRows(job, from, to));
    }

    private static void ShadeRows(RasterJob job, int from, int to)
    {
        int w = job.Width, gw = w + 2;
        float[] h = job.Heights;
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

        for (int y = from; y < to; y++)
        {
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
                    if (job.Sea == null)
                        px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 0;
                    else
                        ShadeWater(job, i, sx, sy, contours, px, o);
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
    }

    /// <summary>
    /// Fills <see cref="RasterJob.Mips"/>: the image and every half-size level down to 1 × 1, for a pre-drawn layer
    /// seen zoomed out (without them its contour lines flicker). Colours are averaged by their alpha, so the edges
    /// of transparent areas don't darken.
    /// </summary>
    public static bool Mipmaps(RasterJob job, Func<bool> cancelled)
    {
        int w = job.Width, h = job.Height, levels = MipCount(w, h);
        long total = 0;
        for (int k = 0; k < levels; k++)
            total += (long)Math.Max(1, w >> k) * Math.Max(1, h >> k) * 4;
        if (job.Mips == null || job.Mips.Length != total)
            job.Mips = new byte[total];
        byte[] m = job.Mips;
        Buffer.BlockCopy(job.Rgba, 0, m, 0, w * h * 4);
        int src = 0, sw = w, sh = h;
        int dst = w * h * 4;
        for (int k = 1; k < levels; k++)
        {
            if (cancelled())
                return false;
            int dw = Math.Max(1, sw >> 1), dh = Math.Max(1, sh >> 1);
            for (int y = 0; y < dh; y++)
            {
                int y0 = Math.Min(sh - 1, y * 2), y1 = Math.Min(sh - 1, y * 2 + 1);
                for (int x = 0; x < dw; x++)
                {
                    int x0 = Math.Min(sw - 1, x * 2), x1 = Math.Min(sw - 1, x * 2 + 1);
                    int p0 = src + (y0 * sw + x0) * 4, p1 = src + (y0 * sw + x1) * 4;
                    int p2 = src + (y1 * sw + x0) * 4, p3 = src + (y1 * sw + x1) * 4;
                    int a0 = m[p0 + 3], a1 = m[p1 + 3], a2 = m[p2 + 3], a3 = m[p3 + 3];
                    int asum = a0 + a1 + a2 + a3;
                    int o = dst + (y * dw + x) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        m[o + c] = asum == 0
                            ? (byte)((m[p0 + c] + m[p1 + c] + m[p2 + c] + m[p3 + c] + 2) >> 2)
                            : (byte)((m[p0 + c] * a0 + m[p1 + c] * a1 + m[p2 + c] * a2 + m[p3 + c] * a3 + asum / 2) / asum);
                    }
                    m[o + 3] = (byte)((asum + 2) >> 2);
                }
            }
            src = dst;
            dst += dw * dh * 4;
            sw = dw;
            sh = dh;
        }
        return true;
    }

    /// <summary>Mip levels of a full chain, as Unity counts them.</summary>
    public static int MipCount(int width, int height)
    {
        int levels = 1;
        for (int size = Math.Max(width, height); size > 1; size >>= 1)
            levels++;
        return levels;
    }

    /// <summary>Runs <paramref name="band"/> over rows [0, count) in bands on several cores; false if cancelled.</summary>
    private static bool Rows(int count, Func<bool> cancelled, Action<int, int> band)
    {
        int bands = Math.Max(1, Math.Min(Workers * 4, count / 16));
        int size = (count + bands - 1) / bands;
        int stopped = 0;
        Parallel.For(0, bands, new ParallelOptions { MaxDegreeOfParallelism = Workers }, (k, state) =>
        {
            if (Volatile.Read(ref stopped) != 0 || cancelled())
            {
                Interlocked.Exchange(ref stopped, 1);
                state.Stop();
                return;
            }
            int from = k * size, to = Math.Min(count, from + size);
            if (from < to)
                band(from, to);
        });
        return stopped == 0;
    }

    // One water pixel: the colour of its depth band, the seabed's relief (fainter than on land, with more
    // exaggeration) and, like on a chart, a thin depth line at every band edge but the coast, which the stock map
    // draws. Lines are light on dark bands and dark on light ones.
    private static void ShadeWater(RasterJob job, int i, float sx, float sy, bool lines, byte[] px, int o)
    {
        float[] h = job.Heights;
        Palette sea = job.Sea;
        int gw = job.Width + 2;
        float d0 = -h[i];
        // Land next to the water counts as sea level.
        float hl = Math.Min(h[i - 1], 0f), hr = Math.Min(h[i + 1], 0f), hu = Math.Min(h[i - gw], 0f), hd = Math.Min(h[i + gw], 0f);

        int bandIndex = sea.BandOf(d0);
        int band = bandIndex * 3;
        float r = sea.Rgb[band], g = sea.Rgb[band + 1], b = sea.Rgb[band + 2];
        float a = job.Opacity;
        bool darkBand = 0.3f * r + 0.59f * g + 0.11f * b < 110f;

        float shading = job.Hillshade * SeabedShading;
        if (shading > 0f)
        {
            float dx = (hr - hl) * sx * SeabedExaggeration, dy = (hu - hd) * sy * SeabedExaggeration;
            float lit = (dx - dy + 1.41421356f) / (1.41421356f * (float)Math.Sqrt(dx * dx + dy * dy + 1f));
            if (lit < 0f) lit = 0f;
            float s = 1f + (lit - 1f) * shading;
            r *= s;
            g *= s;
            b *= s;
        }

        float gxp = (hr - hl) * 0.5f, gyp = (hd - hu) * 0.5f;
        float grad = (float)Math.Sqrt(gxp * gxp + gyp * gyp);
        if (lines && grad > 1e-3f)
        {
            float[] limits = sea.Limits;
            int n = limits.Length;
            float shallower = bandIndex > 0 ? d0 - limits[bandIndex] : float.PositiveInfinity;
            float deeper = bandIndex + 1 < n ? limits[bandIndex + 1] - d0 : float.PositiveInfinity;
            int edge = shallower <= deeper ? bandIndex : bandIndex + 1;
            float dist = Math.Min(shallower, deeper);
            if (!float.IsInfinity(dist))
            {
                float spacing = limits[edge] - limits[edge - 1];
                if (edge + 1 < n)
                    spacing = Math.Min(spacing, limits[edge + 1] - limits[edge]);
                float la = Clamp01(1.1f - dist / grad) * (sea.Dark ? 0.35f : 0.45f) * Fade(grad / spacing);
                if (la > 0f)
                {
                    float lr = darkBand ? 176f : 28f, lg = darkBand ? 196f : 48f, lb = darkBand ? 216f : 82f;
                    r += (lr - r) * la;
                    g += (lg - g) * la;
                    b += (lb - b) * la;
                    a += la * (1f - a);
                }
            }
        }

        px[o] = ToByte(r);
        px[o + 1] = ToByte(g);
        px[o + 2] = ToByte(b);
        px[o + 3] = ToByte(a * 255f);
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    /// <summary>Line strength from how many lines meet per pixel: full up to 0.4, gone at 1.</summary>
    private static float Fade(float perPixel) => Math.Max(0f, Math.Min(1f, (1f - perPixel) / 0.6f));

    private static float Land(float v) => v > 0f ? v : 0f;

    private static float Water(float v) => v < 0f ? v : 0f;

    private static int Clamp(int i, int n) => i < 0 ? 0 : (i >= n ? n - 1 : i);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

    private static byte ToByte(float v) => v <= 0f ? (byte)0 : (v >= 255f ? (byte)255 : (byte)(v + 0.5f));
}
