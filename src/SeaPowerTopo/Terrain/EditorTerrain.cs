#if FLATMAP
using System.IO;
using SeaPower;
using UnityEngine;

namespace SeaPowerTopo.Terrain;

/// <summary>
/// On 0.8.7 the mission editor's map has no map scene and the game loads no DEM for it: while it is the map drawn, the
/// mod reads the world grid (<see cref="LowresGrid"/>) and the DEM chunks (<see cref="DemCache"/>) itself, from the
/// terrain folder the game would read.
/// </summary>
internal static class EditorTerrain
{
    /// <summary>The mission editor's map is the one drawn: the mod reads its own heights.</summary>
    public static bool Active;

    // A mod's user/ folder counts as a terrain once it holds this file: the game's own test, character for character
    // (TerrainManager.OnStart and TerrainLoader.checkForAndApplyStampForMap), although the chunks themselves are named
    // without the underscore (Utils.getDEMFileName). Kept as the game has it, so the editor reads the folder the game
    // itself reads.
    private const string UserTerrainMarker = "/user/terrain/data/y1_x01.lz4";

    /// <summary>The terrain folder the game reads its heights from, or null if there is none.</summary>
    public static string Folder()
    {
        string dir = Singleton<TerrainManager>.InstanceExists() ? Singleton<TerrainManager>.Instance.WorldPath : null;
        if (string.IsNullOrEmpty(dir))
            dir = Application.streamingAssetsPath + (File.Exists(Application.streamingAssetsPath + UserTerrainMarker)
                ? "/user/terrain/data/" : "/original/terrain/data/");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>A file of the terrain folder, or null if it is not there.</summary>
    public static string FilePath(string name)
    {
        string dir = Folder();
        if (dir == null)
            return null;
        string file = Path.Combine(dir, name);
        return File.Exists(file) ? file : null;
    }
}
#endif
