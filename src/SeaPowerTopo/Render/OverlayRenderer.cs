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

/// <summary>A lon/lat rectangle on the map, in degrees.</summary>
internal readonly struct GeoRect : IEquatable<GeoRect>
{
    public readonly double West, South, East, North;

    public GeoRect(double west, double south, double east, double north)
    {
        West = west;
        South = south;
        East = east;
        North = north;
    }

    public double Width => East - West;
    public double Height => North - South;

    public bool Contains(GeoRect r) => r.West >= West && r.East <= East && r.South >= South && r.North <= North;

    public bool Equals(GeoRect r) => r.West == West && r.South == South && r.East == East && r.North == North;

    public override string ToString() => $"{South:0.0}..{North:0.0}°N {West:0.0}..{East:0.0}°E";
}

/// <summary>The live map's scene of the current mission, shared by every overlay layer.</summary>
internal sealed class MapSceneRef
{
    private float nextLookup;

    public MapScene Scene { get; private set; }
    /// <summary>The 3 arc-minute world grid behind the zoomed-out map (null if not found).</summary>
    public Texture2D Lowres { get; private set; }
    /// <summary>Counts the scenes found: a layer that sees a new one drops its quad, which went with the old scene.</summary>
    public int Generation { get; private set; }

    public bool Find()
    {
        if (Scene != null)
            return true;
        if (Time.unscaledTime < nextLookup)
            return false;
        nextLookup = Time.unscaledTime + 1f;
        Scene = Object.FindFirstObjectByType<MapScene>();
        if (Scene == null)
            return false;

        // The background quads covering the whole world carry the 3 arc-minute grid as their heightmap.
        Lowres = null;
        foreach (MapRect rect in Scene.GetComponentsInChildren<MapRect>(true))
        {
            if (rect.Width >= 359f && rect.heightmap != null && rect.heightmap.width == HeightReader.LowCols)
            {
                Lowres = rect.heightmap;
                break;
            }
        }
        if (Lowres == null)
            ModLog.Warn("World height grid of the map not found; zoomed-out areas without loaded DEM stay uncoloured.");
        Generation++;
        return true;
    }

    /// <summary>What the map shows, and its scale in degrees per pixel of the map texture.</summary>
    public bool TryView(out GeoRect view, out double viewDpp)
    {
        view = default;
        viewDpp = 0;
        if (!Find())
            return false;
        BoundingBox box = Scene.Viewport;
        RenderTexture target = Scene.Camera != null ? Scene.Camera.MapTexture : null;
        if (box == null || target == null || target.height < 2 || box.Height <= 0)
            return false;
        view = new GeoRect(box.West, box.South, box.East, box.North);
        viewDpp = box.Height / target.height;
        return true;
    }

    public void Forget()
    {
        Scene = null;
        Lowres = null;
    }
}

