using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SeaPowerTopo.Render;
using SeaPower;

namespace SeaPowerTopo.Terrain;

/// <summary>
/// The game's 3 arc-minute world height grid (<c>terrain/data/lowres.raw</c>, 7200 × 3600 int16 metres), read once
/// from the file the game itself uses - a mod's own file where one replaces it. The map keeps its copy on the
/// graphics card only, so the mod reads its own (52 MB) on a worker thread at the first mission, and again only if
/// the file the game resolves changes. Pinned, so <see cref="HeightReader"/> can read it through a pointer.
/// </summary>
internal static unsafe class LowresGrid
{
    private static string path;
    private static Task<short[]> loading;
    private static short[] data;
    private static GCHandle pin;

    /// <summary>The grid, or null while it loads (or if the file is missing).</summary>
    public static short* Pointer => data == null ? null : (short*)pin.AddrOfPinnedObject();

    private static string TerrainFile(string name) => Singleton<FileManager>.InstanceExists() ? Singleton<FileManager>.Instance.Layers?.GetTerrainFile(name) : null;

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
                Rig.Log.Warn($"World height grid {path} not readable ({done.Exception?.GetBaseException().Message ?? "too short"}) - " +
                            "zoomed-out areas without the fine DEM stay uncoloured.");
                return;
            }
            Release();
            data = done.Result;
            pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            Rig.Log.Info($"World height grid read from {path}");
            return;
        }
        string now = TerrainFile("lowres.raw");
        if (now == null || string.Equals(now, path, StringComparison.OrdinalIgnoreCase))
            return;
        path = now;
        loading = System.Threading.Tasks.Task.Run(() => Read(now));
    }

    private static short[] Read(string file)
    {
        var grid = new short[DemGrid.LowCols * DemGrid.LowRows];
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

    private static void Release()
    {
        if (pin.IsAllocated)
            pin.Free();
        data = null;
    }
}
