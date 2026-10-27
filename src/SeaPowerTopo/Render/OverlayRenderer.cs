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
using Image = Noesis.Image;
using TextureSource = Noesis.TextureSource;
using Visibility = Noesis.Visibility;

namespace SeaPowerTopo.Render;

/// <summary>A lon/lat rectangle on the map, in degrees (longitudes may run past ±180 across the date line).</summary>
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
    public double CentreLon => (West + East) / 2;

    public bool Contains(GeoRect r) => r.West >= West && r.East <= East && r.South >= South && r.North <= North;

    /// <summary>Holds <paramref name="r"/> in any turn of longitude; a rectangle all the way round holds any longitudes.</summary>
    public bool Holds(GeoRect r) => r.South >= South && r.North <= North &&
                                    (Width >= 360.0 - 1e-9 || Contains(r.Near(CentreLon)));

    /// <summary>The same rectangle a whole number of turns east or west, nearest <paramref name="lon"/>.</summary>
    public GeoRect Near(double lon)
    {
        double turns = Math.Round((lon - CentreLon) / 360.0) * 360.0;
        return turns == 0 ? this : new GeoRect(West + turns, South, East + turns, North);
    }

    /// <summary>cos of the latitude nearest the equator: how wide a degree of longitude gets anywhere in it.</summary>
    public double MaxCosLat => South <= 0 && North >= 0 ? 1.0
        : Math.Cos(Math.Min(Math.Abs(South), Math.Abs(North)) * Math.PI / 180.0);

    public bool Equals(GeoRect r) => r.West == West && r.South == South && r.East == East && r.North == North;

    public override string ToString() => $"{South:0.0}..{North:0.0}°N {West:0.0}..{East:0.0}°E";
}

/// <summary>
/// The live map the overlay draws into: a map control (<c>MapBase</c>) whose <c>RenderedLayer</c> shows the picture
/// of a map camera (<c>MapCamera</c>), which renders the globe of its map scene (<c>MapScene</c>). Shared by every
/// overlay layer. Several maps can be open (the second map, a pop-out); the overlay goes into the largest.
/// </summary>
internal sealed class MapSceneRef : OverlayTarget
{
    private const int Samples = 9;                   // per side of the view, for its lon/lat bounds

    private MapCamera candidate;
    private float candidateArea, nextChoice;

    public MapScene Scene { get; private set; }
    public MapCamera Camera { get; private set; }

    /// <summary>Every frame from each map control that shows a live map (the Noesis map hook).</summary>
    public void Offer(RenderedLayer rendered)
    {
        MapCamera camera = rendered.Camera;
        if (camera == null || camera.MapTexture == null)
            return;
        float area = rendered.ActualWidth * rendered.ActualHeight;
        if (candidate == null || area > candidateArea || candidate == camera)
        {
            candidate = camera;
            candidateArea = area;
        }
    }

    /// <summary>Takes the largest map offered lately; false while there is none.</summary>
    public override bool Find()
    {
        if (Scene != null && Camera == null)
            Forget();                                  // destroyed with its mission or window
        if (Time.unscaledTime >= nextChoice)
        {
            nextChoice = Time.unscaledTime + 1f;
            if (candidate != null && candidate != Camera)
            {
                MapScene scene = candidate.GetComponentInParent<MapScene>();
                if (scene != null)
                {
                    Camera = candidate;
                    Scene = scene;
                    Generation++;
                }
            }
            candidate = null;
            candidateArea = 0f;
        }
        return Scene != null && Camera != null;
    }

