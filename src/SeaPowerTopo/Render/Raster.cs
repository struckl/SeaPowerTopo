using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SeaPowerTopo.Render;

/// <summary>Terrain heights on a regular lon/lat grid, copied out of the game's DEM on the main thread.</summary>
internal sealed class SourceGrid
{
    /// <summary>Centre of cell (0, 0) and the cell size in degrees; rows run south, columns east.</summary>
    public double Lon0, Lat0, Cell;
    /// <summary>East-west cell size in degrees where it differs from <see cref="Cell"/> (0 = the same).</summary>
    public double CellX;
    public double CellLon => CellX > 0 ? CellX : Cell;
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

/// <summary>The game's thermal layer over an image: depth in metres per 0.2° tile (NaN = no layer there).</summary>
internal sealed class LayerTiles
{
    /// <summary>First tile (game tile numbers, see <see cref="Raster.LayerTileX"/>) and the count each way.</summary>
    public int X0, Y0, Nx, Ny;
    public float[] Depth;

    /// <summary>Layer depth of a tile, NaN outside the grid or where there is none.</summary>
    public float At(int tileX, int tileY)
    {
        int i = tileX - X0, j = tileY - Y0;
        if (i < 0)
            i += Raster.LayerTilesX;
        if (i < 0 || i >= Nx || j < 0 || j >= Ny)
            return float.NaN;
        return Depth[j * Nx + i];
    }
}

/// <summary>One overlay image: where it lies on the map, how it is coloured, and its buffers.</summary>
internal sealed class RasterJob
{
    public int Width, Height;
    /// <summary>North-west corner of the image and degrees of latitude per pixel.</summary>
    public double West, North, Dpp;
    /// <summary>
    /// Degrees of longitude per pixel where they differ (0 = <see cref="Dpp"/>, square in lon/lat): the globe map
    /// shows a degree of longitude cos(latitude) times as wide as one of latitude, so an image for it can be that
    /// much narrower.
    /// </summary>
    public double DppX;
    public double DppLon => DppX > 0 ? DppX : Dpp;

    public Palette Palette;
    /// <summary>Depth bands of the sea; null leaves the water to the stock map.</summary>
    public Palette Sea;
    public float Opacity, Hillshade;
    /// <summary>Metres between contour lines; 0 = none.</summary>
    public float Interval;
    /// <summary>Terrain at or above this height (metres) is hatched red; NaN = off.</summary>
    public float Danger = float.NaN;
    /// <summary>Depth of the selected submarine in metres: water shallower than this is hatched red; NaN = off.</summary>
    public float Keel = float.NaN;
    /// <summary>Thermal layer to show for the selected submarine; null = none.</summary>
    public LayerTiles Layer;
    /// <summary>
    /// Per image column, for the soft edges of the thermal layer: the first of the two tile centres the column lies
    /// between, and how far (0..1) it is towards the second. Set by <see cref="Raster.Shade"/>.
    /// </summary>
    public int[] LayerI0;
    public float[] LayerFx;
    /// <summary>
    /// Chart themes: the safety contour in metres (the shallow and the deep contour follow from it), set by the
    /// caller; <see cref="Raster.Shade"/> works out the shades' edges and the contour depths from it.
    /// </summary>
    public float ChartSafety = 30f, ChartShallow = 5f, ChartDeep = 100f;
    public float[] ChartLevels;

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
    public double East => West + Width * DppLon;
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

    /// <summary>The game's thermal layer tiles: 0.2° squares, numbered from 180° W and from the north pole.</summary>
    public const int LayerTilesX = 1800;
    private const float LayerTile = 0.2f, LayerOffset = 1f / 120f;
    /// <summary>Upper edges of the layer depth bands in metres (300, 350 and 400 ft); the last band is deeper.</summary>
    public static readonly float[] LayerBands = { 91.44f, 106.68f, 121.92f };

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

