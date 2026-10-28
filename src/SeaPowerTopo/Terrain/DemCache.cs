using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SeaPower;
using Unity.Collections;
using UnityEngine;

namespace SeaPowerTopo.Terrain;

/// <summary>
/// 20° DEM chunks the 3D terrain hasn't loaded. Since the globe map the stock map reads its chunks straight to the
/// graphics card, so outside the 3D terrain's area the game holds no copy the mod could read. Those chunks are read
/// here with the game's own reader (<c>TerrainLoader.ReadDEMHeights</c>, the files a mod may replace included) on a
/// worker thread, and the last few kept. Main thread only, apart from the reads.
/// </summary>
internal static class DemCache
{
    private const int MaxChunks = 6;                  // 11.5 MB each

    private sealed class Entry
    {
        public Task<NativeArray<byte>> Load;
        public NativeArray<byte> Heights;
        public int LastUse;
        public bool Failed;                           // not asked for again this mission
    }

    private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

    /// <summary>The chunk's heights if read, else starts reading it and returns false.</summary>
    public static bool TryGet(string key, out NativeArray<byte> heights)
    {
        heights = default;
        if (!entries.TryGetValue(key, out Entry e))
        {
            if (!Singleton<FileManager>.InstanceExists())
                return false;
            // Captured as object: a closure field of type FileLayerSnapshot (not in 0.8.7) would make the closure class
            // unloadable there and ECS would fail to start, although this globe DLL stays off (see GameApi.PaletteDark).
            object layers = Singleton<FileManager>.Instance.Layers;
            entries[key] = e = new Entry { Load = System.Threading.Tasks.Task.Run(() => Read(key, (FileLayerSnapshot)layers)) };
        }
        e.LastUse = Time.frameCount;
        if (!e.Heights.IsCreated)
            return false;
        heights = e.Heights;
        return true;
    }

    /// <summary>True once a chunk asked for has been read.</summary>
    public static bool Has(string key) => entries.TryGetValue(key, out Entry e) && e.Heights.IsCreated;

    /// <summary>Start of a frame: takes finished reads, drops the least used chunks beyond the budget.</summary>
    public static void Poll()
    {
        foreach (KeyValuePair<string, Entry> kv in entries)
        {
            Entry e = kv.Value;
            if (e.Load == null || !e.Load.IsCompleted)
                continue;
            if (e.Load.IsFaulted || !e.Load.Result.IsCreated)
            {
                ModLog.Debug($"DEM chunk {kv.Key} not readable: {e.Load.Exception?.GetBaseException().Message}");
                e.Failed = true;
            }
            else
            {
                e.Heights = e.Load.Result;
            }
            e.Load = null;
        }

        while (Loaded() > MaxChunks)
        {
            string oldest = null;
            int use = int.MaxValue;
            foreach (KeyValuePair<string, Entry> kv in entries)
            {
                if (kv.Value.Heights.IsCreated && kv.Value.LastUse < use)
                {
                    use = kv.Value.LastUse;
                    oldest = kv.Key;
                }
            }
            if (oldest == null)
                break;
            entries[oldest].Heights.Dispose();
            entries.Remove(oldest);
        }
    }

    /// <summary>Mission over: frees every chunk (reads still running free theirs when they finish).</summary>
    public static void Clear()
    {
        foreach (Entry e in entries.Values)
        {
            if (e.Heights.IsCreated)
                e.Heights.Dispose();
            else
                e.Load?.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion && t.Result.IsCreated) t.Result.Dispose(); });
        }
        entries.Clear();
    }

    private static int Loaded()
    {
        int n = 0;
        foreach (Entry e in entries.Values)
        {
            if (e.Heights.IsCreated)
                n++;
        }
        return n;
    }

    private static NativeArray<byte> Read(string key, FileLayerSnapshot layers)
    {
        if (!TerrainLoader.ReadDEMHeights(key, layers, out NativeArray<byte> heights, out NativeArray<byte> water))
            return default;
        if (water.IsCreated)
            water.Dispose();
        return heights;
    }
}
