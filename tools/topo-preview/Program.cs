using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using K4os.Compression.LZ4;
using SeaPowerTopo.Render;

namespace SeaPowerTopo.Preview;

/// <summary>
/// Renders one map view the way the plugin does: DEM samples copied onto a source grid (every n-th when zoomed
/// out, the world grid where no DEM chunk exists), resampled and coloured by the plugin's own Raster code, over a
/// flat stand-in for the stock map.
/// <code>
/// dotnet run --project tools/topo-preview -- --at 61.5,8 --span 2 --size 1600x900 [--danger 1200] [--dark]
///     [--interval 100] [--nodepth] [--depth-factor 2] [--out preview.png] [--game "E:\Games\steamapps\common\Sea Power"]
/// </code>
/// --theme picks the look (atlas, midnight, ecdis-day, ... – see Theme.cs; the map theme follows from it, --dark picks the
/// default night look), --chart-safety the safety contour of the chart themes in metres.
/// --at is the centre (lat,lon), --span the view height in degrees, --size the map's pixel size; --nodepth leaves
/// the sea to the stock map, --depth-factor is the game's under-water depth factor (Options → World, default 2).
/// --sub 120 draws what a selected submarine at 120 m sees: the thermal layer (with a stand-in for the game's
/// random layer depths, which need Unity) and water shallower than the submarine.
/// </summary>
internal static class Program
{
    private const int ChunkSize = 2400, DemCols = 43200, DemRows = 21600, LowCols = 7200, LowRows = 3600;
    private const double DemPerDegree = 120.0, LowCell = 0.05, DemLimitDpp = 0.04;

    private static string dataDir;
    private static short[] lowres;
    private static readonly Dictionary<int, short[]> Chunks = new Dictionary<int, short[]>();

    private static int Main(string[] args)
    {
        double lat = 61.5, lon = 8, span = 2;
        int width = 1600, height = 900;
        float danger = float.NaN, interval = -1, depthFactor = 2, keel = float.NaN;
        bool dark = false, depth = true;
        string themeId = null;
        float chartSafety = 30f;
        string output = "preview.png", game = @"E:\Games\steamapps\common\Sea Power";
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string next = i + 1 < args.Length ? args[i + 1] : null;
            switch (a)
            {
                case "--at":
                    string[] p = next.Split(',');
                    lat = D(p[0]);
                    lon = D(p[1]);
                    i++;
                    break;
                case "--span": span = D(next); i++; break;
                case "--size":
                    string[] s = next.Split('x');
                    width = int.Parse(s[0]);
                    height = int.Parse(s[1]);
                    i++;
                    break;
                case "--danger": danger = (float)D(next); i++; break;
                case "--sub": keel = (float)D(next); i++; break;
                case "--interval": interval = (float)D(next); i++; break;
                case "--dark": dark = true; break;
                case "--nodepth": depth = false; break;
                case "--depth-factor": depthFactor = (float)D(next); i++; break;
                case "--theme": themeId = next; i++; break;
                case "--chart-safety": chartSafety = (float)D(next); i++; break;
                case "--out": output = next; i++; break;
                case "--game": game = next; i++; break;
                default:
                    Console.Error.WriteLine($"Unknown option {a}");
                    return 1;
            }
        }

        Theme theme = themeId == null ? Themes.Get(null, dark) : Themes.Light.Find(t => t.Id == themeId) ?? Themes.Dark.Find(t => t.Id == themeId);
        if (theme == null)
        {
            Console.Error.WriteLine($"Unknown theme {themeId}");
            return 1;
        }
        dark = theme.Dark;
        dataDir = Path.Combine(game, "Sea Power_Data", "StreamingAssets", "original", "terrain", "data");
        lowres = ReadShorts(Path.Combine(dataDir, "lowres.raw"), LowCols * LowRows);

        double dpp = span / height;
        var job = new RasterJob
        {
            Width = width,
            Height = height,
            Dpp = dpp,
            West = lon - dpp * width / 2,
            North = lat + span / 2,
            Palette = new Palette(Palette.DefaultLimits, theme),
            Sea = depth || theme.IsChart ? new Palette(Palette.DefaultDepthLimits, theme, sea: true) : null,
            Opacity = Math.Min(1f, theme.Opacity),
            ChartSafety = chartSafety,
            Hillshade = 0.6f,
            Interval = interval >= 0 ? interval : Raster.AutoInterval(dpp * Raster.MetresPerDegree),
            Danger = danger,
            Keel = keel,
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        SourceGrid src = Fill(job, depthFactor, out bool dem);
        if (!float.IsNaN(keel))
            job.Layer = StandInLayer(job, src);
        long fillMs = watch.ElapsedMilliseconds;
        Raster.Resample(src, job, () => false);
        long resampleMs = watch.ElapsedMilliseconds - fillMs;
        Raster.Shade(job, () => false);
        long shadeMs = watch.ElapsedMilliseconds - fillMs - resampleMs;

        WritePng(output, width, height, Composite(job, dark));
        Console.WriteLine($"{output}: {width}x{height}, {(dem ? "DEM" : "world grid")}, {dpp * DemPerDegree:0.00} DEM cells/px, " +
                          $"contours {job.Interval:0} m, fill {fillMs} ms, resample {resampleMs} ms, shade {shadeMs} ms");
        return 0;
    }