    /// <summary>
    /// What the map shows: a lon/lat rectangle round the view (west and east unwrapped around its centre; the whole
    /// way round once a pole or the globe's edge is in view), its scale in degrees of latitude per pixel of the map
    /// texture at the centre, and the part of the globe in view.
    /// </summary>
    public override bool TryView(out GeoRect view, out double viewDpp, out GlobeCap cap)
    {
        view = default;
        viewDpp = 0;
        cap = default;
        if (!Find())
            return false;
        Camera cam = Camera.RenderCamera;
        RenderTexture target = Camera.MapTexture;
        if (cam == null || target == null || target.height < 2)
            return false;
        Transform t = Camera.transform;
        Vector3 centre = t.localPosition.normalized, right = t.localRotation * Vector3.right, up = t.localRotation * Vector3.up;
        float half = cam.orthographicSize, aspect = (float)target.width / target.height;
        viewDpp = 2.0 * half / target.height * 180.0 / Math.PI;

        double corner = half * Math.Sqrt(1.0 + aspect * aspect);
        cap = new GlobeCap(centre, corner >= 1.0 ? 90.0 : Math.Asin(corner) * 180.0 / Math.PI);

        GlobeMesh.ToGeo(centre, out double lat0, out double lon0);
        double south = lat0, north = lat0, west = lon0, east = lon0;
        bool edge = false;
        for (int a = 0; a < Samples; a++)
        {
            for (int b = 0; b < Samples; b++)
            {
                float dx = (2f * a / (Samples - 1) - 1f) * half * aspect, dy = (2f * b / (Samples - 1) - 1f) * half;
                float r2 = dx * dx + dy * dy;
                Vector3 p;
                if (r2 < 1f)
                {
                    p = centre * Mathf.Sqrt(1f - r2) + right * dx + up * dy;
                }
                else
                {
                    // Off the globe: the edge of the disc in that direction.
                    edge = true;
                    float r = Mathf.Sqrt(r2);
                    p = right * (dx / r) + up * (dy / r);
                }
                GlobeMesh.ToGeo(p, out double lat, out double lon);
                lon += Math.Round((lon0 - lon) / 360.0) * 360.0;
                south = Math.Min(south, lat);
                north = Math.Max(north, lat);
                west = Math.Min(west, lon);
                east = Math.Max(east, lon);
            }
        }
        // A pole in view: everything round it is too.
        for (int pole = -1; pole <= 1; pole += 2)
        {
            Vector3 v = new Vector3(0f, pole, 0f);
            float along = Vector3.Dot(v, centre), x = Vector3.Dot(v, right), y = Vector3.Dot(v, up);
            if (along > 0f && Math.Abs(x) <= half * aspect && Math.Abs(y) <= half)
            {
                if (pole > 0)
                    north = 90.0;
                else
                    south = -90.0;
                edge = true;
            }
        }
        if (edge)
        {
            west = lon0 - 180.0;
            east = lon0 + 180.0;
        }
        else
        {
            // The bounds of a curved outline lie between the samples: a little more all round.
            double padLon = (east - west) * 0.03, padLat = (north - south) * 0.03;
            west -= padLon;
            east += padLon;
            south = Math.Max(-90.0, south - padLat);
            north = Math.Min(90.0, north + padLat);
        }
        view = new GeoRect(west, south, east, north);
        return true;
    }

    /// <summary>The map's day/night state, the shown palette's (in Auto: at the view's centre).</summary>
    public override bool Dark => Scene != null && GameApi.IsDark(Scene.GetComponent<MapThemeSelector>()?.Palette);

    public override void Forget()
    {
        Scene = null;
        Camera = null;
        candidate = null;
    }
}

