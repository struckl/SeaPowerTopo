using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using K4os.Compression.LZ4;
using SeaPowerTopo.Render;

namespace SeaPowerTopo.Preview;

/// <summary>
/// Renders one map view the way the plugin does: DEM samples copied onto the plugin's source grid (every n-th when
/// zoomed out, the world grid where no DEM chunk exists), resampled and coloured by the plugin's own Raster code at the
/// default settings (Look), over a flat stand-in for the stock map.
/// <code>
/// dotnet run --project tools/topo-preview -- --at 61.5,8 --span 2 --size 1600x900 [--danger 1200] [--dark]
///     [--interval 100] [--nodepth] [--depth-factor 2] [--out preview.png] [--game "E:\Games\steamapps\common\Sea Power"]
/// </code>
/// --theme picks the look (midnight, ecdis-day, genshtab, ... - see Theme.cs; the map theme follows from it, --dark picks
/// the default night look), --chart-safety the safety contour of the chart themes in metres.
/// --themes-dir loads a folder of theme files (as the mod does from BepInEx/config/SeaPowerTopo/themes; --theme then
/// takes their ids), --theme-file one such file. --check-themes tests the theme files: every built-in look written out and
/// read back, and a file with only a Base, must draw exactly what the original does.
/// --at is the centre (lat,lon), --span the view height in degrees, --size the map's pixel size; --nodepth leaves
/// the sea to the stock map, --depth-factor is the game's under-water depth factor (Options > World, default 2).
/// The pixels are as much wider in degrees as the globe map's are (cos of the latitude), as in the game.
/// --sub 120 draws what a selected submarine at 120 m sees: the thermal layer (with a stand-in for the game's
/// random layer depths, which need Unity) and water shallower than the submarine.
/// </summary>
internal static class Program
{
    private sealed class Options
    {
        public double Lat = 61.5, Lon = 8, Span = 2;
        public int Width = 1600, Height = 900;
        public float Danger = float.NaN, Interval = -1, DepthFactor = 2, Keel = float.NaN;
        public float ChartSafety = Look.DefaultChartSafety;
        public bool Dark, Depth = true, Check;
        public string ThemeId, ThemesDir, ThemeFile;
        public string Output = "preview.png", Game = @"E:\Games\steamapps\common\Sea Power";
    }

    // Each option and what it sets; the ones with a value take the next argument.
    private static readonly Dictionary<string, Action<Options, string>> WithValue = new Dictionary<string, Action<Options, string>>
    {
        ["--at"] = (o, v) => { string[] p = v.Split(','); o.Lat = D(p[0]); o.Lon = D(p[1]); },
        ["--span"] = (o, v) => o.Span = D(v),
        ["--size"] = (o, v) => { string[] s = v.Split('x'); o.Width = int.Parse(s[0]); o.Height = int.Parse(s[1]); },
        ["--danger"] = (o, v) => o.Danger = (float)D(v),
        ["--sub"] = (o, v) => o.Keel = (float)D(v),
        ["--interval"] = (o, v) => o.Interval = (float)D(v),
        ["--depth-factor"] = (o, v) => o.DepthFactor = (float)D(v),
        ["--theme"] = (o, v) => o.ThemeId = v,
        ["--themes-dir"] = (o, v) => o.ThemesDir = v,
        ["--theme-file"] = (o, v) => o.ThemeFile = v,
        ["--chart-safety"] = (o, v) => o.ChartSafety = (float)D(v),
        ["--out"] = (o, v) => o.Output = v,
        ["--game"] = (o, v) => o.Game = v,
    };

    private static readonly Dictionary<string, Action<Options>> Flags = new Dictionary<string, Action<Options>>
    {
        ["--dark"] = o => o.Dark = true,
        ["--nodepth"] = o => o.Depth = false,
        ["--check-themes"] = o => o.Check = true,
    };

