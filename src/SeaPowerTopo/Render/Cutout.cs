using System.Collections.Generic;

namespace SeaPowerTopo.Render;

/// <summary>A rectangle in unit-square coordinates (0..1 across the image, y up).</summary>
internal readonly struct UnitRect
{
    public readonly float X0, Y0, X1, Y1;

    public UnitRect(float x0, float y0, float x1, float y1)
    {
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
    }

    public override string ToString() => $"({X0:0.###},{Y0:0.###})-({X1:0.###},{Y1:0.###})";
}

/// <summary>
/// The unit square minus some holes, as a few rectangles that don't overlap: the hole edges split the square into a
/// grid, the cells outside every hole stay, neighbours in a row merge. Plain C#, so it can be checked offline.
/// </summary>
internal static class Cutout
{
    public static List<UnitRect> Rects(IReadOnlyList<UnitRect> holes)
    {
        var xs = new List<float> { 0f, 1f };
        var ys = new List<float> { 0f, 1f };
        var cut = new List<UnitRect>();
        foreach (UnitRect h in holes)
        {
            float x0 = Clamp01(h.X0), x1 = Clamp01(h.X1), y0 = Clamp01(h.Y0), y1 = Clamp01(h.Y1);
            if (x1 <= x0 || y1 <= y0)
                continue;
            cut.Add(new UnitRect(x0, y0, x1, y1));
            xs.Add(x0);
            xs.Add(x1);
            ys.Add(y0);
            ys.Add(y1);
        }
        xs.Sort();
        ys.Sort();

        var rects = new List<UnitRect>();
        for (int j = 0; j + 1 < ys.Count; j++)
        {
            float y0 = ys[j], y1 = ys[j + 1];
            if (y1 - y0 < 1e-6f)
                continue;
            float runStart = -1f, runEnd = -1f;
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                float x0 = xs[i], x1 = xs[i + 1];
                if (x1 - x0 < 1e-6f)
                    continue;
                if (!InHole(cut, (x0 + x1) * 0.5f, (y0 + y1) * 0.5f))
                {
                    if (runStart < 0f)
                        runStart = x0;
                    runEnd = x1;
                    continue;
                }
                if (runStart >= 0f)
                    rects.Add(new UnitRect(runStart, y0, runEnd, y1));
                runStart = -1f;
            }
            if (runStart >= 0f)
                rects.Add(new UnitRect(runStart, y0, runEnd, y1));
        }
        return rects;
    }

    private static bool InHole(List<UnitRect> cut, float x, float y)
    {
        foreach (UnitRect h in cut)
        {
            if (x > h.X0 && x < h.X1 && y > h.Y0 && y < h.Y1)
                return true;
        }
        return false;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}
