using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using SeaPower;

namespace SeaPowerTopo.Terrain;

/// <summary>
/// 20-degree DEM chunks the mod reads itself, on a worker thread, keeping the last few. Main thread only, apart from the reads.
/// Every chunk the overlay draws from. The mod never takes a handle on the game's own chunks (the game keeps only 16, and
/// handles held by a mod starved the 3D terrain of its preloads); the game's own reader (<c>TerrainLoader.ReadDEMHeights</c>,
/// the files a mod may replace included) reads them here instead.
/// </summary>
internal static class DemCache
{
    private const int MaxChunks = 8;                  // 11.5 MB each

    // A chunk that could not be read stays here without heights, so it is not asked for again until Clear.
    private sealed class Entry
    {
        public Task<NativeArray<byte>> Load;
        public NativeArray<byte> Heights;
        public int LastUse;
    }

    private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

    /// <summary>The chunk's heights if read, else starts reading it and returns false.</summary>
    public static bool TryGet(string key, out NativeArray<byte> heights, bool load = true)
    {
        heights = default;
        if (!entries.TryGetValue(key, out Entry e))
        {
            Task<NativeArray<byte>> read = load ? StartRead(key) : null;
            if (read == null)
                return false;
            entries[key] = e = new Entry { Load = read };
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
                Rig.Log.Debug($"DEM chunk {kv.Key} not readable: {e.Load.Exception?.GetBaseException().Message}");
            else
                e.Heights = e.Load.Result;
            e.Load = null;
        }
        while (Loaded() > MaxChunks)
        {
            if (!DropLeastUsed())
                break;
        }
    }

    private static bool DropLeastUsed()
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
            return false;
        entries[oldest].Heights.Dispose();
        entries.Remove(oldest);
        return true;
    }

    /// <summary>Mission over or editor map left: frees every chunk (reads still running free theirs when they finish).</summary>
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

    private static Task<NativeArray<byte>> StartRead(string key)
    {
        if (!Singleton<FileManager>.InstanceExists())
            return null;
        // Captured as object and cast inside the task, so the closure class carries no field of the game's FileLayerSnapshot type.
        object layers = Singleton<FileManager>.Instance.Layers;
        return System.Threading.Tasks.Task.Run(() => Read(key, (FileLayerSnapshot)layers));
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