    private static int Main(string[] args)
    {
        Options o = Parse(args);
        if (o == null)
            return 1;
        LoadThemeFiles(o);
        var terrain = new Terrain(o.Game);
        if (o.Check)
            return ThemeCheck.Run(terrain, o.DepthFactor);

        Theme theme = o.ThemeId == null ? Themes.Get(null, o.Dark) : Themes.Find(o.ThemeId);
        if (theme == null)
        {
            Console.Error.WriteLine($"Unknown theme {o.ThemeId}");
            return 1;
        }
        RasterJob job = NewJob(theme, o.Lat, o.Lon, o.Span, o.Width, o.Height, o.Depth, o.ChartSafety, o.Danger, o.Keel);
        if (o.Interval >= 0)
            job.Interval = o.Interval;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        SourceGrid src = terrain.Fill(job, o.DepthFactor, out bool dem);
        if (!float.IsNaN(o.Keel))
            job.Layer = StandInLayer(job, src);
        long fillMs = watch.ElapsedMilliseconds;
        Raster.Resample(src, job, () => false);
        long resampleMs = watch.ElapsedMilliseconds - fillMs;
        Raster.Shade(job, () => false);
        long shadeMs = watch.ElapsedMilliseconds - fillMs - resampleMs;

        Png.Write(o.Output, job.Width, job.Height, Composite(job, theme.Dark));
        Console.WriteLine($"{o.Output}: {job.Width}x{job.Height}, {(dem ? "DEM" : "world grid")}, {job.Dpp * DemGrid.DemPerDegree:0.00} DEM cells/px, " +
                          $"contours {job.Interval:0} m, fill {fillMs} ms, resample {resampleMs} ms, shade {shadeMs} ms");
        return 0;
    }

