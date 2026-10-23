using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MapControl;
using SeaPower;
using SeaPower.LiveMap;
using SeaPowerTopo.Terrain;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SeaPowerTopo.Render;

/// <summary>
/// The overlay itself. The stock map is a Unity scene of lon/lat quads on layer 19 that the map camera renders
/// into the texture the map window shows; one more quad in that scene, above the stock tiles, carries our image
/// and so pans, zooms and wraps with the map for free.
/// <para>The image covers the view plus a margin and is redrawn when the view leaves it, a pan has used up most of
/// the margin on one side, or the zoom changes a lot: heights are copied out of the DEM on the main thread in
/// slices of a few milliseconds per frame, then resampled and coloured on a worker thread, then uploaded. Until
/// then the previous image stays in place.</para>
/// </summary>
internal sealed class OverlayRenderer
{
    private const int MapLayer = 19;                 // the stock map tiles' layer, the only one the map camera sees
    private const float ZOffset = -0.05f;            // in front of the stock tiles (z 0) and background (z 0.1)
    private const double FrameBudgetMs = 3.0;
    // Extra map drawn on every side of the view, as a share of the view: as much as the pixel budget allows at
    // full resolution, within these limits.
    private const double MinMargin = 0.15, MaxMargin = 0.4;
    private const double PrefetchAt = 0.4;           // redraw ahead of a pan once this share of a margin is left
    private const int MaxPixels = 8_000_000, MaxSide = 8192;
    private const double DemLimitDpp = 0.04;         // beyond ~5 DEM samples per pixel the world grid is enough
    private const float SettleSeconds = 0.12f;

    private enum Phase { Idle, Filling, Shading }

    private sealed class Plan
    {
        public double West, North, Dpp, ViewDpp;
        /// <summary>Margin around the view the image was planned with: share of the view, and in degrees.</summary>
        public double Margin, MarginLon, MarginLat;
        public int Width, Height;
        public bool Dem;
        public float Interval, Danger;
        public bool Dark;
        public int SettingsVersion;
        public double South => North - Height * Dpp;
        public double East => West + Width * Dpp;
        public Plan Clone() => (Plan)MemberwiseClone();
    }

    private MapScene scene;
    private Texture2D lowres;
    private float nextSceneLookup;
    private GameObject quad;
    private MeshRenderer quadRenderer;
    private Material material;
    private Texture2D texture;
    private bool shaderMissing;

    private Phase phase;
    private readonly SourceGrid source = new SourceGrid();
    private int fillRow, fillX0, fillY0, fillStep;
    private RasterJob building, shown, spare;
    private Plan buildingPlan, shownPlan;
    private HashSet<string> buildingMissing = new HashSet<string>(), shownMissing = new HashSet<string>();
    private Task<bool> work;
    private volatile bool cancel;
    private readonly Stopwatch timer = new Stopwatch();

    private Palette palette, seaPalette;
    private int paletteVersion = -1;
    private bool paletteDark;

    private double lastWest, lastSouth, lastEast, lastNorth;
    private float lastViewChange;
    private float nextMissingCheck;

    /// <summary>Palettes, contour interval and highlight altitude of the image on screen, for the legend.</summary>
    public Palette Palette => palette;
    /// <summary>Depth bands of the sea; null while the sea is left to the stock map.</summary>
    public Palette SeaPalette => seaPalette;
    public float ShownInterval => shownPlan?.Interval ?? 0f;
    public float ShownDanger => shownPlan?.Danger ?? float.NaN;
    public bool HasImage => shown != null && quad != null;

    /// <summary>Lowres world grid of the current map scene, for the cursor readout.</summary>
    public Texture2D Lowres => FindScene() ? lowres : null;

