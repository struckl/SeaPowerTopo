using System;
using System.Collections.Generic;
using HarmonyLib;
using SeaPower;
using SeaPowerTopo.Render;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SeaPowerTopo.Terrain;

/// <summary>
/// The game's thermal layer (<c>Environment.getLayerHeight</c>): the world is cut into 0.2° tiles; a tile whose
/// water is deeper than 500 ft has a layer 250–450 ft deep, drawn for the tile from a random number seeded by its
/// coordinates; a mission can switch the layer off. The game settles a tile the first time anything asks about it
/// (a sonar, the depth shown for the selected unit) – those tiles are read from its cache. The others are worked out
/// the same way here, from the tile's centre, without touching the game's state (the random generator is put back).
/// Main thread only.
/// </summary>
internal static class LayerMap
{
    private const float MetresPerUnit = 67.200066f, FeetToMetres = 0.3048f;
    private const float MinSeaFeet = 500f;

    private static readonly AccessTools.FieldRef<SeaPower.Environment, Dictionary<Vector2Int, float>> GameCache =
        AccessTools.FieldRefAccess<SeaPower.Environment, Dictionary<Vector2Int, float>>("_layerDepths");
    private static readonly Dictionary<int, float> FeetBySeed = new Dictionary<int, float>();

    /// <summary>The mission has no thermal layer at all.</summary>
    public static bool Disabled =>
        Singleton<SeaPower.Environment>.InstanceExists() && Singleton<SeaPower.Environment>.Instance._layerDisabled;

    /// <summary>
    /// Layer depth of a tile in metres, NaN where there is none.
    /// </summary>
    /// <param name="seaDepthAt">Water depth in metres the simulation uses at a position (≤ 0 on land).</param>
    public static float Depth(int tileX, int tileY, Func<double, double, float> seaDepthAt)
    {
        if (Singleton<SeaPower.Environment>.InstanceExists())
        {
            SeaPower.Environment environment = Singleton<SeaPower.Environment>.Instance;
            if (environment._layerDisabled)
                return float.NaN;
            Dictionary<Vector2Int, float> cache = GameCache(environment);
            if (cache != null && cache.TryGetValue(new Vector2Int(tileX, tileY), out float height))
                return height < 0f ? -height * MetresPerUnit : float.NaN;
        }
        Raster.LayerTileCentre(tileX, tileY, out double lon, out double lat);
        if (!(seaDepthAt(lon, lat) / FeetToMetres > MinSeaFeet))
            return float.NaN;
        int seed = tileX * tileX + tileY * tileY;
        if (!FeetBySeed.TryGetValue(seed, out float feet))
        {
            Random.State saved = Random.state;
            Random.InitState(seed);
            feet = Random.Range(250f, 450f);
            Random.state = saved;
            FeetBySeed[seed] = feet;
        }
        return feet * FeetToMetres;
    }
}