    private static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            if (WithValue.TryGetValue(args[i], out Action<Options, string> set) && i + 1 < args.Length)
                set(o, args[++i]);
            else if (Flags.TryGetValue(args[i], out Action<Options> flag))
                flag(o);
            else
            {
                Console.Error.WriteLine($"Unknown option {args[i]}");
                return null;
            }
        }
        return o;
    }

    // A folder of theme files, or one file on its own as a folder of one.
    private static void LoadThemeFiles(Options o)
    {
        if (o.ThemeFile != null)
        {
            o.ThemesDir = Path.Combine(Path.GetTempPath(), "topo-preview-theme");
            if (Directory.Exists(o.ThemesDir))
                Directory.Delete(o.ThemesDir, true);
            Directory.CreateDirectory(o.ThemesDir);
            File.Copy(o.ThemeFile, Path.Combine(o.ThemesDir, Path.GetFileName(o.ThemeFile)));
        }
        if (o.ThemesDir == null)
            return;
        ThemeLibrary.SetFolder(o.ThemesDir);
        ThemeLibrary.Load(Console.WriteLine, m => Console.Error.WriteLine("warning: " + m));
        if (o.ThemeFile != null && o.ThemeId == null && ThemeLibrary.Loaded > 0)
            o.ThemeId = Themes.Light.Concat(Themes.Dark).First(t => t.Custom).Id;
    }

    /// <summary>
    /// The image the plugin would draw for this view at the default settings: its palettes, opacity, hill shading and
    /// contour step, with pixels as much wider in degrees as the plugin makes them on the globe.
    /// </summary>
    internal static RasterJob NewJob(Theme theme, double lat, double lon, double span, int width, int height,
                                     bool depth, float chartSafety, float danger, float keel)
    {
        double dpp = span / height;
        double dppX = dpp * new GeoRect(lon - 1, lat - span / 2, lon + 1, lat + span / 2).GlobeLonStretch;
        return new RasterJob
        {
            Width = width,
            Height = height,
            Dpp = dpp,
            DppX = dppX,
            West = lon - dppX * width / 2,
            North = lat + span / 2,
            Palette = new Palette(Palette.DefaultLimits, theme),
            Sea = depth || theme.IsChart ? new Palette(Palette.DefaultDepthLimits, theme, sea: true) : null,
            Opacity = Look.Opacity(theme, Look.DefaultOpacity),
            ChartSafety = chartSafety,
            Hillshade = Look.DefaultHillshade,
            Interval = Raster.AutoInterval(dpp * Raster.MetresPerDegree * theme.LandIntervalScale),
            Danger = danger,
            Keel = keel,
        };
    }

    // The game's tiles and its 500 ft rule, but a hash for the layer depth instead of Unity's seeded random.
    private static LayerTiles StandInLayer(RasterJob job, SourceGrid src)
    {
        // One tile more all round, for the soft edges.
        int x0 = Raster.LayerTileX(job.West) - 1, nx = Raster.LayerTileX(job.East) - x0 + 2;
        int y0 = Raster.LayerTileY(job.North) - 1, ny = Raster.LayerTileY(job.South) - y0 + 2;
        var tiles = new LayerTiles { X0 = x0, Y0 = y0, Nx = nx, Ny = ny, Depth = new float[nx * ny] };
        float span = Raster.LayerMaxFeet - Raster.LayerMinFeet;
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                Raster.LayerTileCentre(x0 + i, y0 + j, out double lon, out double lat);
                int si = Math.Max(0, Math.Min(src.Nx - 1, (int)Math.Round((lon - src.Lon0) / src.CellLon)));
                int sj = Math.Max(0, Math.Min(src.Ny - 1, (int)Math.Round((src.Lat0 - lat) / src.Cell)));
                float sea = -src.V[sj * src.Nx + si];
                uint hash = (uint)((x0 + i) * 73856093) ^ (uint)((y0 + j) * 19349663);
                tiles.Depth[j * nx + i] = sea / Raster.FeetToMetres > Raster.LayerMinSeaFeet
                    ? (Raster.LayerMinFeet + hash % (uint)span) * Raster.FeetToMetres : float.NaN;
            }
        }
        return tiles;
    }

    // Stand-in for the stock map underneath: flat tan land, blue-grey water (day) or near-black (night).
    private static byte[] Composite(RasterJob job, bool dark)
    {
        var rgb = new byte[job.Width * job.Height * 3];
        int gw = job.Width + 2;
        for (int y = 0; y < job.Height; y++)
        {
            for (int x = 0; x < job.Width; x++)
            {
                bool water = job.Heights[(y + 1) * gw + x + 1] < 0f;
                float br, bg, bb;
                if (dark) { br = water ? 34 : 30; bg = water ? 40 : 30; bb = water ? 47 : 30; }
                else { br = water ? 144 : 196; bg = water ? 163 : 186; bb = water ? 182 : 170; }
                int o = (y * job.Width + x) * 4;
                float a = job.Rgba[o + 3] / 255f;
                int q = (y * job.Width + x) * 3;
                rgb[q] = (byte)(br + (job.Rgba[o] - br) * a);
                rgb[q + 1] = (byte)(bg + (job.Rgba[o + 1] - bg) * a);
                rgb[q + 2] = (byte)(bb + (job.Rgba[o + 2] - bb) * a);
            }
        }
        return rgb;
    }

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>The game's terrain files read the way the game reads them, and the plugin's source grid filled from them.</summary>
internal sealed class Terrain
{
    private readonly string dataDir;
    private readonly short[] lowres;
    private readonly Dictionary<int, short[]> chunks = new Dictionary<int, short[]>();

    public Terrain(string game)
    {
        dataDir = Path.Combine(game, "Sea Power_Data", "StreamingAssets", "original", "terrain", "data");
        byte[] bytes = File.ReadAllBytes(Path.Combine(dataDir, "lowres.raw"));
        lowres = new short[DemGrid.LowCols * DemGrid.LowRows];
        Buffer.BlockCopy(bytes, 0, lowres, 0, Math.Min(bytes.Length, lowres.Length * 2));
    }

