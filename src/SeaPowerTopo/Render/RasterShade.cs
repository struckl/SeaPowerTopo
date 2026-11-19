using System;
using System.Collections.Generic;

namespace SeaPowerTopo.Render;

// The colouring half of Raster: resampled heights to RGBA, one pixel at a time on the worker threads.
internal static partial class Raster
{
    private static readonly float[] ChartDepths = { 2, 5, 10, 20, 30, 50, 100, 200, 500, 1000, 2000, 3000, 4000, 5000, 6000 };

    // The terrain-warning reds: the hatching and the solid edge line, for terrain above the aircraft and for water
    // shallower than the submarine.
    private static readonly int[] DangerHatch = { 232, 36, 28 }, DangerEdge = { 205, 18, 18 };

    /// <summary>The depth line a sea theme draws in its shelf colour.</summary>
    private const float ShelfDepth = 200f;

    /// <summary>
    /// Colours the resampled heights into <see cref="RasterJob.Rgba"/>. Water is tinted by depth, or stays
    /// transparent without a sea palette.
    /// </summary>
    public static bool Shade(RasterJob job, Func<bool> cancelled)
    {
        int w = job.Width, ht = job.Height;
        if (job.Rgba == null || job.Rgba.Length != w * ht * 4)
            job.Rgba = new byte[w * ht * 4];
        var run = new ShadeRun(job);
        return Rows(ht, cancelled, (from, to) => ShadeRows(run, from, to));
    }

    // What one Shade works out once for all its pixels: the theme's strengths at the player's settings, the contour
    // colours, a chart's depths and the thermal layer's tile columns.
    private sealed class ShadeRun
    {
        public readonly RasterJob Job;
        public readonly Theme Theme;
        public readonly float LandShading, SeaShading;
        public readonly float Minor, Major;
        public readonly bool Contours, Hatch, Submarine;
        public readonly float My, MxEquator, Z;
        public readonly int[] Line;
        public readonly float MinorAlpha, MajorAlpha, HaloAlpha;

        // A chart: the edges of its four water shades, and every contour depth with the safety contour among them.
        public float ChartShallow, ChartSafety, ChartDeep;
        public float[] ChartLevels;
        public int ChartSafetyLevel = -1;

        // The thermal layer: the tile of every image column (from column -1), and for the soft edges the first of the
        // two tile centres a column lies between and how far (0..1) it is towards the second.
        public int[] LayerCols, LayerI0;
        public float[] LayerFx;

        public ShadeRun(RasterJob job)
        {
            Job = job;
            Theme = job.Palette.Theme;
            LandShading = Look.LandShading(Theme, job.Hillshade);
            SeaShading = Look.SeaShading(Theme, job.Hillshade);
            Minor = job.Interval;
            Major = job.Interval * 5f;
            Contours = Minor > 0f;
            Hatch = !float.IsNaN(job.Danger);
            Submarine = job.Layer != null || !float.IsNaN(job.Keel);
            My = (float)(job.Dpp * MetresPerDegree);
            MxEquator = (float)(job.DppLon * MetresPerDegree);
            Z = Exaggeration(My);
            if (!Theme.Legacy && Theme.LandLine != null)
            {
                Line = Theme.LandLine;
                MinorAlpha = Theme.LandMinor;
                MajorAlpha = Theme.LandMajor;
                HaloAlpha = Theme.LandHalo;
            }
            else
            {
                Line = job.Palette.Dark ? DefaultLineDark : DefaultLineLight;
                MinorAlpha = job.Palette.Dark ? 0.3f : 0.45f;
                MajorAlpha = job.Palette.Dark ? 0.6f : 0.8f;
            }
            if (Theme.IsChart)
                PrepareChart();
            if (job.Layer != null)
                PrepareLayerColumns();
        }

        // The contours of the first release, and of themes without their own colour: dark brown by day, light at night.
        private static readonly int[] DefaultLineLight = { 72, 46, 26 }, DefaultLineDark = { 178, 168, 148 };

