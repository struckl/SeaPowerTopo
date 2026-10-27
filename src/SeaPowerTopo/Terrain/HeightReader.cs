using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerTopo.Render;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SeaPowerTopo.Terrain;

/// <summary>A terrain height under one point, as the simulation sees it.</summary>
internal struct HeightSample
{
    public bool Known;
    public bool Water;
    /// <summary>Land: height above sea level. Water: depth (positive). Metres.</summary>
    public float Metres;
    /// <summary>Taken from the 3 arc-minute world grid because the fine DEM isn't loaded there.</summary>
    public bool Coarse;
}

/// <summary>
/// Reads terrain heights straight out of the game's DEM: the 20° × 20° chunks (2400 × 2400 int16 metres, one
/// sample every 30 arc seconds) the 3D terrain has loaded or <see cref="DemCache"/> has read, and the 3 arc-minute
/// world grid (<see cref="LowresGrid"/>) for everywhere else. Same data, same sample positions as the stock map draws.
/// <para>Main thread only, and only within the frame it was created in: Unity may move a texture's CPU copy when
/// the game uploads it (which happens on the main thread), so pointers are never kept across frames. Dispose
/// releases the cache handles that keep the chunks from being evicted meanwhile.</para>
/// </summary>
internal sealed unsafe class HeightReader : IDisposable
{
    public const int ChunkSize = 2400, ChunksX = 18, ChunksY = 9;
    public const int DemCols = ChunkSize * ChunksX, DemRows = ChunkSize * ChunksY;
    public const double DemPerDegree = 120.0;
    public const int LowCols = 7200, LowRows = 3600;
    public const double LowCell = 0.05;

    // DEM grid point (gx, gy) sits at lon = -180 + gx / 120, lat = 90 - gy / 120 (the stock map tiles' texel
    // centres); world grid texel (lx, ly) at lon = -180 + (lx + 0.5) * 0.05, lat = 90 - (ly + 0.5) * 0.05.

    private readonly DEMHandle[] handles = new DEMHandle[ChunksX * ChunksY];
    private readonly short*[] chunks = new short*[ChunksX * ChunksY];
    private readonly byte[] state = new byte[ChunksX * ChunksY];   // 0 = not looked up, 1 = loaded, 2 = missing
    private readonly short* low;
    private readonly HashSet<string> missing;

    /// <summary>The game's terrain height multiplier (Options → World), applied to land.</summary>
    public readonly float Multiplier;
    /// <summary>The game's under-water depth factor (Options → World), applied to water.</summary>
    public readonly float DepthFactor;

    public bool HasLowres => low != null;

    /// <param name="missing">Collects the keys of DEM chunks that were needed but not loaded yet (they are being read).</param>
    public HeightReader(HashSet<string> missing = null)
    {
        this.missing = missing;
        Multiplier = Globals._heightMultiplier > 0f ? Globals._heightMultiplier : 1f;
        DepthFactor = Globals._underWaterDepthFactor > 0f ? Globals._underWaterDepthFactor : 1f;
        low = LowresGrid.Pointer;
    }

    /// <summary>A raw DEM value as the simulation uses it (land scaled by the multiplier, water by the depth factor).</summary>
    public float Sim(float raw) => Raster.SimHeight(raw, Multiplier, DepthFactor);

    /// <summary>Raw DEM metres at a DEM grid point; false if its chunk isn't loaded.</summary>
    public bool TryDem(int gx, int gy, out short raw)
    {
        gx %= DemCols;
        if (gx < 0) gx += DemCols;
        if (gy < 0) gy = 0;
        else if (gy >= DemRows) gy = DemRows - 1;
        int cx = gx / ChunkSize, cy = gy / ChunkSize, c = cy * ChunksX + cx;
        if (state[c] == 0)
            Open(c, cx, cy);
        short* p = chunks[c];
        if (p == null)
        {
            raw = 0;
            return false;
        }
        raw = p[(gy - cy * ChunkSize) * ChunkSize + (gx - cx * ChunkSize)];
        return true;
    }

    /// <summary>Raw metres of a world grid texel (0 without the grid).</summary>
    public short Low(int lx, int ly)
    {
        if (low == null)
            return 0;
        lx %= LowCols;
        if (lx < 0) lx += LowCols;
        if (ly < 0) ly = 0;
        else if (ly >= LowRows) ly = LowRows - 1;
        return low[ly * LowCols + lx];
    }