/// <summary>
/// One overlay layer. The stock map is a Unity scene – a unit globe of lon/lat tiles – that the map camera renders
/// into the texture the map window shows; one more mesh in that scene, laid exactly on the stock tiles
/// (<see cref="GlobeMesh"/>) and drawn after them, carries our image and so turns and zooms with the globe for free.
/// On a flat map (the mission editor's, <see cref="FlatMapRef"/>) the image is a picture laid on its lon/lat
/// rectangle among the map's background elements instead.
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
    private const int MaxLayerTiles = 40_000;        // further out the thermal layer is left off (main-thread work)
    private const double MinCosLat = 0.05;           // a pixel is at most 20 times wider (in degrees) than high

    private enum Phase { Idle, Filling, Shading }

    private sealed class Plan
    {
        /// <summary>North-west corner, degrees of latitude and of longitude per pixel, the view's scale.</summary>
        public double West, North, Dpp, DppX, ViewDpp;
        /// <summary>Margin around the view the image was planned with: share of the view, and in degrees.</summary>
        public double Margin, MarginLon, MarginLat;
        public int Width, Height;
        public bool Dem;
        public float Interval, Danger;
        /// <summary>Selected submarine's depth (NaN = none), and whether the thermal layer is drawn for it.</summary>
        public float Keel = float.NaN;
        public bool Layer;
        public bool Dark;
        public string ThemeId;
        public int SettingsVersion;
        public double South => North - Height * Dpp;
        public double East => West + Width * DppX;
        public GeoRect Rect => new GeoRect(West, South, East, North);
        public Plan Clone() => (Plan)MemberwiseClone();
    }

    private readonly OverlayTarget map;
    private readonly string name;
    private readonly float zOffset;
    private int generation = -1;

    // A pre-drawn layer: its area, scale, and whether its scale is fine enough to read the DEM.
    private GeoRect? area;
    private double areaDpp, areaDppX;
    private bool areaDem;

    private GameObject quad;
    private MeshRenderer quadRenderer;
    // On a flat map: the picture, and the texture it shows.
    private Image image;
    private Texture imageTexture;
    private bool imageShown;
    private Mesh mesh;
    private Material material;
    private Texture2D texture;
    private bool shaderMissing;
    private readonly List<GeoRect> holes = new List<GeoRect>();
    private bool meshDirty = true, meshEmpty;
    // The part of the globe the mesh was made for, with a margin; it is made again once the view leaves that.
    private GlobeCap meshCap;
    private double meshRadius;
    private readonly List<Vector3> meshVertices = new List<Vector3>();
    private readonly List<Vector2> meshUvs = new List<Vector2>();
    private readonly List<int> meshTriangles = new List<int>();

    private Phase phase;
    private readonly SourceGrid source = new SourceGrid();
    private int fillRow, fillX0, fillY0, fillStep, fillStepX;
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
    private static Theme sharedTheme;

    /// <param name="name">For the log.</param>
    /// <param name="zOffset">Depth in the map scene: the stock tiles are at 0, the background at 0.1, nearer is in front.</param>
    public OverlayRenderer(OverlayTarget map, string name, float zOffset)
    {
        this.map = map;
        this.name = name;
        this.zOffset = zOffset;
    }

    /// <summary>Palettes, contour interval and highlight altitude of the image on screen, for the legend.</summary>
    public Palette Palette => palette;
    /// <summary>Depth bands of the sea; null while the sea is left to the stock map.</summary>
    public Palette SeaPalette => seaPalette;
    /// <summary>The theme of the palette in use (null before the first draw).</summary>
    public Theme Theme => palette?.Theme;
    public float ShownInterval => shownPlan?.Interval ?? 0f;
    public float ShownDanger => shownPlan?.Danger ?? float.NaN;
    public float ShownKeel => shownPlan?.Keel ?? float.NaN;
    public bool ShownLayer => shownPlan?.Layer ?? false;
    public bool HasImage => shown != null && (quad != null || image != null);

    /// <summary>Copying heights out of the DEM right now (the main-thread part of a redraw).</summary>
    public bool Filling => phase == Phase.Filling;
    /// <summary>On the map this frame.</summary>
    public bool Showing => quadRenderer != null ? quadRenderer.enabled : image != null && imageShown;
    /// <summary>Where the image on the map lies.</summary>
    public GeoRect ShownRect => shownPlan?.Rect ?? default;

    /// <summary>The fixed area of a pre-drawn layer (null for the live layer).</summary>
    public GeoRect? Area => area;
    /// <summary>Degrees per pixel of a pre-drawn layer.</summary>
    public double AreaDpp => areaDpp;

    /// <summary>
    /// Makes this a pre-drawn layer of a fixed area at a fixed scale (degrees of latitude per pixel; a pixel spans
    /// as many degrees of longitude as it takes to be as wide as high where the area is widest).
    /// </summary>
    public void SetArea(GeoRect rect, double dpp, bool dem)
    {
        area = rect;
        areaDpp = dpp;
        areaDppX = dpp / Math.Max(MinCosLat, rect.MaxCosLat);
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
    /// <param name="keel">Selected submarine's depth in metres: the thermal layer and water shallower than this
    /// are drawn; NaN = no submarine (always for pre-drawn layers).</param>
    /// <param name="mayStart">May use this frame's main-thread budget (another layer isn't using it).</param>
    public void Tick(bool on, bool show, bool dark, float danger, float keel, bool mayStart)
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
            image = null;
            imageTexture = null;
        }
        if (!on)
        {
            if (quadRenderer != null)
                quadRenderer.enabled = false;
            ShowImage(false);
            if (phase != Phase.Idle)
                Abort();
            return;
        }
        if (shaderMissing || !map.Find() || !(map is FlatMapRef ? EnsureImage() : EnsureQuad()))
            return;
        EnsurePalette(dark);

        bool haveView = map.TryView(out GeoRect view, out double viewDpp, out GlobeCap cap);
        if (haveView)
        {
            TrackView(view);
            if (!meshDirty && shownPlan != null && LeftCap(cap))
                meshDirty = true;
        }
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
                    StartIfStale(view, viewDpp, dark, danger, keel);
                break;
        }
        if (quadRenderer == null)
        {
            ShowImage(show && shown != null);
            return;
        }
        if (meshDirty && haveView)
            BuildMesh(cap);
        quadRenderer.enabled = show && shown != null && !meshEmpty;
    }

    private bool EnsureImage()
    {
        if (image == null)
        {
            image = ((FlatMapRef)map).AddImage();
            imageTexture = null;
            imageShown = false;
        }
        return true;
    }

    private void ShowImage(bool show)
    {
        if (image == null)
            return;
        if (show && texture != null && !ReferenceEquals(imageTexture, texture))
        {
            // A new texture (the size changed): Noesis shows a Unity texture through a source wrapping it.
            imageTexture = texture;
            image.Source = new TextureSource(texture);
        }
        show &= imageTexture != null;
        if (show != imageShown)
        {
            imageShown = show;
            image.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
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
        if (image != null && map is FlatMapRef flat)
            flat.RemoveImage(image);
        image = null;
        imageTexture = null;
        imageShown = false;
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

        // The globe is centred on the scene's origin with radius 1: the mesh needs no transform of its own.
        quad = new GameObject($"SeaPowerTopo {name}") { layer = MapLayer };
        quad.transform.SetParent(((MapSceneRef)map).Scene.transform, false);
        mesh = new Mesh { name = $"SeaPowerTopo {name}" };
        meshDirty = true;
        quad.AddComponent<MeshFilter>().sharedMesh = mesh;
        quadRenderer = quad.AddComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = material;
        quadRenderer.renderingLayerMask = 1u << MapLayer;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        quadRenderer.enabled = false;
        return true;
    }

    /// <summary>
    /// The image minus the holes (see <see cref="Cutout"/>), on the part of the globe in view with a margin as wide
    /// as the view again (at least 2°), so a pan only makes it again once it has gone that far.
    /// </summary>
    private void BuildMesh(GlobeCap cap)
    {
        meshDirty = false;
        if (mesh == null || shownPlan == null)
            return;
        GeoRect r = shownPlan.Rect;
        var cut = new List<UnitRect>(holes.Count);
        foreach (GeoRect hole in holes)
        {
            // Each layer counts its longitudes in its own turn: a hole past the date line of this image (the world's
            // -180..180 under a view across 180°) lies partly a turn east or west of it, so it is cut in all three.
            for (int turn = -1; turn <= 1; turn++)
            {
                double west = hole.West + turn * 360.0, east = hole.East + turn * 360.0;
                if (east <= r.West || west >= r.East)
                    continue;
                cut.Add(new UnitRect(Unit(west, r.West, r.Width), Unit(hole.South, r.South, r.Height),
                                     Unit(east, r.West, r.Width), Unit(hole.North, r.South, r.Height)));
            }
        }
        var parts = new List<GeoRect>();
        foreach (UnitRect part in Cutout.Rects(cut))
            parts.Add(new GeoRect(r.West + part.X0 * r.Width, r.South + part.Y0 * r.Height,
                                  r.West + part.X1 * r.Width, r.South + part.Y1 * r.Height));

        meshCap = cap;
        meshRadius = Math.Min(90.0, cap.Radius * 2.0 + 2.0);
        GlobeMesh.Build(r, parts, cap.Centre, meshRadius, meshVertices, meshUvs, meshTriangles);

        mesh.Clear();
        mesh.indexFormat = meshVertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(meshVertices);
        mesh.SetUVs(0, meshUvs);
        var colors = new Color32[meshVertices.Count];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = new Color32(255, 255, 255, 255);
        mesh.colors32 = colors;
        mesh.SetTriangles(meshTriangles, 0);
        mesh.RecalculateBounds();
        meshEmpty = meshTriangles.Count == 0;
    }

    // The view has gone beyond the margin the mesh was made with, or zoomed out past it, or in so far that the mesh
    // carries far more than it needs. With the whole globe in view the mesh is the facing half: a turn of up to 5°
    // leaves out only the last degrees at the globe's edge, where they are squeezed into a pixel or two.
    private bool LeftCap(GlobeCap cap)
    {
        double apart = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Vector3.Dot(cap.Centre, meshCap.Centre)))) * 180.0 / Math.PI;
        return apart + Math.Min(cap.Radius, 85.0) > meshRadius || cap.Radius * 6.0 + 4.0 < meshRadius;
    }

    private static float Unit(double v, double origin, double size) =>
        (float)Math.Max(0.0, Math.Min(1.0, (v - origin) / size));

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
        // The theme the user chose for this map theme (day or night).
        Theme theme = Themes.Get(dark ? Settings.NightLook.Value : Settings.DayLook.Value, dark);
        if (sharedLand == null || sharedVersion != Settings.Version || sharedDark != dark || sharedTheme != theme)
        {
            float[] limits = Palette.ParseLimits(Settings.Bands.Value);
            if (limits == null)
            {
                ModLog.Warn($"Height bands '{Settings.Bands.Value}' unusable (ascending metres from 0) - using the default.");
                limits = Palette.DefaultLimits;
            }
            sharedLand = new Palette(limits, theme);
            sharedSea = null;
            if (Settings.SeaDepth.Value || theme.IsChart)
            {
                float[] depths = Palette.ParseLimits(Settings.DepthBands.Value);
                if (depths == null)
                {
                    ModLog.Warn($"Depth bands '{Settings.DepthBands.Value}' unusable (ascending metres from 0) - using the default.");
                    depths = Palette.DefaultDepthLimits;
                }
                sharedSea = new Palette(depths, theme, sea: true);
            }
            sharedVersion = Settings.Version;
            sharedDark = dark;
            sharedTheme = theme;
        }
        palette = sharedLand;
        seaPalette = sharedSea;
    }

    private void StartIfStale(GeoRect view, double viewDpp, bool dark, float danger, float keel)
    {
        bool layer = !float.IsNaN(keel) && !LayerMap.Disabled;
        bool covered = shownPlan != null && Covers(shownPlan, view);
        double zoom = shownPlan != null ? shownPlan.ViewDpp / viewDpp : 1.0;
        bool nearEdge = covered && NearEdge(shownPlan, view);
        // The layer tiles are worked out with the heights on the main thread: taking it up needs a full redraw.
        bool geometryStale = shown == null || !covered || shownPlan.SettingsVersion != Settings.Version ||
                             zoom > 1.45 || zoom < 0.7 || nearEdge || shownPlan.Layer != layer ||
                             shownPlan.ThemeId != palette.Theme.Id || DemArrived();
        if (geometryStale)
        {
            // Let a zoom settle first, unless the view has run off the image. A pan that has used up most of the
            // margin redraws at once, so the new image is there before the view reaches the edge.
            bool panning = nearEdge && zoom < 1.15 && zoom > 0.87;
            if (covered && !panning && Time.unscaledTime - lastViewChange < SettleSeconds)
                return;
            StartFull(view, viewDpp, dark, danger, keel, layer);
            return;
        }
        if (shownPlan.Dark != dark || DangerMoved(shownPlan.Danger, danger) || KeelMoved(shownPlan.Keel, keel))
            StartRecolour(dark, danger, keel);
    }

    private void StartFixedIfStale(bool dark, bool mayStart)
    {
        if (!mayStart)
            return;
        GeoRect a = area.Value;
        bool sameArea = shownPlan != null && shownPlan.West == a.West && shownPlan.North == a.North && shownPlan.Dpp == areaDpp &&
                        shownPlan.DppX == areaDppX;
        if (shown != null && sameArea && shownPlan.SettingsVersion == Settings.Version && shownPlan.Dark == dark &&
            shownPlan.ThemeId == palette.Theme.Id && !DemArrived())
        {
            return;
        }
        var plan = new Plan
        {
            West = a.West,
            North = a.North,
            Dpp = areaDpp,
            DppX = areaDppX,
            ViewDpp = areaDpp,
            Width = Math.Max(2, (int)Math.Ceiling(a.Width / areaDppX)),
            Height = Math.Max(2, (int)Math.Ceiling(a.Height / areaDpp)),
            Dem = areaDem,
            Dark = dark,
            ThemeId = palette.Theme.Id,
            Danger = float.NaN,
            SettingsVersion = Settings.Version,
        };
        plan.Interval = ContourInterval(areaDpp);
        Begin(plan);
    }

    private static bool Covers(Plan p, GeoRect view)
    {
        const double eps = 1e-9;
        view = view.Near((p.West + p.East) / 2);
        return view.West >= p.West - eps && view.East <= p.East + eps &&
               (view.North <= p.North + eps || p.North >= 90.0 - eps) &&
               (view.South >= p.South - eps || p.South <= -90.0 + eps);
    }

    // Less than PrefetchAt of the planned margin left on some side (an edge at a pole doesn't count).
    private static bool NearEdge(Plan p, GeoRect view)
    {
        const double eps = 1e-9;
        view = view.Near((p.West + p.East) / 2);
        double lon = p.MarginLon * PrefetchAt, lat = p.MarginLat * PrefetchAt;
        if (view.West - p.West < lon || p.East - view.East < lon)
            return true;
        if (p.North < 90.0 - eps && p.North - view.North < lat)
            return true;
        return p.South > -90.0 + eps && view.South - p.South < lat;
    }

    private static bool KeelMoved(float shownKeel, float keel)
    {
        bool shownOn = !float.IsNaN(shownKeel), on = !float.IsNaN(keel);
        if (shownOn != on)
            return true;
        return on && Math.Abs(shownKeel - keel) > Math.Max(3f, 0.03f * keel);
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
            if (TerrainLoader.IsDEMChunkAvailable(key) || DemCache.Has(key))
                return true;
        }
        return false;
    }

    private float ContourInterval(double dpp) =>
        !Settings.Contours.Value ? 0f
        : Settings.ContourInterval.Value > 0 ? Settings.ContourInterval.Value
        : Raster.AutoInterval(dpp * Raster.MetresPerDegree * palette.Theme.LandIntervalScale);

    private void StartFull(GeoRect view, double viewDpp, bool dark, float danger, float keel, bool layer)
    {
        // On the globe a degree of longitude is cos(latitude) as wide as one of latitude: pixels that many degrees
        // wider (where the view is widest) are as wide on screen as high.
        double dpp = viewDpp / Settings.Resolution.Value, stretch = 1.0 / Math.Max(MinCosLat, view.MaxCosLat);
        double spanLon = Math.Min(360.0, view.Width), spanLat = view.Height;
        double margin = (Math.Sqrt(MaxPixels / (spanLon / (dpp * stretch) * (spanLat / dpp))) - 1.0) / 2.0;
        margin = Math.Min(margin, (MaxSide * dpp / Math.Max(spanLon / stretch, spanLat) - 1.0) / 2.0);
        margin = Math.Max(MinMargin, Math.Min(MaxMargin, margin));
        double marginLon = Math.Min(spanLon * margin, (360.0 - spanLon) / 2);
        double centre = (view.West + view.East) / 2;
        double west = centre - spanLon / 2 - marginLon, east = centre + spanLon / 2 + marginLon;
        double north = Math.Min(90.0, view.North + spanLat * margin), south = Math.Max(-90.0, view.South - spanLat * margin);
        int width = (int)Math.Ceiling((east - west) / (dpp * stretch)), height = (int)Math.Ceiling((north - south) / dpp);
        double shrink = Math.Max(Math.Sqrt((double)width * height / MaxPixels), Math.Max(width / (double)MaxSide, height / (double)MaxSide));
        if (shrink > 1.0)
        {
            dpp *= shrink;
            width = (int)Math.Ceiling((east - west) / (dpp * stretch));
            height = (int)Math.Ceiling((north - south) / dpp);
        }

        var plan = new Plan
        {
            West = west,
            North = north,
            Dpp = dpp,
            DppX = (east - west) / Math.Max(2, width),
            ViewDpp = viewDpp,
            Margin = margin,
            MarginLon = marginLon,
            MarginLat = spanLat * margin,
            Width = Math.Max(2, width),
            Height = Math.Max(2, height),
            Dem = dpp < DemLimitDpp,
            Dark = dark,
            ThemeId = palette.Theme.Id,
            Danger = danger,
            Keel = keel,
            Layer = layer,
            SettingsVersion = Settings.Version,
        };
        plan.Interval = ContourInterval(viewDpp);
        Begin(plan);
    }

    // Source grid: DEM samples (every n-th when zoomed out) or world grid texels, with two cells of margin around
    // the image for the interpolation. Then the main-thread fill starts.
    private void Begin(Plan plan)
    {
        double dpp = plan.Dpp, dppX = plan.DppX, west = plan.West, north = plan.North;
        if (plan.Dem)
        {
            fillStep = Math.Max(1, (int)Math.Round(dpp * HeightReader.DemPerDegree));
            fillStepX = Math.Max(1, (int)Math.Round(dppX * HeightReader.DemPerDegree));
            fillX0 = (int)Math.Floor((west + 180.0) * HeightReader.DemPerDegree) - 2 * fillStepX;
            fillY0 = (int)Math.Floor((90.0 - north) * HeightReader.DemPerDegree) - 2 * fillStep;
            source.Lon0 = -180.0 + fillX0 / HeightReader.DemPerDegree;
            source.Lat0 = 90.0 - fillY0 / HeightReader.DemPerDegree;
            source.Cell = fillStep / HeightReader.DemPerDegree;
            source.CellX = fillStepX / HeightReader.DemPerDegree;
        }
        else
        {
            fillStep = Math.Max(1, (int)Math.Round(dpp / HeightReader.LowCell));
            fillStepX = Math.Max(1, (int)Math.Round(dppX / HeightReader.LowCell));
            fillX0 = (int)Math.Floor((west + 180.0) / HeightReader.LowCell - 0.5) - 2 * fillStepX;
            fillY0 = (int)Math.Floor((90.0 - north) / HeightReader.LowCell - 0.5) - 2 * fillStep;
            source.Lon0 = -180.0 + (fillX0 + 0.5) * HeightReader.LowCell;
            source.Lat0 = 90.0 - (fillY0 + 0.5) * HeightReader.LowCell;
            source.Cell = fillStep * HeightReader.LowCell;
            source.CellX = fillStepX * HeightReader.LowCell;
        }
        source.Allocate((int)Math.Ceiling((plan.East - source.Lon0) / source.CellX) + 3,
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

    private void StartRecolour(bool dark, float danger, float keel)
    {
        Plan plan = shownPlan.Clone();
        plan.Dark = dark;
        plan.Danger = danger;
        plan.Keel = keel;
        building = TakeJob();
        Configure(building, plan);
        building.Heights = shown.Heights;
        building.Layer = shown.Layer;
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
        job.DppX = plan.DppX;
        job.Palette = palette;
        job.Sea = seaPalette;
        // A theme covers the stock map as much as it wants at the default setting (0.7); the setting scales that.
        job.Opacity = palette.Theme.Legacy ? Settings.Opacity.Value
            : Math.Min(1f, palette.Theme.Opacity * Settings.Opacity.Value / 0.7f);
        job.ChartSafety = Settings.ChartSafety.Value;
        job.Hillshade = Settings.Hillshade.Value;
        job.Interval = plan.Interval;
        job.Danger = plan.Danger;
        job.Keel = plan.Keel;
        job.Layer = null;
    }

    private void ContinueFill()
    {
        var slice = Stopwatch.StartNew();
        using (var reader = new HeightReader(buildingMissing))
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
        {
            if (buildingPlan.Layer)
                building.Layer = LayerTilesFor(buildingPlan);
            StartWorker(resample: true);
        }
    }

    /// <summary>
    /// The thermal layer over the image, tile by tile (main thread: the game's cache and random generator). Whether
    /// a tile's water is deep enough comes from the heights just copied.
    /// </summary>
    private LayerTiles LayerTilesFor(Plan plan)
    {
        int x0 = Raster.LayerTileX(plan.West), nx;
        if (plan.East - plan.West >= 359.0)
        {
            x0 = 0;
            nx = Raster.LayerTilesX;
        }
        else
        {
            nx = Raster.LayerTileX(plan.East) - x0 + 1;
            if (nx <= 0)
                nx += Raster.LayerTilesX;           // across the date line
        }
        int y0 = Raster.LayerTileY(plan.North), ny = Raster.LayerTileY(plan.South) - y0 + 1;
        if ((long)nx * ny > MaxLayerTiles || ny <= 0)
        {
            ModLog.Debug($"Thermal layer left off: {nx}x{ny} tiles in view");
            return null;
        }
        // One tile more all round: the soft edges of the layer blend with the neighbours of the tiles in view.
        if (nx < Raster.LayerTilesX)
        {
            x0 = (x0 + Raster.LayerTilesX - 1) % Raster.LayerTilesX;
            nx = Math.Min(Raster.LayerTilesX, nx + 2);
        }
        y0 -= 1;
        ny += 2;
        var tiles = new LayerTiles { X0 = x0, Y0 = y0, Nx = nx, Ny = ny, Depth = new float[nx * ny] };
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
                tiles.Depth[j * nx + i] = LayerMap.Depth((x0 + i) % Raster.LayerTilesX, y0 + j, SeaDepthFromSource);
        }
        return tiles;
    }

    // Water depth at the nearest sample of the heights just copied (0 on land).
    private float SeaDepthFromSource(double lon, double lat)
    {
        double span = source.Nx * source.CellLon;
        while (lon < source.Lon0)
            lon += 360.0;
        while (lon > source.Lon0 + span && lon - 360.0 >= source.Lon0)
            lon -= 360.0;
        int i = (int)Math.Round((lon - source.Lon0) / source.CellLon), j = (int)Math.Round((source.Lat0 - lat) / source.Cell);
        i = Math.Max(0, Math.Min(source.Nx - 1, i));
        j = Math.Max(0, Math.Min(source.Ny - 1, j));
        float v = source.V[j * source.Nx + i];
        return v < 0f ? -v : 0f;
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
                int gx = fillX0 + i * fillStepX;
                v[o + i] = reader.TryDem(gx, gy, out short raw)
                    ? reader.Sim(raw)
                    : reader.Sim(reader.LowAt(-180.0 + gx / HeightReader.DemPerDegree, lat));
            }
        }
        else
        {
            int ly = fillY0 + j * fillStep;
            for (int i = 0; i < nx; i++)
                v[o + i] = reader.Sim(reader.Low(fillX0 + i * fillStepX, ly));
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
            if (material != null)
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
        spare = shown;
        shown = job;
        shownPlan = buildingPlan;
        shownMissing = buildingMissing;
        meshDirty = true;
        if (image != null)
            FlatMapRef.Place(image, shownPlan.Rect);
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