        private void PrepareChart()
        {
            Look.ChartDepths(Job.ChartSafety, out ChartShallow, out ChartSafety, out ChartDeep);
            var set = new SortedSet<float>(ChartDepths) { ChartShallow, ChartSafety, ChartDeep };
            var levels = new List<float>();
            foreach (float v in set)
            {
                // Neighbours closer than a tenth of a metre are one contour (a safety contour of 30.04 m is 30 m).
                if (levels.Count == 0 || v - levels[levels.Count - 1] > 0.1f)
                    levels.Add(v);
                if (v == ChartSafety)
                    ChartSafetyLevel = levels.Count - 1;
            }
            ChartLevels = levels.ToArray();
        }

        private void PrepareLayerColumns()
        {
            int w = Job.Width;
            LayerCols = new int[w + 1];
            for (int x = -1; x < w; x++)
                LayerCols[x + 1] = LayerTileX(Job.West + (x + 0.5) * Job.DppLon);
            LayerI0 = new int[w];
            LayerFx = new float[w];
            for (int x = 0; x < w; x++)
            {
                float s = LayerTileXf(Job.West + (x + 0.5) * Job.DppLon) - 0.5f;
                int i0 = (int)Math.Floor(s);
                LayerI0[x] = i0;
                LayerFx[x] = s - i0;
            }
        }
    }

    // One image row: the hill shading's metres per pixel at its latitude, and its thermal layer tile row.
    private struct ShadeRow
    {
        public int Y;
        public float Sx, Sy;
        public int TileY, TileYAbove, LayerJ0;
        public float LayerFy;

        public ShadeRow(ShadeRun run, int y)
        {
            RasterJob job = run.Job;
            Y = y;
            double lat = job.North - (y + 0.5) * job.Dpp;
            float mx = run.MxEquator * (float)Math.Max(0.02, Math.Cos(lat * Math.PI / 180.0));
            Sx = run.Z / (2f * mx);
            Sy = run.Z / (2f * run.My);
            TileY = TileYAbove = LayerJ0 = 0;
            LayerFy = 0f;
            if (job.Layer != null)
            {
                TileY = LayerTileY(lat);
                TileYAbove = LayerTileY(lat + job.Dpp);
                float ty = LayerTileYf(lat) - 0.5f;
                LayerJ0 = (int)Math.Floor(ty);
                LayerFy = ty - LayerJ0;
            }
        }
    }

    // A pixel being coloured: straight alpha, 0..255 colour.
    private struct Px
    {
        public float R, G, B, A;

        public Px(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        // The colour moved the share k towards c; alpha is left to the caller.
        public void Mix(float cr, float cg, float cb, float k)
        {
            R += (cr - R) * k;
            G += (cg - G) * k;
            B += (cb - B) * k;
        }

        public void Mix(int[] c, float k) => Mix(c[0], c[1], c[2], k);

        public void Scale(float s)
        {
            R *= s;
            G *= s;
            B *= s;
        }

        // A colour of the given opacity laid over the pixel (straight alpha).
        public void Over(float cr, float cg, float cb, float k)
        {
            float outA = k + A * (1f - k);
            if (outA <= 0f)
                return;
            float under = A * (1f - k) / outA, over = k / outA;
            R = cr * over + R * under;
            G = cg * over + G * under;
            B = cb * over + B * under;
            A = outA;
        }

        public void Over(int[] c, float k) => Over(c[0], c[1], c[2], k);

        public void Write(byte[] px, int o)
        {
            px[o] = ToByte(R);
            px[o + 1] = ToByte(G);
            px[o + 2] = ToByte(B);
            px[o + 3] = ToByte(A * 255f);
        }
    }

    private static void ShadeRows(ShadeRun run, int from, int to)
    {
        RasterJob job = run.Job;
        int w = job.Width, gw = w + 2;
        for (int y = from; y < to; y++)
        {
            var row = new ShadeRow(run, y);
            int line = (y + 1) * gw + 1;
            int o = y * w * 4;
            for (int x = 0; x < w; x++, o += 4)
            {
                if (job.Heights[line + x] < 0f)
                    ShadeSea(run, row, x, line + x, o);
                else
                    ShadeLand(run, row, x, line + x).Write(job.Rgba, o);
            }
        }
    }