    // Utils.longLatToTile, float for float, so the tile edges fall where the game's do.
    public static int LayerTileX(double longitude)
    {
        float x = (float)longitude + 180f - LayerOffset;
        if (x < 0f)
            x = 0f;
        int tile = (int)Math.Floor(x / LayerTile);
        if (tile < 0)
            tile += LayerTilesX;
        if (tile >= LayerTilesX)
            tile -= LayerTilesX;
        return tile;
    }

    public static int LayerTileY(double latitude) => (int)Math.Floor((Math.Abs((float)latitude - 90f) - LayerOffset) / LayerTile);

    // The same tile grid as a continuous coordinate: tile k spans [k, k + 1) and its centre is k + 0.5.
    public static float LayerTileXf(double longitude)
    {
        double lon = longitude + 180.0;
        lon -= Math.Floor(lon / 360.0) * 360.0;
        return (float)(lon - LayerOffset) / LayerTile;
    }

    public static float LayerTileYf(double latitude) => (Math.Abs((float)latitude - 90f) - LayerOffset) / LayerTile;

    private static int WrapTile(int tileX) => ((tileX % LayerTilesX) + LayerTilesX) % LayerTilesX;

    // The soft switch of the thermal layer: nothing below a weight of 0.3, full above 0.7, so the change from tile
    // to tile happens around the tile edge (where the weight is a half) and not as a hard block.
    private static float SoftWeight(float w)
    {
        float t = Clamp01((w - 0.3f) / 0.4f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Centre of a layer tile.</summary>
    public static void LayerTileCentre(int tileX, int tileY, out double longitude, out double latitude)
    {
        longitude = -180.0 + LayerOffset + (tileX + 0.5) * LayerTile;
        latitude = 90.0 - LayerOffset - (tileY + 0.5) * LayerTile;
    }

    /// <summary>Band of a layer depth (metres) in <see cref="LayerBands"/>.</summary>
    public static int LayerBand(float metres)
    {
        int band = 0;
        while (band < LayerBands.Length && metres >= LayerBands[band])
            band++;
        return band;
    }

    /// <summary>Colour of a layer depth band: one teal, a little darker the deeper the layer.</summary>
    public static void LayerColour(int band, bool dark, out float r, out float g, out float b)
    {
        float t = band / (float)LayerBands.Length;
        r = 110f + (60f - 110f) * t;
        g = 215f + (170f - 215f) * t;
        b = 205f + (175f - 205f) * t;
        if (dark)
        {
            r *= 0.7f;
            g *= 0.75f;
            b *= 0.8f;
        }
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
            double u = (job.West + (x - 0.5) * job.DppLon - src.Lon0) / src.CellLon;
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
        if (job.Palette.Theme.IsChart)
            PrepareChart(job);
        int[] layerCols = null;
        if (job.Layer != null)
        {
            layerCols = new int[w + 1];
            for (int x = -1; x < w; x++)
                layerCols[x + 1] = LayerTileX(job.West + (x + 0.5) * job.DppLon);
            job.LayerI0 = new int[w];
            job.LayerFx = new float[w];
            for (int x = 0; x < w; x++)
            {
                float s = LayerTileXf(job.West + (x + 0.5) * job.DppLon) - 0.5f;
                int i0 = (int)Math.Floor(s);
                job.LayerI0[x] = i0;
                job.LayerFx[x] = s - i0;
            }
        }
        return Rows(ht, cancelled, (from, to) => ShadeRows(job, layerCols, from, to));
    }

    private static readonly float[] ChartDepths = { 2, 5, 10, 20, 30, 50, 100, 200, 500, 1000, 2000, 3000, 4000, 5000, 6000 };

    // A chart's depths: the shallow, safety and deep contours set the four water shades, and contours run at the
    // standard chart depths and at those three.
    private static void PrepareChart(RasterJob job)
    {
        float safety = Math.Max(2f, job.ChartSafety);
        job.ChartShallow = Math.Max(2f, safety / 6f);
        job.ChartDeep = safety * 100f / 30f;
        var set = new SortedSet<float>(ChartDepths) { job.ChartShallow, safety, job.ChartDeep };
        var levels = new List<float>();
        foreach (float v in set)
        {
            // Neighbours closer than a tenth of a metre are one contour (a safety contour of 30.04 m is 30 m).
            if (levels.Count == 0 || v - levels[levels.Count - 1] > 0.1f)
                levels.Add(v);
        }
        job.ChartLevels = levels.ToArray();
    }

    private static void ShadeRows(RasterJob job, int[] layerCols, int from, int to)
    {
        int w = job.Width, gw = w + 2;
        float[] h = job.Heights;
        byte[] px = job.Rgba;
        Palette pal = job.Palette;
        Theme theme = pal.Theme;
        float tint = job.Opacity, shading = job.Hillshade;
        // The Standard themes draw with the user's hill shading as it is; the others have their own strength, which
        // the setting scales (0 = off, the default 0.6 = the theme's own).
        if (!theme.Legacy)
            shading = job.Hillshade <= 0f ? 0f : Math.Min(1.3f, theme.Shade * job.Hillshade / 0.6f);
        float minor = job.Interval, major = job.Interval * 5f;
        bool contours = minor > 0f;
        float danger = job.Danger;
        bool hatch = !float.IsNaN(danger);

        float my = (float)(job.Dpp * MetresPerDegree), mxEquator = (float)(job.DppLon * MetresPerDegree);
        float z = Exaggeration(my);
        float lr, lg, lb, minorAlpha, majorAlpha;
        if (pal.Dark) { lr = 178; lg = 168; lb = 148; minorAlpha = 0.3f; majorAlpha = 0.6f; }
        else { lr = 72; lg = 46; lb = 26; minorAlpha = 0.45f; majorAlpha = 0.8f; }
        float haloAlpha = 0f;
        if (!theme.Legacy && theme.LandLine != null)
        {
            lr = theme.LandLine[0];
            lg = theme.LandLine[1];
            lb = theme.LandLine[2];
            minorAlpha = theme.LandMinor;
            majorAlpha = theme.LandMajor;
            haloAlpha = theme.LandHalo;
        }

        bool submarine = job.Layer != null || !float.IsNaN(job.Keel);
        for (int y = from; y < to; y++)
        {
            double lat = job.North - (y + 0.5) * job.Dpp;
            float mx = mxEquator * (float)Math.Max(0.02, Math.Cos(lat * Math.PI / 180.0));
            float sx = z / (2f * mx), sy = z / (2f * my);
            int row = (y + 1) * gw + 1;
            int o = y * w * 4;
            int tileY = 0, tileYAbove = 0, layerJ0 = 0;
            float layerFy = 0f;
            if (job.Layer != null)
            {
                tileY = LayerTileY(lat);
                tileYAbove = LayerTileY(lat + job.Dpp);
                float ty = LayerTileYf(lat) - 0.5f;
                layerJ0 = (int)Math.Floor(ty);
                layerFy = ty - layerJ0;
            }
            for (int x = 0; x < w; x++, o += 4)
            {
                int i = row + x;
                float h0 = h[i];
                if (h0 < 0f)
                {
                    if (job.Sea == null)
                        px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 0;
                    else if (theme.IsChart)
                        ShadeChartWater(job, i, contours, px, o);
                    else
                        ShadeWater(job, i, sx, sy, contours, px, o);
                    if (submarine)
                        ShadeSubmarine(job, layerCols, x, y, i, tileY, tileYAbove, layerJ0, layerFy, px, o);
                    continue;
                }
                float hl = h[i - 1], hr = h[i + 1], hu = h[i - gw], hd = h[i + gw];
                bool landL = hl >= 0f, landR = hr >= 0f, landU = hu >= 0f, landD = hd >= 0f;
                if (!landL) hl = 0f;
                if (!landR) hr = 0f;
                if (!landU) hu = 0f;
                if (!landD) hd = 0f;

                float r, g, b;
                if (theme.LandFlat != null)
                {
                    r = theme.LandFlat[0];
                    g = theme.LandFlat[1];
                    b = theme.LandFlat[2];
                }
                else
                {
                    pal.Colour(h0, out r, out g, out b);
                }
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
                    if (!theme.Legacy)
                    {
                        // Shadows fall towards the theme's shadow colour, lit slopes towards its highlight.
                        if (s < 1f && theme.ShadowTint != null)
                        {
                            float tt = Clamp01((1f - s) * theme.ShadowAmt);
                            r += (theme.ShadowTint[0] - r) * tt;
                            g += (theme.ShadowTint[1] - g) * tt;
                            b += (theme.ShadowTint[2] - b) * tt;
                        }
                        else if (s > 1f && theme.HighlightTint != null)
                        {
                            float tt = Clamp01((s - 1f) * theme.HighlightAmt * 3f);
                            r += (theme.HighlightTint[0] - r) * tt;
                            g += (theme.HighlightTint[1] - g) * tt;
                            b += (theme.HighlightTint[2] - b) * tt;
                        }
                    }
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
                    if (haloAlpha > 0f && level > 0f)
                        la = Math.Max(la, Clamp01(1f - Math.Abs(h0 - level) / (grad * 3.5f)) * haloAlpha * Fade(perPixel));
                    if (la > 0f)
                    {
                        float lineR = lr, lineG = lg, lineB = lb;
                        if (theme.LandLineHigh != null)
                        {
                            // Lines graded by height: dim and in the low colour near the sea, full and in the high colour up high.
                            float tg = Clamp01(h0 / theme.LandLineSpan);
                            la *= theme.LandLineLowAmt + (1f - theme.LandLineLowAmt) * tg;
                            lineR = lr + (theme.LandLineHigh[0] - lr) * tg;
                            lineG = lg + (theme.LandLineHigh[1] - lg) * tg;
                            lineB = lb + (theme.LandLineHigh[2] - lb) * tg;
                        }
                        r += (lineR - r) * la;
                        g += (lineG - g) * la;
                        b += (lineB - b) * la;
                        a += la * (1f - a);
                    }
                }

                if (theme.Coast != null && (!landL || !landR || !landU || !landD))
                {
                    // The theme draws its own coastline on the land side of the shore.
                    float ck = theme.CoastAmt;
                    r += (theme.Coast[0] - r) * ck;
                    g += (theme.Coast[1] - g) * ck;
                    b += (theme.Coast[2] - b) * ck;
                    a += ck * (1f - a);
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

    // What a selected submarine needs on a water pixel: the thermal layer's tile in teal – clearly where the
    // submarine, at its depth, is below the layer, faintly where the layer is deeper than the submarine – with a line
    // where the layer area ends; and red hatching with a solid edge where the water is shallower than the submarine.
    private static void ShadeSubmarine(RasterJob job, int[] layerCols, int x, int y, int i, int tileY, int tileYAbove,
                                       int layerJ0, float layerFy, byte[] px, int o)
    {
        float r = px[o], g = px[o + 1], b = px[o + 2], a = px[o + 3] / 255f;
        bool dark = job.Palette.Dark;
        float d0 = -job.Heights[i];
        float keel = job.Keel;

        LayerTiles layer = job.Layer;
        if (layer != null)
        {
            int tileX = layerCols[x + 1];
            float depth = layer.At(tileX, tileY);
            bool here = !float.IsNaN(depth);
            // The edge of the layer area: the tile to the left or above differs in having a layer.
            bool edge = here != !float.IsNaN(layer.At(layerCols[x], tileY)) ||
                        here != !float.IsNaN(layer.At(tileX, tileYAbove));

            // The tint is soft: the state of each tile (the submarine below the layer, or above it) is blended with
            // its neighbours' by the distance from the tile centres and held to a narrow band around the tile edge,
            // so the squares of the game's model show as rounded patches, not hard blocks. Where two tiles differ the
            // switch lies on their common edge, as in the game.
            float wBelow = 0f, wAbove = 0f, wDepth = 0f, wExist = 0f;
            int i0 = job.LayerI0[x];
            float fx = job.LayerFx[x];
            for (int k = 0; k < 4; k++)
            {
                float td = layer.At(WrapTile(i0 + (k & 1)), layerJ0 + (k >> 1));
                if (float.IsNaN(td))
                    continue;
                float wk = ((k & 1) == 0 ? 1f - fx : fx) * ((k >> 1) == 0 ? 1f - layerFy : layerFy);
                wExist += wk;
                wDepth += wk * td;
                if (!float.IsNaN(keel) && keel > td)
                    wBelow += wk;
                else
                    wAbove += wk;
            }
            if (wExist > 0f)
            {
                LayerColour(LayerBand(wDepth / wExist), dark, out float cr, out float cg, out float cb);
                float alpha = LayerBelow * SoftWeight(wBelow) + LayerAbove * SoftWeight(wAbove);
                if (alpha > 0f)
                    Over(ref r, ref g, ref b, ref a, cr, cg, cb, alpha);
            }
            if (edge)
                Over(ref r, ref g, ref b, ref a, dark ? 90f : 0f, dark ? 220f : 150f, dark ? 210f : 150f, 0.7f);
        }

        if (!float.IsNaN(keel) && keel > 0f)
        {
            if (d0 < keel)
                Over(ref r, ref g, ref b, ref a, 232f, 36f, 28f, (((x + y) >> 2) & 1) == 0 ? 0.8f : 0.5f);
            int gw = job.Width + 2;
            float[] h = job.Heights;
            float gxp = (Math.Min(h[i + 1], 0f) - Math.Min(h[i - 1], 0f)) * 0.5f;
            float gyp = (Math.Min(h[i + gw], 0f) - Math.Min(h[i - gw], 0f)) * 0.5f;
            float grad = (float)Math.Sqrt(gxp * gxp + gyp * gyp);
            if (grad > 1e-3f)
            {
                float line = Clamp01(1.8f - Math.Abs(d0 - keel) / grad);
                if (line > 0f)
                    Over(ref r, ref g, ref b, ref a, 205f, 18f, 18f, line * 0.95f);
            }
        }

        px[o] = ToByte(r);
        px[o + 1] = ToByte(g);
        px[o + 2] = ToByte(b);
        px[o + 3] = ToByte(a * 255f);
    }

    /// <summary>Strength of the layer's teal where the submarine is below the layer, and where it is above.</summary>
    public const float LayerBelow = 0.4f, LayerAbove = 0.13f;

    // A colour of the given opacity laid over the pixel (straight alpha).
    private static void Over(ref float r, ref float g, ref float b, ref float a, float cr, float cg, float cb, float k)
    {
        float outA = k + a * (1f - k);
        if (outA <= 0f)
            return;
        float under = a * (1f - k) / outA, over = k / outA;
        r = cr * over + r * under;
        g = cg * over + g * under;
        b = cb * over + b * under;
        a = outA;
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
        sea.Colour(d0, out float r, out float g, out float b);
        float a = job.Opacity;
        bool darkBand = 0.3f * r + 0.59f * g + 0.11f * b < 110f;
        Theme theme = sea.Theme;

        float shading = job.Hillshade * SeabedShading;
        if (!theme.Legacy)
            shading = job.Hillshade <= 0f ? 0f : Math.Min(1.2f, theme.SeaShade * job.Hillshade / 0.6f);
        if (shading > 0f)
        {
            float dx = (hr - hl) * sx * SeabedExaggeration, dy = (hu - hd) * sy * SeabedExaggeration;
            float lit = (dx - dy + 1.41421356f) / (1.41421356f * (float)Math.Sqrt(dx * dx + dy * dy + 1f));
            if (lit < 0f) lit = 0f;
            float s = 1f + (lit - 1f) * shading;
            r *= s;
            g *= s;
            b *= s;
            if (!theme.Legacy && s < 1f && theme.ShadowTint != null)
            {
                float tt = Clamp01((1f - s) * theme.ShadowAmt * 0.6f);
                r += (theme.ShadowTint[0] - r) * tt;
                g += (theme.ShadowTint[1] - g) * tt;
                b += (theme.ShadowTint[2] - b) * tt;
            }
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
                float lr = darkBand ? 176f : 28f, lg = darkBand ? 196f : 48f, lb = darkBand ? 216f : 82f;
                if (!theme.Legacy && theme.SeaLine != null)
                {
                    // A line of the theme's colour with an optional glow; the shelf break (200 m) brightest.
                    bool shelf = Math.Abs(limits[edge] - 200f) < 1f;
                    int[] lc = shelf && theme.ShelfLine != null ? theme.ShelfLine : theme.SeaLine;
                    lr = lc[0];
                    lg = lc[1];
                    lb = lc[2];
                    float boost = shelf ? theme.ShelfBoost : 1f;
                    float core = Clamp01(1.1f - dist / grad) * Math.Min(1f, theme.SeaLineAmt * boost);
                    float halo = theme.SeaLineHalo > 0f ? Clamp01(1f - dist / (grad * 3.5f)) * theme.SeaLineHalo * boost : 0f;
                    la = Math.Max(core, halo) * Fade(grad / spacing);
                }
                if (la > 0f)
                {
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

    // A chart's water: the four depth shades of the theme, contours at the standard chart depths with the safety
    // contour bold in its own colour. No relief – a chart shows depths, not the shape of the sea floor.
    private static void ShadeChartWater(RasterJob job, int i, bool lines, byte[] px, int o)
    {
        Theme theme = job.Palette.Theme;
        float[] h = job.Heights;
        int gw = job.Width + 2;
        float d0 = -h[i];
        int[] zone = d0 < job.ChartShallow ? theme.Zones[0] : d0 < job.ChartSafety ? theme.Zones[1] : d0 < job.ChartDeep ? theme.Zones[2] : theme.Zones[3];
        float r = zone[0], g = zone[1], b = zone[2];
        float a = 1f;

        if (lines && theme.SeaLine != null)
        {
            // Land next to the water counts as sea level.
            float hl = Math.Min(h[i - 1], 0f), hr = Math.Min(h[i + 1], 0f), hu = Math.Min(h[i - gw], 0f), hd = Math.Min(h[i + gw], 0f);
            float gxp = (hr - hl) * 0.5f, gyp = (hd - hu) * 0.5f;
            float grad = (float)Math.Sqrt(gxp * gxp + gyp * gyp);
            float[] levels = job.ChartLevels;
            if (grad > 1e-3f && levels != null)
            {
                int n = levels.Length;
                int k = 0;
                while (k < n && levels[k] <= d0)
                    k++;                                      // the first contour deeper than this pixel
                float below = k > 0 ? d0 - levels[k - 1] : float.PositiveInfinity;
                float above = k < n ? levels[k] - d0 : float.PositiveInfinity;
                int edge = below <= above ? k - 1 : k;
                float dist = Math.Min(below, above);
                if (edge >= 0 && edge < n && !float.IsInfinity(dist))
                {
                    float spacing = float.PositiveInfinity;
                    if (edge > 0)
                        spacing = levels[edge] - levels[edge - 1];
                    if (edge + 1 < n)
                        spacing = Math.Min(spacing, levels[edge + 1] - levels[edge]);
                    if (float.IsInfinity(spacing))
                        spacing = levels[edge];
                    bool safety = Math.Abs(levels[edge] - job.ChartSafety) < 0.15f;
                    float la = Clamp01((safety ? 1.9f : 1.1f) - dist / grad) * Math.Min(1f, theme.SeaLineAmt) * Fade(grad / spacing / (safety ? 3f : 1f));
                    if (la > 0f)
                    {
                        int[] lc = safety && theme.SafetyLine != null ? theme.SafetyLine : theme.SeaLine;
                        r += (lc[0] - r) * la;
                        g += (lc[1] - g) * la;
                        b += (lc[2] - b) * la;
                    }
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