/// <summary>
/// One overlay layer. The stock map is a Unity scene of lon/lat quads on layer 19 that the map camera renders
/// into the texture the map window shows; one more quad in that scene, above the stock tiles, carries our image
/// and so pans, zooms and wraps with the map for free.
/// <para>A live layer's image covers the view plus a margin and is redrawn when the view leaves it, a pan has used
/// up most of the margin on one side, or the zoom changes a lot. A pre-drawn layer covers a fixed area at a fixed
/// scale (the mission area, the world) and is drawn once. Either way heights are copied out of the DEM on the main
/// thread in slices of a few milliseconds per frame, then resampled and coloured on worker threads, then uploaded;
/// until then the previous image stays in place. Layers in front cut holes into the ones behind, so translucent
/// images never stack.</para>
/// </summary>
internal sealed class OverlayRenderer
{
    private const int MapLayer = 19;                 // the stock map tiles' layer, the only one the map camera sees
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
        public GeoRect Rect => new GeoRect(West, South, East, North);
        public Plan Clone() => (Plan)MemberwiseClone();
    }

    private readonly MapSceneRef map;
    private readonly string name;
    private readonly float zOffset;
    private int generation = -1;

    // A pre-drawn layer: its area, scale, and whether its scale is fine enough to read the DEM.
    private GeoRect? area;
    private double areaDpp;
    private bool areaDem;

    private GameObject quad;
    private MeshRenderer quadRenderer;
    private Mesh mesh;
    private Material material;
    private Texture2D texture;
    private bool shaderMissing;
    private readonly List<GeoRect> holes = new List<GeoRect>();
    private bool meshDirty = true, meshEmpty;

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

    private double lastWest, lastSouth, lastEast, lastNorth;
    private float lastViewChange;
    private float nextMissingCheck;

    // One set of palettes for all layers, rebuilt when the settings or the theme change.
    private static Palette sharedLand, sharedSea;
    private static int sharedVersion = -1;
    private static bool sharedDark;

    /// <param name="name">For the log.</param>
    /// <param name="zOffset">Depth in the map scene: the stock tiles are at 0, the background at 0.1, nearer is in front.</param>
    public OverlayRenderer(MapSceneRef map, string name, float zOffset)
    {
        this.map = map;
        this.name = name;
        this.zOffset = zOffset;
    }

    /// <summary>Palettes, contour interval and highlight altitude of the image on screen, for the legend.</summary>
    public Palette Palette => palette;
    /// <summary>Depth bands of the sea; null while the sea is left to the stock map.</summary>
    public Palette SeaPalette => seaPalette;
    public float ShownInterval => shownPlan?.Interval ?? 0f;
    public float ShownDanger => shownPlan?.Danger ?? float.NaN;
    public bool HasImage => shown != null && quad != null;

    /// <summary>Copying heights out of the DEM right now (the main-thread part of a redraw).</summary>
    public bool Filling => phase == Phase.Filling;
    /// <summary>On the map this frame.</summary>
    public bool Showing => quadRenderer != null && quadRenderer.enabled;
    /// <summary>Where the image on the map lies.</summary>
    public GeoRect ShownRect => shownPlan?.Rect ?? default;

    /// <summary>The fixed area of a pre-drawn layer (null for the live layer).</summary>
    public GeoRect? Area => area;
    /// <summary>Degrees per pixel of a pre-drawn layer.</summary>
    public double AreaDpp => areaDpp;

    /// <summary>Makes this a pre-drawn layer of a fixed area at a fixed scale (degrees per pixel).</summary>
    public void SetArea(GeoRect rect, double dpp, bool dem)
    {
        area = rect;
        areaDpp = dpp;
        areaDem = dem;
    }

    /// <summary>
    /// Image areas of the layers in front: this layer leaves them out, so their translucent colours don't stack
    /// with its own.
    /// </summary>
    public void SetHoles(List<GeoRect> front)
    {
        if (holes.Count == front.Count)
        {
            bool same = true;
            for (int i = 0; i < holes.Count && same; i++)
                same = holes[i].Equals(front[i]);
            if (same)
                return;
        }
        holes.Clear();
        holes.AddRange(front);
        meshDirty = true;
    }

    /// <param name="on">Keep an image (draw one if needed); off drops a redraw under way and hides the layer.</param>
    /// <param name="show">Show it on the map this frame.</param>
    /// <param name="dark">The map shows its night theme.</param>
    /// <param name="danger">Highlight terrain at or above this many metres; NaN = off (always off for pre-drawn layers).</param>
    /// <param name="mayStart">May use this frame's main-thread budget (another layer isn't using it).</param>
    public void Tick(bool on, bool show, bool dark, float danger, bool mayStart)
    {
        if (map.Generation != generation)
        {
            // A new scene means a new mission: anything from the old one is gone.
            generation = map.Generation;
            shown = spare = null;
            shownPlan = null;
            quad = null;
            quadRenderer = null;
            mesh = null;
        }
        if (!on)
        {
            if (quadRenderer != null)
                quadRenderer.enabled = false;
            if (phase != Phase.Idle)
                Abort();
            return;
        }
        if (shaderMissing || !map.Find() || !EnsureQuad())
            return;
        EnsurePalette(dark);

        bool haveView = map.TryView(out GeoRect view, out double viewDpp);
        if (haveView)
            TrackView(view);
        switch (phase)
        {
            case Phase.Filling:
                if (mayStart || area == null)
                    ContinueFill();
                break;
            case Phase.Shading:
                if (work.IsCompleted)
                    Finish();
                break;
            default:
                if (area != null)
                    StartFixedIfStale(dark, mayStart);
                else if (haveView)
                    StartIfStale(view, viewDpp, dark, danger);
                break;
        }
        if (meshDirty)
            BuildMesh();
        quadRenderer.enabled = show && shown != null && !meshEmpty;
    }

    /// <summary>Mission unloading: drop everything; the map scene goes with the mission.</summary>
    public void Shutdown()
    {
        // The workers only touch managed buffers, so there is nothing to wait for.
        cancel = true;
        work = null;
        phase = Phase.Idle;
        shown = spare = building = null;
        shownPlan = buildingPlan = null;
        source.V = null;
        shownMissing.Clear();
        holes.Clear();
        area = null;
        if (quad != null)
            Object.Destroy(quad);
        if (texture != null)
            Object.Destroy(texture);
        quad = null;
        quadRenderer = null;
        mesh = null;
        texture = null;
    }

    private bool EnsureQuad()
    {
        if (quad != null)
            return true;
        if (material == null)
        {
            Shader shader = null;
            foreach (string candidate in new[] { "Sprites/Default", "UI/Default", "Unlit/Transparent" })
            {
                shader = Shader.Find(candidate);
                if (shader != null)
                    break;
            }
            if (shader == null)
            {
                shaderMissing = true;
                ModLog.Error("No transparent shader found (Sprites/Default) - overlay off.");
                return false;
            }
            material = new Material(shader) { name = $"SeaPowerTopo {name}" };
            if (texture != null)
                material.mainTexture = texture;
        }

        quad = new GameObject($"SeaPowerTopo {name}") { layer = MapLayer };
        quad.transform.SetParent(map.Scene.transform, false);
        mesh = new Mesh { name = $"SeaPowerTopo {name}" };
        meshDirty = true;
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

    /// <summary>The image minus the holes (see <see cref="Cutout"/>), in the unit square the quad's transform scales.</summary>
    private void BuildMesh()
    {
        meshDirty = false;
        if (mesh == null || shownPlan == null)
            return;
        GeoRect r = shownPlan.Rect;
        var cut = new List<UnitRect>(holes.Count);
        foreach (GeoRect h in holes)
            cut.Add(new UnitRect(Unit(h.West, r.West, r.Width), Unit(h.South, r.South, r.Height),
                                 Unit(h.East, r.West, r.Width), Unit(h.North, r.South, r.Height)));
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        foreach (UnitRect part in Cutout.Rects(cut))
            AddRect(vertices, uvs, triangles, part.X0, part.Y0, part.X1, part.Y1);

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        var colors = new Color32[vertices.Count];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = new Color32(255, 255, 255, 255);
        mesh.colors32 = colors;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        meshEmpty = triangles.Count == 0;
    }

    private static float Unit(double v, double origin, double size) =>
        (float)Math.Max(0.0, Math.Min(1.0, (v - origin) / size));

    private static void AddRect(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, float x0, float y0, float x1, float y1)
    {
        int b = vertices.Count;
        vertices.Add(new Vector3(x0, y0, 0));
        vertices.Add(new Vector3(x1, y0, 0));
        vertices.Add(new Vector3(x0, y1, 0));
        vertices.Add(new Vector3(x1, y1, 0));
        // Image row 0 is the north edge, and a texture's first row is v = 0, so v runs north to south.
        uvs.Add(new Vector2(x0, 1f - y0));
        uvs.Add(new Vector2(x1, 1f - y0));
        uvs.Add(new Vector2(x0, 1f - y1));
        uvs.Add(new Vector2(x1, 1f - y1));
        triangles.Add(b);
        triangles.Add(b + 2);
        triangles.Add(b + 1);
        triangles.Add(b + 2);
        triangles.Add(b + 3);
        triangles.Add(b + 1);
    }

    private void TrackView(GeoRect view)
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
        if (sharedLand == null || sharedVersion != Settings.Version || sharedDark != dark)
        {
            float[] limits = Palette.ParseLimits(Settings.Bands.Value);
            if (limits == null)
            {
                ModLog.Warn($"Height bands '{Settings.Bands.Value}' unusable (ascending metres from 0) - using the default.");
                limits = Palette.DefaultLimits;
            }
            sharedLand = new Palette(limits, dark);
            sharedSea = null;
            if (Settings.SeaDepth.Value)
            {
                float[] depths = Palette.ParseLimits(Settings.DepthBands.Value);
                if (depths == null)
                {
                    ModLog.Warn($"Depth bands '{Settings.DepthBands.Value}' unusable (ascending metres from 0) - using the default.");
                    depths = Palette.DefaultDepthLimits;
                }
                sharedSea = new Palette(depths, dark, sea: true);
            }
            sharedVersion = Settings.Version;
            sharedDark = dark;
        }
        palette = sharedLand;
        seaPalette = sharedSea;
    }

    private void StartIfStale(GeoRect view, double viewDpp, bool dark, float danger)
    {
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

    private void StartFixedIfStale(bool dark, bool mayStart)
    {
        if (!mayStart)
            return;
        GeoRect a = area.Value;
        bool sameArea = shownPlan != null && shownPlan.West == a.West && shownPlan.North == a.North && shownPlan.Dpp == areaDpp;
        if (shown != null && sameArea && shownPlan.SettingsVersion == Settings.Version && shownPlan.Dark == dark && !DemArrived())
            return;
        var plan = new Plan
        {
            West = a.West,
            North = a.North,
            Dpp = areaDpp,
            ViewDpp = areaDpp,
            Width = Math.Max(2, (int)Math.Ceiling(a.Width / areaDpp)),
            Height = Math.Max(2, (int)Math.Ceiling(a.Height / areaDpp)),
            Dem = areaDem,
            Dark = dark,
            Danger = float.NaN,
            SettingsVersion = Settings.Version,
        };
        plan.Interval = ContourInterval(areaDpp);
        Begin(plan);
    }

    private static bool Covers(Plan p, GeoRect view)
    {
        const double eps = 1e-9;
        return view.West >= p.West - eps && view.East <= p.East + eps &&
               (view.North <= p.North + eps || p.North >= 90.0 - eps) &&
               (view.South >= p.South - eps || p.South <= -90.0 + eps);
    }

    // Less than PrefetchAt of the planned margin left on some side (an edge at a pole doesn't count).
    private static bool NearEdge(Plan p, GeoRect view)
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

    private static float ContourInterval(double dpp) =>
        !Settings.Contours.Value ? 0f
        : Settings.ContourInterval.Value > 0 ? Settings.ContourInterval.Value
        : Raster.AutoInterval(dpp * Raster.MetresPerDegree);

    private void StartFull(GeoRect view, double viewDpp, bool dark, float danger)
    {
        double dpp = viewDpp / Settings.Resolution.Value;
        double spanLon = view.Width, spanLat = view.Height;
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
        plan.Interval = ContourInterval(viewDpp);
        Begin(plan);
    }

    // Source grid: DEM samples (every n-th when zoomed out) or world grid texels, with two cells of margin around
    // the image for the interpolation. Then the main-thread fill starts.
    private void Begin(Plan plan)
    {
        double dpp = plan.Dpp, west = plan.West, north = plan.North;
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
        using (var reader = new HeightReader(map.Lowres, buildingMissing))
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
        bool mips = area != null;
        Func<bool> cancelled = () => cancel;
        work = System.Threading.Tasks.Task.Run(() => (!resample || Raster.Resample(src, job, cancelled)) && Raster.Shade(job, cancelled) &&
                              (!mips || Raster.Mipmaps(job, cancelled)));
        phase = Phase.Shading;
    }

    private void Finish()
    {
        Task<bool> done = work;
        work = null;
        phase = Phase.Idle;
        if (done.IsFaulted)
        {
            ModLog.Error($"Overlay redraw ({name}) failed: {done.Exception?.GetBaseException()}");
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
        bool mips = job.Mips != null && area != null;
        if (texture == null || texture.width != job.Width || texture.height != job.Height || texture.mipmapCount > 1 != mips)
        {
            if (texture != null)
                Object.Destroy(texture);
            texture = new Texture2D(job.Width, job.Height, TextureFormat.RGBA32, mips)
            {
                name = $"SeaPowerTopo {name}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear,
            };
            material.mainTexture = texture;
        }
        if (!mips)
        {
            texture.LoadRawTextureData(job.Rgba);
            texture.Apply(false, false);
        }
        else
        {
            bool loaded = false;
            if (texture.mipmapCount == Raster.MipCount(job.Width, job.Height))
            {
                try
                {
                    texture.LoadRawTextureData(job.Mips);
                    texture.Apply(false, false);
                    loaded = true;
                }
                catch (Exception ex)
                {
                    ModLog.Debug($"Overlay {name}: own mip levels refused ({ex.Message})");
                }
            }
            if (!loaded)
            {
                // Unity lays the levels out differently: let it make them.
                texture.SetPixelData(job.Rgba, 0);
                texture.Apply(true, false);
            }
        }
        Place(job);

        spare = shown;
        shown = job;
        shownPlan = buildingPlan;
        shownMissing = buildingMissing;
        meshDirty = true;
        ModLog.Debug($"Overlay {name} {job.Width}x{job.Height} (margin {shownPlan.Margin:0.00}), {(shownPlan.Dem ? "DEM" : "world grid")}, " +
                     $"{shownPlan.Dpp * HeightReader.DemPerDegree:0.00} DEM cells/px, contours {shownPlan.Interval:0} m, " +
                     $"{shownMissing.Count} chunk(s) missing, {timer.ElapsedMilliseconds} ms");
        if (area != null)
        {
            // A pre-drawn layer is never recoloured, only drawn again: its buffers can go.
            job.Heights = null;
            job.Rgba = null;
            job.Mips = null;
            spare = null;
            source.V = null;
        }
    }

    private void Place(RasterJob job)
    {
        if (quad == null)
            return;
        quad.transform.localPosition = new Vector3((float)job.West, (float)job.South, zOffset);
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