    // A water pixel: tinted by depth (or by the chart's shades, or left to the stock map), with what a selected
    // submarine needs on top.
    private static void ShadeSea(ShadeRun run, ShadeRow row, int x, int i, int o)
    {
        RasterJob job = run.Job;
        byte[] px = job.Rgba;
        if (job.Sea == null)
            px[o] = px[o + 1] = px[o + 2] = px[o + 3] = 0;
        else if (run.Theme.IsChart)
            ShadeChartWater(run, i).Write(px, o);
        else
            ShadeWater(run, row, i).Write(px, o);
        if (run.Submarine)
            ShadeSubmarine(run, row, x, i, px, o);
    }

    // A land pixel: height colour, hill shading, the aircraft's danger hatching, contour lines, the theme's coastline
    // and the danger edge line.
    private static Px ShadeLand(ShadeRun run, ShadeRow row, int x, int i)
    {
        RasterJob job = run.Job;
        Theme theme = run.Theme;
        float[] h = job.Heights;
        int gw = job.Width + 2;
        float h0 = h[i];
        float hl = h[i - 1], hr = h[i + 1], hu = h[i - gw], hd = h[i + gw];
        bool shore = hl < 0f || hr < 0f || hu < 0f || hd < 0f;
        // Water next to the land counts as sea level.
        if (hl < 0f) hl = 0f;
        if (hr < 0f) hr = 0f;
        if (hu < 0f) hu = 0f;
        if (hd < 0f) hd = 0f;

        Px p = LandColour(job.Palette, h0, job.Opacity);
        if (run.LandShading > 0f)
            LandRelief(theme, run.LandShading, Lit((hr - hl) * row.Sx, (hu - hd) * row.Sy), ref p);
        if (run.Hatch && h0 >= job.Danger)
        {
            p.Mix(DangerHatch, HatchStrength(x, row.Y));
            if (p.A < 0.75f) p.A = 0.75f;
        }
        float grad = Gradient(hl, hr, hu, hd);
        if (run.Contours && grad > 1e-3f)
            LandContours(run, h0, grad, ref p);
        if (theme.Coast != null && shore)
        {
            // The theme draws its own coastline on the land side of the shore.
            p.Mix(theme.Coast, theme.CoastAmt);
            p.A += theme.CoastAmt * (1f - p.A);
        }
        if (run.Hatch && grad > 1e-3f)
        {
            // Solid red edge where the terrain reaches the aircraft's altitude.
            float edge = Clamp01(1.8f - Math.Abs(h0 - job.Danger) / grad);
            if (edge > 0f)
            {
                p.Mix(DangerEdge, edge);
                p.A += edge * (0.95f - p.A);
            }
        }
        return p;
    }

    private static Px LandColour(Palette pal, float h0, float opacity)
    {
        int[] flat = pal.Theme.LandFlat;
        if (flat != null)
            return new Px(flat[0], flat[1], flat[2], opacity);
        pal.Colour(h0, out float r, out float g, out float b);
        return new Px(r, g, b, opacity);
    }

    // Hill shading: darker away from the light, and (not for the Standard looks) shadows falling towards the theme's
    // shadow colour, lit slopes towards its highlight.
    private static void LandRelief(Theme theme, float shading, float lit, ref Px p)
    {
        float s = 1f + (lit - 1f) * shading;
        p.Scale(s);
        if (theme.Legacy)
            return;
        if (s < 1f && theme.ShadowTint != null)
            p.Mix(theme.ShadowTint, Clamp01((1f - s) * theme.ShadowAmt));
        else if (s > 1f && theme.HighlightTint != null)
            p.Mix(theme.HighlightTint, Clamp01((s - 1f) * theme.HighlightAmt * 3f));
    }

    // Surface normal (-dx, -dy, 1) against light from the north-west, 45 degrees up; flat ground = 1.
    private static float Lit(float dx, float dy)
    {
        float lit = (dx - dy + 1.41421356f) / (1.41421356f * (float)Math.Sqrt(dx * dx + dy * dy + 1f));
        return lit < 0f ? 0f : lit;
    }

    // Height change per pixel from the four neighbours.
    private static float Gradient(float hl, float hr, float hu, float hd)
    {
        float gxp = (hr - hl) * 0.5f, gyp = (hd - hu) * 0.5f;
        return (float)Math.Sqrt(gxp * gxp + gyp * gyp);
    }