    /// <param name="visible">The Topography layer is on.</param>
    /// <param name="dark">The map shows its night theme.</param>
    /// <param name="danger">Highlight terrain at or above this many metres; NaN = off.</param>
    public void Tick(bool visible, bool dark, float danger)
    {
        if (!visible)
        {
            if (quadRenderer != null)
                quadRenderer.enabled = false;
            if (phase != Phase.Idle)
                Abort();
            return;
        }
        if (shaderMissing || !FindScene() || !EnsureQuad())
            return;

        BoundingBox view = scene.Viewport;
        RenderTexture target = scene.Camera != null ? scene.Camera.MapTexture : null;
        if (view == null || target == null || target.height < 2 || view.Height <= 0)
            return;
        TrackView(view);
        EnsurePalette(dark);

        switch (phase)
        {
            case Phase.Filling:
                ContinueFill();
                break;
            case Phase.Shading:
                if (work.IsCompleted)
                    Finish();
                break;
            default:
                StartIfStale(view, target.height, dark, danger);
                break;
        }
        quadRenderer.enabled = shown != null;
    }

    /// <summary>Mission unloading: drop everything; the map scene goes with the mission.</summary>
    public void Shutdown()
    {
        // The worker only touches managed buffers, so there is nothing to wait for.
        cancel = true;
        work = null;
        phase = Phase.Idle;
        shown = spare = building = null;
        shownPlan = buildingPlan = null;
        source.V = null;
        shownMissing.Clear();
        if (quad != null)
            Object.Destroy(quad);
        if (texture != null)
            Object.Destroy(texture);
        quad = null;
        quadRenderer = null;
        texture = null;
        scene = null;
        lowres = null;
    }

    private bool FindScene()
    {
        if (scene != null)
            return true;
        if (Time.unscaledTime < nextSceneLookup)
            return false;
        nextSceneLookup = Time.unscaledTime + 1f;
        scene = Object.FindFirstObjectByType<MapScene>();
        if (scene == null)
            return false;

        // The background quads covering the whole world carry the 3 arc-minute grid as their heightmap.
        lowres = null;
        foreach (MapRect rect in scene.GetComponentsInChildren<MapRect>(true))
        {
            if (rect.Width >= 359f && rect.heightmap != null && rect.heightmap.width == HeightReader.LowCols)
            {
                lowres = rect.heightmap;
                break;
            }
        }
        if (lowres == null)
            ModLog.Warn("World height grid of the map not found; zoomed-out areas without loaded DEM stay uncoloured.");
        // A new scene means a new mission: anything from the old one is gone.
        shown = spare = null;
        shownPlan = null;
        quad = null;
        quadRenderer = null;
        return true;
    }