    /// <summary>The source grid for the image as the plugin plans it (OverlayRenderer.Begin), with the heights as the simulation has them.</summary>
    public SourceGrid Fill(RasterJob job, float depthFactor, out bool dem)
    {
        var src = new SourceGrid();
        dem = job.Dpp < DemGrid.DemLimitDpp;
        src.Plan(dem, job.Dpp, job.DppLon, job.West, job.North, job.East, job.South);
        for (int j = 0; j < src.Ny; j++)
        {
            int y = src.Y0 + j * src.Step;
            for (int i = 0; i < src.Nx; i++)
            {
                int x = src.X0 + i * src.StepX;
                float raw = dem ? Dem(x, y) : Low(x, y);
                src.V[j * src.Nx + i] = Raster.SimHeight(raw, 1f, depthFactor);
            }
        }
        return src;
    }

    private float Dem(int gx, int gy)
    {
        gx = ((gx % DemGrid.DemCols) + DemGrid.DemCols) % DemGrid.DemCols;
        gy = Math.Max(0, Math.Min(DemGrid.DemRows - 1, gy));
        int cx = gx / DemGrid.ChunkSize, cy = gy / DemGrid.ChunkSize;
        short[] chunk = Chunk(cx, cy);
        if (chunk != null)
            return chunk[(gy - cy * DemGrid.ChunkSize) * DemGrid.ChunkSize + (gx - cx * DemGrid.ChunkSize)];
        DemGrid.LowCell4(-180.0 + gx / DemGrid.DemPerDegree, 90.0 - gy / DemGrid.DemPerDegree, out int i, out int j, out float tx, out float ty);
        return DemGrid.Bilerp(Low(i, j), Low(i + 1, j), Low(i, j + 1), Low(i + 1, j + 1), tx, ty);
    }

    private short Low(int lx, int ly)
    {
        lx = ((lx % DemGrid.LowCols) + DemGrid.LowCols) % DemGrid.LowCols;
        ly = Math.Max(0, Math.Min(DemGrid.LowRows - 1, ly));
        return lowres[ly * DemGrid.LowCols + lx];
    }

    // A chunk's file by the game's name for it (Utils.getDEMFileName: y1x01.lz4 ...), null where there is none.
    private short[] Chunk(int cx, int cy)
    {
        int key = cy * DemGrid.ChunksX + cx;
        if (chunks.TryGetValue(key, out short[] cached))
            return cached;
        string path = Path.Combine(dataDir, $"y{cy + 1}x{cx + 1:00}.lz4");
        short[] chunk = null;
        if (File.Exists(path))
        {
            // LZ4 block with the uncompressed length as a 4-byte footer (the game's LZ4.decompressBufferFixed).
            byte[] packed = File.ReadAllBytes(path);
            int length = BitConverter.ToInt32(packed, packed.Length - 4);
            var bytes = new byte[length];
            LZ4Codec.Decode(packed, 0, packed.Length - 4, bytes, 0, length);
            chunk = new short[DemGrid.ChunkSize * DemGrid.ChunkSize];
            Buffer.BlockCopy(bytes, 0, chunk, 0, chunk.Length * 2);
        }
        chunks[key] = chunk;
        return chunk;
    }
}

/// <summary>A plain RGB PNG writer, so the preview needs no imaging library.</summary>
internal static class Png
{
    public static void Write(string path, int width, int height, byte[] rgb)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BigEndian(header, 0, width);
        BigEndian(header, 4, height);
        header[8] = 8;   // bit depth
        header[9] = 2;   // RGB
        WriteChunk(file, "IHDR", header);
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgb, y * width * 3, width * 3);
            }
        }
        WriteChunk(file, "IDAT", packed.ToArray());
        WriteChunk(file, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BigEndian(len, 0, data.Length);
        s.Write(len);
        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++)
            body[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, body, 4, data.Length);
        s.Write(body);
        var crc = new byte[4];
        BigEndian(crc, 0, (int)Crc32(body));
        s.Write(crc);
    }

    private static void BigEndian(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