    // The red hatching: stripes four pixels wide.
    private static float HatchStrength(int x, int y) => (((x + y) >> 2) & 1) == 0 ? 0.8f : 0.5f;

    // Distance in pixels from this pixel to a contour = height difference / height change per pixel. That gives
    // anti-aliased lines of the same width at every zoom; where lines would merge on steep ground they fade out (bold
    // ones hold on five times longer).
    private static void LandContours(ShadeRun run, float h0, float grad, ref Px p)
    {
        Theme theme = run.Theme;
        float minor = run.Minor, major = run.Major;
        float level = (float)Math.Round(h0 / minor) * minor;
        float boldLevel = (float)Math.Round(h0 / major) * major;
        float perPixel = grad / minor;
        float la = 0f;
        if (boldLevel > 0f)
            la = Clamp01(1.6f - Math.Abs(h0 - boldLevel) / grad) * run.MajorAlpha * Fade(perPixel / 5f);
        if (level > 0f)       // level 0 is the coastline, which the stock map draws
            la = Math.Max(la, Clamp01(1.1f - Math.Abs(h0 - level) / grad) * run.MinorAlpha * Fade(perPixel));
        if (run.HaloAlpha > 0f && level > 0f)
            la = Math.Max(la, Clamp01(1f - Math.Abs(h0 - level) / (grad * 3.5f)) * run.HaloAlpha * Fade(perPixel));
        if (la <= 0f)
            return;
        int[] line = run.Line;
        float lineR = line[0], lineG = line[1], lineB = line[2];
        if (theme.LandLineHigh != null)
        {
            // Lines graded by height: dim and in the low colour near the sea, full and in the high colour up high.
            float tg = Clamp01(h0 / theme.LandLineSpan);
            la *= theme.LandLineLowAmt + (1f - theme.LandLineLowAmt) * tg;
            lineR = line[0] + (theme.LandLineHigh[0] - line[0]) * tg;
            lineG = line[1] + (theme.LandLineHigh[1] - line[1]) * tg;
            lineB = line[2] + (theme.LandLineHigh[2] - line[2]) * tg;
        }
        p.Mix(lineR, lineG, lineB, la);
        p.A += la * (1f - p.A);
    }

    // The four neighbours of a water pixel, land next to the water counting as sea level.
    private static void SeaNeighbours(RasterJob job, int i, out float hl, out float hr, out float hu, out float hd)
    {
        float[] h = job.Heights;
        int gw = job.Width + 2;
        hl = Math.Min(h[i - 1], 0f);
        hr = Math.Min(h[i + 1], 0f);
        hu = Math.Min(h[i - gw], 0f);
        hd = Math.Min(h[i + gw], 0f);
    }

    // One water pixel: the colour of its depth band, the seabed's relief (fainter than on land, with more
    // exaggeration) and, like on a chart, a thin depth line at every band edge but the coast, which the stock map
    // draws.
    private static Px ShadeWater(ShadeRun run, ShadeRow row, int i)
    {
        RasterJob job = run.Job;
        Palette sea = job.Sea;
        Theme theme = sea.Theme;
        float d0 = -job.Heights[i];
        SeaNeighbours(job, i, out float hl, out float hr, out float hu, out float hd);
        sea.Colour(d0, out float r, out float g, out float b);
        var p = new Px(r, g, b, job.Opacity);
        bool darkBand = 0.3f * r + 0.59f * g + 0.11f * b < 110f;

        if (run.SeaShading > 0f)
        {
            float s = 1f + (Lit((hr - hl) * row.Sx * SeabedExaggeration, (hu - hd) * row.Sy * SeabedExaggeration) - 1f) * run.SeaShading;
            p.Scale(s);
            if (!theme.Legacy && s < 1f && theme.ShadowTint != null)
                p.Mix(theme.ShadowTint, Clamp01((1f - s) * theme.ShadowAmt * 0.6f));
        }
        float grad = Gradient(hl, hr, hu, hd);
        if (run.Contours && grad > 1e-3f)
            DepthLine(sea, d0, grad, darkBand, ref p);
        return p;
    }

