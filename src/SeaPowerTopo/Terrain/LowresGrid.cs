using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SeaPower;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SeaPowerTopo.Terrain;

/// <summary>
/// The game's 3 arc-minute world height grid (<c>terrain/data/lowres.raw</c>, 7200 × 3600 int16 metres), read once
/// from the file the game itself uses – a mod's own file where one replaces it. The map keeps its copy on the
/// graphics card only, so the mod reads its own (52 MB) on a worker thread at the first mission, and again only if
/// the file the game resolves changes. Pinned, so <see cref="HeightReader"/> can read it through a pointer.
/// </summary>
internal static unsafe class LowresGrid
{
#if FLATMAP
    /// <summary>The file is read only where it is needed (the mission editor).</summary>
    public static bool Active;
#endif
    private static string path;
    private static Task<short[]> loading;
    private static short[] data;
    private static GCHandle pin;

#if FLATMAP
    /// <summary>
    /// The live map's own copy of the grid on 0.8.7 (the background quads' heightmap texture), where there is a map
    /// scene; it is read through the texture in the frame asked for, never kept. The mission editor has no scene,
    /// so the grid is read from the file the game's own loader reads.
    /// </summary>
    public static Texture2D Scene;

    /// <summary>The grid, or null while it loads (or if the file is missing).</summary>
    public static short* Pointer
    {
        get
        {
            Texture2D t = Scene;
            if (t != null && t.isReadable && t.format == TextureFormat.R16 &&
                t.width == HeightReader.LowCols && t.height == HeightReader.LowRows)
            {
                NativeArray<short> raw = t.GetRawTextureData<short>();
                if (raw.IsCreated && raw.Length >= HeightReader.LowCols * HeightReader.LowRows)
                    return (short*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(raw);
            }
            return data == null ? null : (short*)pin.AddrOfPinnedObject();
        }
    }

    // The terrain folder the game reads its heights from (a mod's user/ folder when it has a terrain).
    private static string TerrainFile(string name)
    {
        string dir = Singleton<TerrainManager>.InstanceExists() ? Singleton<TerrainManager>.Instance.WorldPath : null;
        if (string.IsNullOrEmpty(dir))
            dir = Application.streamingAssetsPath + (File.Exists(Application.streamingAssetsPath + "/user/terrain/data/y1_x01.lz4")
                ? "/user/terrain/data/" : "/original/terrain/data/");
        string file = Path.Combine(dir, name);
        return File.Exists(file) ? file : null;
    }
#else
    /// <summary>The grid, or null while it loads (or if the file is missing).</summary>
    public static short* Pointer => data == null ? null : (short*)pin.AddrOfPinnedObject();

    private static string TerrainFile(string name) => Singleton<FileManager>.InstanceExists() ? Singleton<FileManager>.Instance.Layers?.GetTerrainFile(name) : null;
#endif

    /// <summary>Main thread: starts loading the grid the game uses now, or picks up a finished load.</summary>
    public static void Poll()
    {
        if (loading != null)
        {
            if (!loading.IsCompleted)
                return;
            Task<short[]> done = loading;
            loading = null;
            if (done.IsFaulted || done.Result == null)
            {
                ModLog.Warn($"World height grid {path} not readable ({done.Exception?.GetBaseException().Message ?? "too short"}) - " +
                            "zoomed-out areas without the fine DEM stay uncoloured.");
                return;
            }
            Release();
            data = done.Result;
            pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            ModLog.Info($"World height grid read from {path}");
            return;
        }
#if FLATMAP
        if (!Active || Scene != null)
            return;                                   // a mission's map scene has the grid
#endif
        string now = TerrainFile("lowres.raw");
        if (now == null || string.Equals(now, path, StringComparison.OrdinalIgnoreCase))
            return;
        path = now;
        loading = System.Threading.Tasks.Task.Run(() => Read(now));
    }

    private static short[] Read(string file)
    {
        var grid = new short[HeightReader.LowCols * HeightReader.LowRows];
        Span<byte> bytes = MemoryMarshal.AsBytes(grid.AsSpan());
        using (FileStream stream = File.OpenRead(file))
        {
            int read = 0;
            while (read < bytes.Length)
            {
                int n = stream.Read(bytes.Slice(read));
                if (n == 0)
                    return null;
                read += n;
            }
        }
        return grid;
    }

#if FLATMAP
    /// <summary>The editor's map is left: the file's copy of the grid goes.</summary>
    public static void Clear()
    {
        Active = false;
        loading = null;
        path = null;
        Release();
    }
#endif

    private static void Release()
    {
        if (pin.IsAllocated)
            pin.Free();
        data = null;
    }
}