    private bool EnsureQuad()
    {
        if (quad != null)
            return true;
        if (material == null)
        {
            Shader shader = null;
            foreach (string name in new[] { "Sprites/Default", "UI/Default", "Unlit/Transparent" })
            {
                shader = Shader.Find(name);
                if (shader != null)
                    break;
            }
            if (shader == null)
            {
                shaderMissing = true;
                ModLog.Error("No transparent shader found (Sprites/Default) - overlay off.");
                return false;
            }
            material = new Material(shader) { name = "SeaPowerTopo overlay" };
            ModLog.Info($"Overlay shader: {shader.name}");
        }

        quad = new GameObject("SeaPowerTopo overlay") { layer = MapLayer };
        quad.transform.SetParent(scene.transform, false);
        var mesh = new Mesh
        {
            name = "SeaPowerTopo quad",
            vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) },
            // Image row 0 is the north edge, and a texture's first row is v = 0, so v runs north to south.
            uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0) },
            colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) },
            triangles = new[] { 0, 2, 1, 2, 3, 1 },
        };
        mesh.RecalculateBounds();
        quad.AddComponent<MeshFilter>().sharedMesh = mesh;
        quadRenderer = quad.AddComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = material;
        quadRenderer.renderingLayerMask = 1u << MapLayer;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        quadRenderer.enabled = false;
        if (shown != null)
            Place(shown);
        return true;
    }

    private void TrackView(BoundingBox view)
    {
        if (view.West != lastWest || view.East != lastEast || view.North != lastNorth || view.South != lastSouth)
        {
            lastWest = view.West;
            lastEast = view.East;
            lastNorth = view.North;
            lastSouth = view.South;
            lastViewChange = Time.unscaledTime;
        }
    }

    private void EnsurePalette(bool dark)
    {
        if (palette != null && paletteVersion == Settings.Version && paletteDark == dark)
            return;
        float[] limits = Palette.ParseLimits(Settings.Bands.Value);
        if (limits == null)
        {
            ModLog.Warn($"Height bands '{Settings.Bands.Value}' unusable (ascending metres from 0) - using the default.");
            limits = Palette.DefaultLimits;
        }
        palette = new Palette(limits, dark);
        seaPalette = null;
        if (Settings.SeaDepth.Value)
        {
            float[] depths = Palette.ParseLimits(Settings.DepthBands.Value);
            if (depths == null)
            {
                ModLog.Warn($"Depth bands '{Settings.DepthBands.Value}' unusable (ascending metres from 0) - using the default.");
                depths = Palette.DefaultDepthLimits;
            }
            seaPalette = new Palette(depths, dark, sea: true);
        }
        paletteVersion = Settings.Version;
        paletteDark = dark;
    }

    private void StartIfStale(BoundingBox view, int viewPixels, bool dark, float danger)
    {
        double viewDpp = view.Height / viewPixels;
        bool covered = shownPlan != null && Covers(shownPlan, view);
        double zoom = shownPlan != null ? shownPlan.ViewDpp / viewDpp : 1.0;
        bool nearEdge = covered && NearEdge(shownPlan, view);
        bool geometryStale = shown == null || !covered || shownPlan.SettingsVersion != Settings.Version ||
                             zoom > 1.45 || zoom < 0.7 || nearEdge || DemArrived();
        if (geometryStale)
        {
            // Let a zoom settle first, unless the view has run off the image. A pan that has used up most of the
            // margin redraws at once, so the new image is there before the view reaches the edge.
            bool panning = nearEdge && zoom < 1.15 && zoom > 0.87;
            if (covered && !panning && Time.unscaledTime - lastViewChange < SettleSeconds)
                return;
            StartFull(view, viewDpp, dark, danger);
            return;
        }
        if (shownPlan.Dark != dark || DangerMoved(shownPlan.Danger, danger))
            StartRecolour(dark, danger);
    }

    private static bool Covers(Plan p, BoundingBox view)
    {
        const double eps = 1e-9;
        return view.West >= p.West - eps && view.East <= p.East + eps &&
               (view.North <= p.North + eps || p.North >= 90.0 - eps) &&
               (view.South >= p.South - eps || p.South <= -90.0 + eps);
    }

    // Less than PrefetchAt of the planned margin left on some side (an edge at a pole doesn't count).
    private static bool NearEdge(Plan p, BoundingBox view)
    {
        const double eps = 1e-9;
        double lon = p.MarginLon * PrefetchAt, lat = p.MarginLat * PrefetchAt;
        if (view.West - p.West < lon || p.East - view.East < lon)
            return true;
        if (p.North < 90.0 - eps && p.North - view.North < lat)
            return true;
        return p.South > -90.0 + eps && view.South - p.South < lat;
    }

    private static bool DangerMoved(float shownDanger, float danger)
    {
        bool shownOn = !float.IsNaN(shownDanger), on = !float.IsNaN(danger);
        if (shownOn != on)
            return true;
        return on && Math.Abs(shownDanger - danger) > Math.Max(10f, 0.03f * Math.Abs(danger));
    }

    // A DEM chunk the image had to do without has been loaded since (the map loads them as you zoom in).
    private bool DemArrived()
    {
        if (shownMissing.Count == 0 || Time.unscaledTime < nextMissingCheck)
            return false;
        nextMissingCheck = Time.unscaledTime + 0.5f;
        foreach (string key in shownMissing)
        {
            if (TerrainLoader.IsDEMChunkAvailable(key))
                return true;
        }
        return false;
    }

    private void StartFull(BoundingBox view, double viewDpp, bool dark, float danger)
    {
        double dpp = viewDpp / Settings.Resolution.Value;
        double spanLon = view.East - view.West, spanLat = view.North - view.South;
        double margin = (Math.Sqrt(MaxPixels / (spanLon / dpp * (spanLat / dpp))) - 1.0) / 2.0;
        margin = Math.Min(margin, (MaxSide * dpp / Math.Max(spanLon, spanLat) - 1.0) / 2.0);
        margin = Math.Max(MinMargin, Math.Min(MaxMargin, margin));
        double west = view.West - spanLon * margin, east = view.East + spanLon * margin;
        double north = Math.Min(90.0, view.North + spanLat * margin), south = Math.Max(-90.0, view.South - spanLat * margin);
        int width = (int)Math.Ceiling((east - west) / dpp), height = (int)Math.Ceiling((north - south) / dpp);
        double shrink = Math.Max(Math.Sqrt((double)width * height / MaxPixels), Math.Max(width / (double)MaxSide, height / (double)MaxSide));
        if (shrink > 1.0)
        {
            dpp *= shrink;
            width = (int)Math.Ceiling((east - west) / dpp);
            height = (int)Math.Ceiling((north - south) / dpp);
        }

        var plan = new Plan
        {
            West = west,
            North = north,
            Dpp = dpp,
            ViewDpp = viewDpp,
            Margin = margin,
            MarginLon = spanLon * margin,
            MarginLat = spanLat * margin,
            Width = Math.Max(2, width),
            Height = Math.Max(2, height),
            Dem = dpp < DemLimitDpp,
            Dark = dark,
            Danger = danger,
            SettingsVersion = Settings.Version,
        };
        plan.Interval = !Settings.Contours.Value ? 0f
            : Settings.ContourInterval.Value > 0 ? Settings.ContourInterval.Value
            : Raster.AutoInterval(viewDpp * Raster.MetresPerDegree);

        // Source grid: DEM samples (every n-th when zoomed out) or world grid texels, with two cells of margin
        // around the image for the interpolation.
        if (plan.Dem)
        {
            fillStep = Math.Max(1, (int)Math.Round(dpp * HeightReader.DemPerDegree));
            fillX0 = (int)Math.Floor((west + 180.0) * HeightReader.DemPerDegree) - 2 * fillStep;
            fillY0 = (int)Math.Floor((90.0 - north) * HeightReader.DemPerDegree) - 2 * fillStep;
            source.Lon0 = -180.0 + fillX0 / HeightReader.DemPerDegree;
            source.Lat0 = 90.0 - fillY0 / HeightReader.DemPerDegree;
            source.Cell = fillStep / HeightReader.DemPerDegree;
        }
        else
        {
            fillStep = Math.Max(1, (int)Math.Round(dpp / HeightReader.LowCell));
            fillX0 = (int)Math.Floor((west + 180.0) / HeightReader.LowCell - 0.5) - 2 * fillStep;
            fillY0 = (int)Math.Floor((90.0 - north) / HeightReader.LowCell - 0.5) - 2 * fillStep;
            source.Lon0 = -180.0 + (fillX0 + 0.5) * HeightReader.LowCell;
            source.Lat0 = 90.0 - (fillY0 + 0.5) * HeightReader.LowCell;
            source.Cell = fillStep * HeightReader.LowCell;
        }
        source.Allocate((int)Math.Ceiling((plan.East - source.Lon0) / source.Cell) + 3,
                        (int)Math.Ceiling((source.Lat0 - plan.South) / source.Cell) + 3);

        building = TakeJob();
        if (shown != null && building.Heights == shown.Heights)
            building.Heights = null;
        Configure(building, plan);
        buildingPlan = plan;
        buildingMissing = new HashSet<string>();
        fillRow = 0;
        cancel = false;
        phase = Phase.Filling;
        timer.Restart();
        ContinueFill();
    }

    private void StartRecolour(bool dark, float danger)
    {
        Plan plan = shownPlan.Clone();
        plan.Dark = dark;
        plan.Danger = danger;
        building = TakeJob();
        Configure(building, plan);
        building.Heights = shown.Heights;
        buildingPlan = plan;
        buildingMissing = new HashSet<string>(shownMissing);
        cancel = false;
        timer.Restart();
        StartWorker(resample: false);
    }

    private RasterJob TakeJob()
    {
        RasterJob job = spare ?? new RasterJob();
        spare = null;
        return job;
    }

    private void Configure(RasterJob job, Plan plan)
    {
        job.Width = plan.Width;
        job.Height = plan.Height;
        job.West = plan.West;
        job.North = plan.North;
        job.Dpp = plan.Dpp;
        job.Palette = palette;
        job.Sea = seaPalette;
        job.Opacity = Settings.Opacity.Value;
        job.Hillshade = Settings.Hillshade.Value;
        job.Interval = plan.Interval;
        job.Danger = plan.Danger;
    }

    private void ContinueFill()
    {
        var slice = Stopwatch.StartNew();
        using (var reader = new HeightReader(lowres, buildingMissing))
        {
            if (!buildingPlan.Dem && !reader.HasLowres)
            {
                Abort();
                return;
            }
            while (fillRow < source.Ny)
            {
                FillRow(reader, fillRow++);
                if (slice.Elapsed.TotalMilliseconds > FrameBudgetMs)
                    break;
            }
        }
        if (fillRow >= source.Ny)
            StartWorker(resample: true);
    }

    private void FillRow(HeightReader reader, int j)
    {
        float[] v = source.V;
        int nx = source.Nx, o = j * nx;
        if (buildingPlan.Dem)
        {
            int gy = fillY0 + j * fillStep;
            double lat = 90.0 - gy / HeightReader.DemPerDegree;
            for (int i = 0; i < nx; i++)
            {
                int gx = fillX0 + i * fillStep;
                v[o + i] = reader.TryDem(gx, gy, out short raw)
                    ? reader.Sim(raw)
                    : reader.Sim(reader.LowAt(-180.0 + gx / HeightReader.DemPerDegree, lat));
            }
        }
        else
        {
            int ly = fillY0 + j * fillStep;
            for (int i = 0; i < nx; i++)
                v[o + i] = reader.Sim(reader.Low(fillX0 + i * fillStep, ly));
        }
    }

    private void StartWorker(bool resample)
    {
        RasterJob job = building;
        SourceGrid src = source;
        Func<bool> cancelled = () => cancel;
        work = System.Threading.Tasks.Task.Run(() => (!resample || Raster.Resample(src, job, cancelled)) && Raster.Shade(job, cancelled));
        phase = Phase.Shading;
    }

    private void Finish()
    {
        Task<bool> done = work;
        work = null;
        phase = Phase.Idle;
        if (done.IsFaulted)
        {
            ModLog.Error($"Overlay redraw failed: {done.Exception?.GetBaseException()}");
            spare = building;
            building = null;
            return;
        }
        if (cancel || !done.Result)
        {
            spare = building;
            building = null;
            return;
        }

        RasterJob job = building;
        building = null;
        if (texture == null || texture.width != job.Width || texture.height != job.Height)
        {
            if (texture != null)
                Object.Destroy(texture);
            texture = new Texture2D(job.Width, job.Height, TextureFormat.RGBA32, false)
            {
                name = "SeaPowerTopo overlay",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            material.mainTexture = texture;
        }
        texture.LoadRawTextureData(job.Rgba);
        texture.Apply(false, false);
        Place(job);

        spare = shown;
        shown = job;
        shownPlan = buildingPlan;
        shownMissing = buildingMissing;
        ModLog.Debug($"Overlay {job.Width}x{job.Height} (margin {shownPlan.Margin:0.00}), {(shownPlan.Dem ? "DEM" : "world grid")}, " +
                     $"{shownPlan.Dpp * HeightReader.DemPerDegree:0.00} DEM cells/px, contours {shownPlan.Interval:0} m, " +
                     $"{shownMissing.Count} chunk(s) missing, {timer.ElapsedMilliseconds} ms");
    }

    private void Place(RasterJob job)
    {
        if (quad == null)
            return;
        quad.transform.localPosition = new Vector3((float)job.West, (float)job.South, ZOffset);
        quad.transform.localScale = new Vector3((float)(job.Width * job.Dpp), (float)(job.Height * job.Dpp), 1f);
    }

    private void Abort()
    {
        cancel = true;
        if (phase == Phase.Filling)
        {
            spare = building;
            building = null;
            phase = Phase.Idle;
        }
        // A running worker sees the flag, finishes early and is discarded in Finish().
    }
}