    // The depth line at the nearest band edge. Lines are light on dark bands and dark on light ones; a theme with its
    // own line colour draws that, with an optional glow, the shelf edge brightest.
    private static void DepthLine(Palette sea, float d0, float grad, bool darkBand, ref Px p)
    {
        float[] limits = sea.Limits;
        int n = limits.Length;
        int bandIndex = sea.BandOf(d0);
        float shallower = bandIndex > 0 ? d0 - limits[bandIndex] : float.PositiveInfinity;
        float deeper = bandIndex + 1 < n ? limits[bandIndex + 1] - d0 : float.PositiveInfinity;
        int edge = shallower <= deeper ? bandIndex : bandIndex + 1;
        float dist = Math.Min(shallower, deeper);
        if (float.IsInfinity(dist))
            return;
        float spacing = limits[edge] - limits[edge - 1];
        if (edge + 1 < n)
            spacing = Math.Min(spacing, limits[edge + 1] - limits[edge]);
        Theme theme = sea.Theme;
        float la = Clamp01(1.1f - dist / grad) * (sea.Dark ? 0.35f : 0.45f) * Fade(grad / spacing);
        float lr = darkBand ? 176f : 28f, lg = darkBand ? 196f : 48f, lb = darkBand ? 216f : 82f;
        if (!theme.Legacy && theme.SeaLine != null)
        {
            bool shelf = Math.Abs(limits[edge] - ShelfDepth) < 1f;
            int[] lc = shelf && theme.ShelfLine != null ? theme.ShelfLine : theme.SeaLine;
            lr = lc[0];
            lg = lc[1];
            lb = lc[2];
            float boost = shelf ? theme.ShelfBoost : 1f;
            float core = Clamp01(1.1f - dist / grad) * Math.Min(1f, theme.SeaLineAmt * boost);
            float halo = theme.SeaLineHalo > 0f ? Clamp01(1f - dist / (grad * 3.5f)) * theme.SeaLineHalo * boost : 0f;
            la = Math.Max(core, halo) * Fade(grad / spacing);
        }
        if (la > 0f)
        {
            p.Mix(lr, lg, lb, la);
            p.A += la * (1f - p.A);
        }
    }

    // A chart's water: the four depth shades of the theme, contours at the standard chart depths with the safety
    // contour bold in its own colour. No relief: a chart shows depths, not the shape of the sea floor.
    private static Px ShadeChartWater(ShadeRun run, int i)
    {
        Theme theme = run.Theme;
        float d0 = -run.Job.Heights[i];
        int[] zone = d0 < run.ChartShallow ? theme.Zones[0] : d0 < run.ChartSafety ? theme.Zones[1] : d0 < run.ChartDeep ? theme.Zones[2] : theme.Zones[3];
        var p = new Px(zone[0], zone[1], zone[2], 1f);
        if (run.Contours && theme.SeaLine != null)
        {
            SeaNeighbours(run.Job, i, out float hl, out float hr, out float hu, out float hd);
            float grad = Gradient(hl, hr, hu, hd);
            if (grad > 1e-3f)
                ChartContour(run, d0, grad, ref p);
        }
        return p;
    }

    private static void ChartContour(ShadeRun run, float d0, float grad, ref Px p)
    {
        float[] levels = run.ChartLevels;
        int n = levels.Length;
        int k = 0;
        while (k < n && levels[k] <= d0)
            k++;
        float below = k > 0 ? d0 - levels[k - 1] : float.PositiveInfinity;
        float above = k < n ? levels[k] - d0 : float.PositiveInfinity;
        int edge = below <= above ? k - 1 : k;
        float dist = Math.Min(below, above);
        if (edge < 0 || edge >= n || float.IsInfinity(dist))
            return;
        float spacing = float.PositiveInfinity;
        if (edge > 0)
            spacing = levels[edge] - levels[edge - 1];
        if (edge + 1 < n)
            spacing = Math.Min(spacing, levels[edge + 1] - levels[edge]);
        if (float.IsInfinity(spacing))
            spacing = levels[edge];
        Theme theme = run.Theme;
        bool safety = edge == run.ChartSafetyLevel;
        float la = Clamp01((safety ? 1.9f : 1.1f) - dist / grad) * Math.Min(1f, theme.SeaLineAmt) * Fade(grad / spacing / (safety ? 3f : 1f));
        if (la > 0f)
            p.Mix(safety && theme.SafetyLine != null ? theme.SafetyLine : theme.SeaLine, la);
    }

