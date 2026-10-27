using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaPowerTopo.Render;

/// <summary>The part of the globe the map camera sees: its centre (a unit vector in the map scene) and radius.</summary>
internal readonly struct GlobeCap
{
    public readonly Vector3 Centre;
    /// <summary>Degrees from the centre to the farthest point in view (90 once the globe's edge shows).</summary>
    public readonly double Radius;

    public GlobeCap(Vector3 centre, double radius)
    {
        Centre = centre;
        Radius = radius;
    }
}

/// <summary>
/// Meshes an overlay image – a lon/lat rectangle, minus the rectangles of the layers in front – onto the stock map's
/// globe. The stock map is the unit sphere cut into 1° cells (<c>MapUtils.MakeSphericalQuad</c>), each drawn as two
/// flat triangles split along its north-west to south-east diagonal, with texture coordinates linear in lon/lat.
/// Every piece here lies in the plane of the stock triangle it covers and is textured the same way, so the overlay
/// sits exactly on the stock map at any zoom. Only cells within the camera's view (plus a margin) are meshed: the map
/// texture has no depth buffer, so the far side of the globe must not be drawn at all.
/// </summary>
internal static class GlobeMesh
{
    private const double Deg = Math.PI / 180.0;

    /// <summary>
    /// The mesh of <paramref name="parts"/> (lon/lat rectangles inside <paramref name="image"/>, longitudes in the
    /// image's own range): the cells whose centre lies within <paramref name="radius"/> degrees of
    /// <paramref name="centre"/> (at most 90: the far side of the globe never).
    /// </summary>
    public static void Build(GeoRect image, List<GeoRect> parts, Vector3 centre, double radius,
                             List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        vertices.Clear();
        uvs.Clear();
        triangles.Clear();
        double minDot = Math.Cos(Math.Min(90.0, radius) * Deg);
        var poly = new double[16];
        var clipped = new double[16];
        foreach (GeoRect part in parts)
        {
            int i0 = (int)Math.Floor(part.West), i1 = (int)Math.Ceiling(part.East);
            int j0 = (int)Math.Floor(part.South), j1 = (int)Math.Ceiling(part.North);
            // The stock formula's sines and cosines, once per grid line.
            var lonCos = new float[i1 - i0 + 1];
            var lonSin = new float[i1 - i0 + 1];
            var latCos = new float[j1 - j0 + 1];
            var latSin = new float[j1 - j0 + 1];
            for (int i = i0; i <= i1; i++)
            {
                float lo = i * 0.0174532924f;
                lonCos[i - i0] = Mathf.Cos(lo);
                lonSin[i - i0] = Mathf.Sin(lo);
            }
            for (int j = j0; j <= j1; j++)
            {
                float la = j * 0.0174532924f;
                latCos[j - j0] = Mathf.Cos(la);
                latSin[j - j0] = Mathf.Sin(la);
            }
            Vector3 Grid(int j, int i) => new Vector3(latCos[j - j0] * lonCos[i - i0], latSin[j - j0], latCos[j - j0] * lonSin[i - i0]);

            for (int j = j0; j < j1; j++)
            {
                double y0 = Math.Max(0.0, part.South - j), y1 = Math.Min(1.0, part.North - j);
                if (y1 <= y0)
                    continue;
                for (int i = i0; i < i1; i++)
                {
                    double x0 = Math.Max(0.0, part.West - i), x1 = Math.Min(1.0, part.East - i);
                    if (x1 <= x0)
                        continue;
                    Vector3 sw = Grid(j, i), se = Grid(j, i + 1), nw = Grid(j + 1, i), ne = Grid(j + 1, i + 1);
                    // The cell's centre (the middle of its diagonal, a hair inside the sphere) in range.
                    if (Dot(nw + se, centre) < 2.0 * minDot)
                        continue;
                    if (x0 == 0.0 && y0 == 0.0 && x1 == 1.0 && y1 == 1.0)
                    {
                        // A whole cell: the stock map's own two triangles.
                        int b = vertices.Count;
                        AddVertex(vertices, uvs, image, sw, i, j);
                        AddVertex(vertices, uvs, image, se, i + 1, j);
                        AddVertex(vertices, uvs, image, nw, i, j + 1);
                        AddVertex(vertices, uvs, image, ne, i + 1, j + 1);
                        triangles.Add(b);
                        triangles.Add(b + 2);
                        triangles.Add(b + 1);
                        triangles.Add(b + 2);
                        triangles.Add(b + 3);
                        triangles.Add(b + 1);
                        continue;
                    }
                    // Part of a cell: the rectangle cut by the diagonal, each half in its own triangle's plane.
                    for (int half = 0; half < 2; half++)
                    {
                        int n = Rect(poly, x0, y0, x1, y1);
                        n = ClipDiagonal(poly, n, clipped, below: half == 0);
                        if (n < 3)
                            continue;
                        int b = vertices.Count;
                        for (int k = 0; k < n; k++)
                        {
                            double x = clipped[2 * k], y = clipped[2 * k + 1];
                            // South-west half: sw + x (se - sw) + y (nw - sw); north-east half: ne + (1 - x) (nw - ne) + (1 - y) (se - ne).
                            Vector3 p = half == 0
                                ? sw + (float)x * (se - sw) + (float)y * (nw - sw)
                                : ne + (float)(1.0 - x) * (nw - ne) + (float)(1.0 - y) * (se - ne);
                            AddVertex(vertices, uvs, image, p, i + x, j + y);
                        }
                        for (int k = 1; k + 1 < n; k++)
                        {
                            triangles.Add(b);
                            triangles.Add(b + k + 1);
                            triangles.Add(b + k);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Latitude and longitude (degrees, longitude -180..180) of a direction in the map scene.</summary>
    public static void ToGeo(Vector3 v, out double lat, out double lon)
    {
        double len = Math.Sqrt((double)v.x * v.x + (double)v.y * v.y + (double)v.z * v.z);
        lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, v.y / len))) / Deg;
        lon = Math.Atan2(v.z, v.x) / Deg;
    }

    private static double Dot(Vector3 a, Vector3 b) => (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;

    private static void AddVertex(List<Vector3> vertices, List<Vector2> uvs, GeoRect image, Vector3 p, double lon, double lat)
    {
        vertices.Add(p);
        // Image row 0 is the north edge, and a texture's first row is v = 0, so v runs north to south.
        uvs.Add(new Vector2((float)((lon - image.West) / image.Width), (float)((image.North - lat) / image.Height)));
    }

    private static int Rect(double[] p, double x0, double y0, double x1, double y1)
    {
        p[0] = x0; p[1] = y0;
        p[2] = x1; p[3] = y0;
        p[4] = x1; p[5] = y1;
        p[6] = x0; p[7] = y1;
        return 4;
    }

    // Sutherland-Hodgman against the cell's diagonal x + y = 1: the south-west half (below) or the north-east one.
    private static int ClipDiagonal(double[] input, int n, double[] output, bool below)
    {
        int m = 0;
        for (int k = 0; k < n; k++)
        {
            double ax = input[2 * k], ay = input[2 * k + 1];
            int next = (k + 1) % n;
            double bx = input[2 * next], by = input[2 * next + 1];
            double da = below ? 1.0 - ax - ay : ax + ay - 1.0, db = below ? 1.0 - bx - by : bx + by - 1.0;
            if (da >= 0.0)
            {
                output[2 * m] = ax;
                output[2 * m + 1] = ay;
                m++;
            }
            if (da >= 0.0 != db >= 0.0)
            {
                double t = da / (da - db);
                output[2 * m] = ax + (bx - ax) * t;
                output[2 * m + 1] = ay + (by - ay) * t;
                m++;
            }
        }
        // Drop points that coincide (a corner on the diagonal), so slivers don't become zero-area triangles.
        int w = 0;
        for (int k = 0; k < m; k++)
        {
            if (w > 0 && Math.Abs(output[2 * k] - output[2 * (w - 1)]) < 1e-12 && Math.Abs(output[2 * k + 1] - output[2 * (w - 1) + 1]) < 1e-12)
                continue;
            output[2 * w] = output[2 * k];
            output[2 * w + 1] = output[2 * k + 1];
            w++;
        }
        if (w > 1 && Math.Abs(output[0] - output[2 * (w - 1)]) < 1e-12 && Math.Abs(output[1] - output[2 * (w - 1) + 1]) < 1e-12)
            w--;
        return w;
    }
}
