using System;

namespace SeaPowerTopo.Render;

/// <summary>
/// The game's two height grids and where their samples sit. Plain C#, shared with the offline preview.
/// <para>DEM: 20° x 20° chunks of 2400 x 2400 int16 metres, one sample every 30 arc seconds; grid point (gx, gy) sits
/// at lon = -180 + gx / 120, lat = 90 - gy / 120 (the stock map tiles' texel centres). World grid
/// (<c>terrain/data/lowres.raw</c>): 7200 x 3600 int16 metres, 3 arc minutes; texel (lx, ly) at
/// lon = -180 + (lx + 0.5) * 0.05, lat = 90 - (ly + 0.5) * 0.05.</para>
/// </summary>
internal static class DemGrid
{
    public const int ChunkSize = 2400, ChunksX = 18, ChunksY = 9;
    public const int DemCols = ChunkSize * ChunksX, DemRows = ChunkSize * ChunksY;
    public const double DemPerDegree = 120.0;
    public const int LowCols = 7200, LowRows = 3600;
    public const double LowCell = 0.05;

    /// <summary>Beyond about 5 DEM samples per pixel the world grid is enough.</summary>
    public const double DemLimitDpp = 0.04;

    /// <summary>The world grid texel north-west of a point and how far (0..1) the point lies towards the next ones.</summary>
    public static void LowCell4(double lon, double lat, out int i, out int j, out float tx, out float ty)
    {
        double u = (lon + 180.0) / LowCell - 0.5, v = (90.0 - lat) / LowCell - 0.5;
        i = (int)Math.Floor(u);
        j = (int)Math.Floor(v);
        tx = (float)(u - i);
        ty = (float)(v - j);
    }

    /// <summary>Bilinear blend of four corners: a b on top, c d below.</summary>
    public static float Bilerp(float a, float b, float c, float d, float tx, float ty)
    {
        float top = a + (b - a) * tx, bottom = c + (d - c) * tx;
        return top + (bottom - top) * ty;
    }
}