    // What a selected submarine needs on a water pixel: the thermal layer's tile in teal - clearly where the
    // submarine, at its depth, is below the layer, faintly where the layer is deeper than the submarine - with a line
    // where the layer area ends; and red hatching with a solid edge where the water is shallower than the submarine.
    private static void ShadeSubmarine(ShadeRun run, ShadeRow row, int x, int i, byte[] px, int o)
    {
        RasterJob job = run.Job;
        var p = new Px(px[o], px[o + 1], px[o + 2], px[o + 3] / 255f);
        if (job.Layer != null)
            ThermalLayer(run, row, x, ref p);
        float keel = job.Keel;
        if (!float.IsNaN(keel) && keel > 0f)
            KeelWarning(job, x, row.Y, i, keel, ref p);
        p.Write(px, o);
    }

    // The tint is soft: the state of each tile (the submarine below the layer, or above it) is blended with its
    // neighbours' by the distance from the tile centres and held to a narrow band around the tile edge, so the
    // squares of the game's model show as rounded patches, not hard blocks. Where two tiles differ the switch lies on
    // their common edge, as in the game.
    private static void ThermalLayer(ShadeRun run, ShadeRow row, int x, ref Px p)
    {
        RasterJob job = run.Job;
        LayerTiles layer = job.Layer;
        bool dark = job.Palette.Dark;
        float keel = job.Keel;
        int tileX = run.LayerCols[x + 1];
        bool here = !float.IsNaN(layer.At(tileX, row.TileY));
        // The edge of the layer area: the tile to the left or above differs in having a layer.
        bool edge = here != !float.IsNaN(layer.At(run.LayerCols[x], row.TileY)) ||
                    here != !float.IsNaN(layer.At(tileX, row.TileYAbove));

        float wBelow = 0f, wAbove = 0f, wDepth = 0f, wExist = 0f;
        int i0 = run.LayerI0[x];
        float fx = run.LayerFx[x];
        for (int k = 0; k < 4; k++)
        {
            float td = layer.At(WrapTile(i0 + (k & 1)), row.LayerJ0 + (k >> 1));
            if (float.IsNaN(td))
                continue;
            float wk = ((k & 1) == 0 ? 1f - fx : fx) * ((k >> 1) == 0 ? 1f - row.LayerFy : row.LayerFy);
            wExist += wk;
            wDepth += wk * td;
            if (!float.IsNaN(keel) && keel > td)
                wBelow += wk;
            else
                wAbove += wk;
        }
        if (wExist > 0f)
        {
            LayerColour(LayerBand(wDepth / wExist), dark, out float cr, out float cg, out float cb);
            float alpha = LayerBelow * SoftWeight(wBelow) + LayerAbove * SoftWeight(wAbove);
            if (alpha > 0f)
                p.Over(cr, cg, cb, alpha);
        }
        if (edge)
            p.Over(dark ? 90f : 0f, dark ? 220f : 150f, dark ? 210f : 150f, 0.7f);
    }

    private static void KeelWarning(RasterJob job, int x, int y, int i, float keel, ref Px p)
    {
        float d0 = -job.Heights[i];
        if (d0 < keel)
            p.Over(DangerHatch, HatchStrength(x, y));
        SeaNeighbours(job, i, out float hl, out float hr, out float hu, out float hd);
        float grad = Gradient(hl, hr, hu, hd);
        if (grad > 1e-3f)
        {
            float line = Clamp01(1.8f - Math.Abs(d0 - keel) / grad);
            if (line > 0f)
                p.Over(DangerEdge, line * 0.95f);
        }
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    /// <summary>Line strength from how many lines meet per pixel: full up to 0.4, gone at 1.</summary>
    private static float Fade(float perPixel) => Math.Max(0f, Math.Min(1f, (1f - perPixel) / 0.6f));

    private static byte ToByte(float v) => v <= 0f ? (byte)0 : (v >= 255f ? (byte)255 : (byte)(v + 0.5f));
}
