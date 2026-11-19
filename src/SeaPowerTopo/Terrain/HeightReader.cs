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
/// Reads terrain heights straight out of the game's DEM (<see cref="DemGrid"/>): the 20° chunks <see cref="DemCache"/>
/// has read (on 0.8.7 also the ones the 3D terrain has loaded), and the 3 arc-minute world grid (<see cref="LowresGrid"/>)
/// for everywhere else. Same data, same sample positions as the stock map draws.
/// <para>Main thread only, and only within the frame it was created in: Unity may move a texture's CPU copy when
/// the game uploads it (which happens on the main thread), so pointers are never kept across frames.</para>
/// </summary>
internal sealed unsafe class HeightReader : IDisposable
{
    private const int ChunkSize = DemGrid.ChunkSize, ChunksX = DemGrid.ChunksX, ChunksY = DemGrid.ChunksY;
    private const int DemCols = DemGrid.DemCols, DemRows = DemGrid.DemRows;
    private const int LowCols = DemGrid.LowCols, LowRows = DemGrid.LowRows;

#if FLATMAP
    // The game's cache handles on the chunks the 3D terrain has loaded, which keep them from being evicted meanwhile.
    private readonly DEMHandle[] handles = new DEMHandle[ChunksX * ChunksY];
#endif
    private readonly short*[] chunks = new short*[ChunksX * ChunksY];
    private readonly byte[] state = new byte[ChunksX * ChunksY];   // 0 = not looked up, 1 = loaded, 2 = missing
    private readonly short* low;
    private readonly HashSet<string> missing;

    /// <summary>The game's terrain height multiplier (Options -> World), applied to land.</summary>
    public readonly float Multiplier;
    /// <summary>The game's under-water depth factor (Options -> World), applied to water.</summary>
    public readonly float DepthFactor;

    public bool HasLowres => low != null;

    // Options in 0.8.7; beta 261107 removed them and fixed land at x1 and water at x2 (TerrainUtils.ScaleDEMHeight). By name, so one build runs on both.
    private static readonly System.Reflection.FieldInfo HeightMultiplierField = typeof(Globals).GetField("_heightMultiplier");
    private static readonly System.Reflection.FieldInfo DepthFactorField = typeof(Globals).GetField("_underWaterDepthFactor");

    private static float Option(System.Reflection.FieldInfo field, float fixedValue)
    {
        if (field == null) return fixedValue;
        float value = (float)field.GetValue(null);
        return value > 0f ? value : 1f;
    }

    private readonly bool load;

    /// <param name="missing">Collects the keys of DEM chunks that were needed but not loaded yet (they are being read).</param>
    /// <param name="load">Start reading chunks that are not read yet (the overlay); the cursor readout only uses what is there.</param>
    public HeightReader(HashSet<string> missing = null, bool load = true)
    {
        this.missing = missing;
        this.load = load;
        Multiplier = Option(HeightMultiplierField, 1f);
        DepthFactor = Option(DepthFactorField, 2f);
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

    /// <summary>Raw DEM metres at a DEM grid point, from the world grid where its chunk isn't loaded.</summary>
    public float Dem(int gx, int gy) =>
        TryDem(gx, gy, out short raw) ? raw : LowAt(-180.0 + gx / DemGrid.DemPerDegree, 90.0 - gy / DemGrid.DemPerDegree);

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
        DemGrid.LowCell4(lon, lat, out int i, out int j, out float tx, out float ty);
        return DemGrid.Bilerp(Low(i, j), Low(i + 1, j), Low(i, j + 1), Low(i + 1, j + 1), tx, ty);
    }

    /// <summary>Terrain under a point for the cursor readout: fine DEM where loaded, the world grid elsewhere.</summary>
    public HeightSample Sample(double lon, double lat)
    {
        double u = (lon + 180.0) * DemGrid.DemPerDegree, v = (90.0 - lat) * DemGrid.DemPerDegree;
        int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
        float tx = (float)(u - i), ty = (float)(v - j);
        if (TryDem(i, j, out short a) && TryDem(i + 1, j, out short b) &&
            TryDem(i, j + 1, out short c) && TryDem(i + 1, j + 1, out short d))
        {
            return Classify(a, b, c, d, tx, ty, coarse: false);
        }
        if (low == null)
            return default;
        DemGrid.LowCell4(lon, lat, out i, out j, out tx, out ty);
        return Classify(Low(i, j), Low(i + 1, j), Low(i, j + 1), Low(i + 1, j + 1), tx, ty, coarse: true);
    }

    // Land or water like the stock map shader decides it (half-way between a land and a water sample), then the
    // height of the land part or the depth of the water part, from the corners as the simulation sees them - the
    // same values the overlay is drawn from, so the readout and the colour under the pointer agree.
    private HeightSample Classify(float a, float b, float c, float d, float tx, float ty, bool coarse)
    {
        float land = DemGrid.Bilerp(a >= 0 ? 1 : 0, b >= 0 ? 1 : 0, c >= 0 ? 1 : 0, d >= 0 ? 1 : 0, tx, ty);
        var s = new HeightSample { Known = true, Coarse = coarse };
        if (land >= 0.5f)
        {
            s.Metres = Sim(DemGrid.Bilerp(Math.Max(a, 0), Math.Max(b, 0), Math.Max(c, 0), Math.Max(d, 0), tx, ty));
            return s;
        }
        s.Water = true;
        s.Metres = -DemGrid.Bilerp(Sim(Math.Min(a, 0)), Sim(Math.Min(b, 0)), Sim(Math.Min(c, 0)), Sim(Math.Min(d, 0)), tx, ty);
        return s;
    }

    private void Open(int c, int cx, int cy)
    {
        state[c] = 2;
        string key = Utils.getDEMFileName(cx + 1, cy + 1, withExtension: false);
        try
        {
#if FLATMAP
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
#endif
            // Globe build: never a handle on the game's own chunks. The game keeps 16 of them; a handle held by the mod
            // keeps the game from preloading the area around the units ("DEM pool exhausted: skipping ... to preload")
            // and clears the "just preloaded" mark that keeps a chunk from being evicted - the 3D terrain was then
            // built without heights (land turned to sea, units floated, ships ran ashore). Own copies instead.
            if (DemCache.TryGet(key, out NativeArray<byte> own, load) && own.Length >= ChunkSize * ChunkSize * 2)
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
        if (load)
            missing?.Add(key);
    }

    public void Dispose()
    {
        for (int c = 0; c < chunks.Length; c++)
        {
#if FLATMAP
            handles[c]?.Dispose();
            handles[c] = null;
#endif
            chunks[c] = null;
            state[c] = 0;
        }
    }
}