    // Same source grid as OverlayRenderer.StartFull / FillRow.
    // The game's tiles and its 500 ft rule, but a hash for the layer depth instead of Unity's seeded random.
    private static LayerTiles StandInLayer(RasterJob job, SourceGrid src)
    {
        int x0 = Raster.LayerTileX(job.West), nx = Raster.LayerTileX(job.East) - x0 + 1;
        int y0 = Raster.LayerTileY(job.North), ny = Raster.LayerTileY(job.South) - y0 + 1;
        var tiles = new LayerTiles { X0 = x0, Y0 = y0, Nx = nx, Ny = ny, Depth = new float[nx * ny] };
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                Raster.LayerTileCentre(x0 + i, y0 + j, out double lon, out double lat);
                int si = Math.Max(0, Math.Min(src.Nx - 1, (int)Math.Round((lon - src.Lon0) / src.Cell)));
                int sj = Math.Max(0, Math.Min(src.Ny - 1, (int)Math.Round((src.Lat0 - lat) / src.Cell)));
                float sea = -src.V[sj * src.Nx + si];
                uint hash = (uint)((x0 + i) * 73856093) ^ (uint)((y0 + j) * 19349663);
                tiles.Depth[j * nx + i] = sea / 0.3048f > 500f ? (250f + hash % 200) * 0.3048f : float.NaN;
            }
        }
        return tiles;
    }

    private static SourceGrid Fill(RasterJob job, float depthFactor, out bool dem)
    {
        var src = new SourceGrid();
        double dpp = job.Dpp;
        dem = dpp < DemLimitDpp;
        int step, x0, y0;
        if (dem)
        {
            step = Math.Max(1, (int)Math.Round(dpp * DemPerDegree));
            x0 = (int)Math.Floor((job.West + 180.0) * DemPerDegree) - 2 * step;
            y0 = (int)Math.Floor((90.0 - job.North) * DemPerDegree) - 2 * step;
            src.Lon0 = -180.0 + x0 / DemPerDegree;
            src.Lat0 = 90.0 - y0 / DemPerDegree;
            src.Cell = step / DemPerDegree;
        }
        else
        {
            step = Math.Max(1, (int)Math.Round(dpp / LowCell));
            x0 = (int)Math.Floor((job.West + 180.0) / LowCell - 0.5) - 2 * step;
            y0 = (int)Math.Floor((90.0 - job.North) / LowCell - 0.5) - 2 * step;
            src.Lon0 = -180.0 + (x0 + 0.5) * LowCell;
            src.Lat0 = 90.0 - (y0 + 0.5) * LowCell;
            src.Cell = step * LowCell;
        }
        src.Allocate((int)Math.Ceiling((job.East - src.Lon0) / src.Cell) + 3, (int)Math.Ceiling((src.Lat0 - job.South) / src.Cell) + 3);
        for (int j = 0; j < src.Ny; j++)
        {
            for (int i = 0; i < src.Nx; i++)
            {
                float raw = dem
                    ? Dem(x0 + i * step, y0 + j * step)
                    : Low(x0 + i * step, y0 + j * step);
                src.V[j * src.Nx + i] = Raster.SimHeight(raw, 1f, depthFactor);
            }
        }
        return src;
    }

    private static float Dem(int gx, int gy)
    {
        gx = ((gx % DemCols) + DemCols) % DemCols;
        gy = Math.Max(0, Math.Min(DemRows - 1, gy));
        int cx = gx / ChunkSize, cy = gy / ChunkSize;
        short[] chunk = Chunk(cx, cy);
        if (chunk == null)
        {
            double lon = -180.0 + gx / DemPerDegree, lat = 90.0 - gy / DemPerDegree;
            double u = (lon + 180.0) / LowCell - 0.5, v = (90.0 - lat) / LowCell - 0.5;
            int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
            float tx = (float)(u - i), ty = (float)(v - j);
            float top = Low(i, j) + (Low(i + 1, j) - Low(i, j)) * tx;
            float bottom = Low(i, j + 1) + (Low(i + 1, j + 1) - Low(i, j + 1)) * tx;
            return top + (bottom - top) * ty;
        }
        return chunk[(gy - cy * ChunkSize) * ChunkSize + (gx - cx * ChunkSize)];
    }

    private static short Low(int lx, int ly)
    {
        lx = ((lx % LowCols) + LowCols) % LowCols;
        ly = Math.Max(0, Math.Min(LowRows - 1, ly));
        return lowres[ly * LowCols + lx];
    }

    private static short[] Chunk(int cx, int cy)
    {
        int key = cy * 18 + cx;
        if (Chunks.TryGetValue(key, out short[] cached))
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
            chunk = new short[ChunkSize * ChunkSize];
            Buffer.BlockCopy(bytes, 0, chunk, 0, chunk.Length * 2);
        }
        Chunks[key] = chunk;
        return chunk;
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

    private static short[] ReadShorts(string path, int count)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var values = new short[count];
        Buffer.BlockCopy(bytes, 0, values, 0, Math.Min(bytes.Length, count * 2));
        return values;
    }

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static void WritePng(string path, int width, int height, byte[] rgb)
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