    /// <summary>Raw metres from the world grid, bilinear; -1 (water) without the grid.</summary>
    public float LowAt(double lon, double lat)
    {
        if (low == null)
            return -1f;
        double u = (lon + 180.0) / LowCell - 0.5, v = (90.0 - lat) / LowCell - 0.5;
        int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
        float tx = (float)(u - i), ty = (float)(v - j);
        float top = Low(i, j) + (Low(i + 1, j) - Low(i, j)) * tx;
        float bottom = Low(i, j + 1) + (Low(i + 1, j + 1) - Low(i, j + 1)) * tx;
        return top + (bottom - top) * ty;
    }

    /// <summary>Terrain under a point for the cursor readout: fine DEM where loaded, the world grid elsewhere.</summary>
    public HeightSample Sample(double lon, double lat)
    {
        double u = (lon + 180.0) * DemPerDegree, v = (90.0 - lat) * DemPerDegree;
        int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
        float tx = (float)(u - i), ty = (float)(v - j);
        if (TryDem(i, j, out short a) && TryDem(i + 1, j, out short b) &&
            TryDem(i, j + 1, out short c) && TryDem(i + 1, j + 1, out short d))
        {
            return Classify(a, b, c, d, tx, ty, coarse: false);
        }
        if (low == null)
            return default;
        u = (lon + 180.0) / LowCell - 0.5;
        v = (90.0 - lat) / LowCell - 0.5;
        i = (int)Math.Floor(u);
        j = (int)Math.Floor(v);
        return Classify(Low(i, j), Low(i + 1, j), Low(i, j + 1), Low(i + 1, j + 1),
                        (float)(u - i), (float)(v - j), coarse: true);
    }

    // Land or water like the stock map shader decides it (half-way between a land and a water sample), then the
    // height of the land part or the depth of the water part, from the corners as the simulation sees them – the
    // same values the overlay is drawn from, so the readout and the colour under the pointer agree.
    private HeightSample Classify(float a, float b, float c, float d, float tx, float ty, bool coarse)
    {
        float land = Bilerp(a >= 0 ? 1 : 0, b >= 0 ? 1 : 0, c >= 0 ? 1 : 0, d >= 0 ? 1 : 0, tx, ty);
        var s = new HeightSample { Known = true, Coarse = coarse };
        if (land >= 0.5f)
        {
            s.Metres = Sim(Bilerp(Math.Max(a, 0), Math.Max(b, 0), Math.Max(c, 0), Math.Max(d, 0), tx, ty));
            return s;
        }
        s.Water = true;
        s.Metres = -Bilerp(Sim(Math.Min(a, 0)), Sim(Math.Min(b, 0)), Sim(Math.Min(c, 0)), Sim(Math.Min(d, 0)), tx, ty);
        return s;
    }

    private void Open(int c, int cx, int cy)
    {
        state[c] = 2;
        string key = Utils.getDEMFileName(cx + 1, cy + 1, withExtension: false);
        try
        {
            DEMHandle handle = TerrainLoader.TryGetCached(key);
            if (handle != null)
            {
                NativeArray<byte> data = handle.Chunk._data;
                if (data.IsCreated && data.Length >= ChunkSize * ChunkSize * 2)
                {
                    handles[c] = handle;
                    chunks[c] = (short*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
                    state[c] = 1;
                    return;
                }
                handle.Dispose();
            }
            if (DemCache.TryGet(key, out NativeArray<byte> own) && own.Length >= ChunkSize * ChunkSize * 2)
            {
                chunks[c] = (short*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(own);
                state[c] = 1;
                return;
            }
        }
        catch (Exception ex)
        {
            // A chunk on its way out of the cache refuses new handles.
            ModLog.Debug($"DEM chunk {key} not readable: {ex.Message}");
        }
        missing?.Add(key);
    }

    private static float Bilerp(float a, float b, float c, float d, float tx, float ty)
    {
        float top = a + (b - a) * tx, bottom = c + (d - c) * tx;
        return top + (bottom - top) * ty;
    }

    public void Dispose()
    {
        for (int c = 0; c < handles.Length; c++)
        {
            if (handles[c] != null)
            {
                handles[c].Dispose();
                handles[c] = null;
            }
            chunks[c] = null;
            state[c] = 0;
        }
    }
}
