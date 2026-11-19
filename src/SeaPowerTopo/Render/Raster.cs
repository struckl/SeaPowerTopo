using System;
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

    /// <summary>
    /// The first sample's grid position and the step between samples (south, and east), in DEM grid points or in
    /// world grid texels: cell (i, j) holds the sample at (X0 + i * StepX, Y0 + j * Step).
    /// </summary>
    public int X0, Y0, Step, StepX;

    /// <summary>
    /// Plans the grid for an image: DEM samples (every n-th when zoomed out) or world grid texels at about the image's
    /// pixel size, with two cells of margin around it for the interpolation.
    /// </summary>
    public void Plan(bool dem, double dpp, double dppX, double west, double north, double east, double south)
    {
        if (dem)
        {
            Step = Math.Max(1, (int)Math.Round(dpp * DemGrid.DemPerDegree));
            StepX = Math.Max(1, (int)Math.Round(dppX * DemGrid.DemPerDegree));
            X0 = (int)Math.Floor((west + 180.0) * DemGrid.DemPerDegree) - 2 * StepX;
            Y0 = (int)Math.Floor((90.0 - north) * DemGrid.DemPerDegree) - 2 * Step;
            Lon0 = -180.0 + X0 / DemGrid.DemPerDegree;
            Lat0 = 90.0 - Y0 / DemGrid.DemPerDegree;
            Cell = Step / DemGrid.DemPerDegree;
            CellX = StepX / DemGrid.DemPerDegree;
        }
        else
        {
            Step = Math.Max(1, (int)Math.Round(dpp / DemGrid.LowCell));
            StepX = Math.Max(1, (int)Math.Round(dppX / DemGrid.LowCell));
            X0 = (int)Math.Floor((west + 180.0) / DemGrid.LowCell - 0.5) - 2 * StepX;
            Y0 = (int)Math.Floor((90.0 - north) / DemGrid.LowCell - 0.5) - 2 * Step;
            Lon0 = -180.0 + (X0 + 0.5) * DemGrid.LowCell;
            Lat0 = 90.0 - (Y0 + 0.5) * DemGrid.LowCell;
            Cell = Step * DemGrid.LowCell;
            CellX = StepX * DemGrid.LowCell;
        }
        Allocate((int)Math.Ceiling((east - Lon0) / CellX) + 3, (int)Math.Ceiling((Lat0 - south) / Cell) + 3);
    }

    private void Allocate(int nx, int ny)
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
    /// <summary>Chart themes: the safety contour in metres; the shallow and the deep contour follow from it (<see cref="Look.ChartDepths"/>).</summary>
    public float ChartSafety = Look.DefaultChartSafety;

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
internal static partial class Raster
{
    public const double MetresPerDegree = 111320.0;

    /// <summary>Raw DEM water shallower than this is made twice as deep again by the simulation.</summary>
    private const float ShallowDoubling = -48f;
    /// <summary>Smallest depth a water pixel carries, so it stays negative.</summary>
    private const float MinDepth = 0.01f;
    /// <summary>The seabed is shaded with more exaggeration than the land (it is flatter).</summary>
    private const float SeabedExaggeration = 2f;

    private static readonly float[] NiceIntervals = { 20, 50, 100, 200, 500, 1000 };

    /// <summary>The game's thermal layer tiles: 0.2° squares, numbered from 180° W and from the north pole.</summary>
    public const int LayerTilesX = 1800;
    private const float LayerTile = 0.2f, LayerOffset = 1f / 120f;
    /// <summary>The game's thermal layer: only where the water is deeper than this many feet, at 250 to 450 ft.</summary>
    public const float LayerMinSeaFeet = 500f, LayerMinFeet = 250f, LayerMaxFeet = 450f;
    public const float FeetToMetres = 0.3048f;
    /// <summary>Upper edges of the layer depth bands in metres (300, 350 and 400 ft); the last band is deeper.</summary>
    public static readonly float[] LayerBands = { 91.44f, 106.68f, 121.92f };
    /// <summary>Strength of the layer's teal where the submarine is below the layer, and where it is above.</summary>
    private const float LayerBelow = 0.4f, LayerAbove = 0.13f;

    // Half the cores: the game's own threads keep theirs.
    private static readonly int Workers = Math.Max(1, Math.Min(8, Environment.ProcessorCount / 2));

    /// <summary>
    /// A raw DEM value in metres as the simulation uses it: land times the height multiplier, water depth times the
    /// under-water depth factor (Options -> World) - and doubled once more where the DEM is shallower than 48 m, as
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

    public static void LayerTileCentre(int tileX, int tileY, out double longitude, out double latitude)
    {
        longitude = -180.0 + LayerOffset + (tileX + 0.5) * LayerTile;
        latitude = 90.0 - LayerOffset - (tileY + 0.5) * LayerTile;
    }

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

    private static float Land(float v) => v > 0f ? v : 0f;

    private static float Water(float v) => v < 0f ? v : 0f;

    private static int Clamp(int i, int n) => i < 0 ? 0 : (i >= n ? n - 1 : i);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }
}
