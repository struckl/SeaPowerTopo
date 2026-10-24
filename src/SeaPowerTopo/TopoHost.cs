using System;
using System.Collections.Generic;
using MapControl;
using SeaPower;
using SeaPower.LiveMap;
using SeapowerUI;
using SeaPowerTopo.Render;
using SeaPowerTopo.Terrain;
using SeaPowerTopo.Ui;
using UnityEngine;

namespace SeaPowerTopo;

/// <summary>
/// Drives the mod: applies the late map hook once NoesisGUI runs, and during a mission keeps the Layers-menu entry,
/// the toggle key, the overlay, and the legend and cursor readout of every live map in step.
/// <para>The overlay is several layers. At mission start the mission area (every unit of every side, plus as much
/// again around it) is drawn once, then two areas four times larger each at a quarter of the detail, then the whole
/// world. Whichever of them shows the view at about its own scale is all the map needs – zoomed out nothing is
/// drawn while playing. Closer in, or while an aircraft's terrain highlight is on, the live layer draws the view on
/// top, with the pre-drawn layers filling in around it while it catches up with a pan.</para>
/// </summary>
internal sealed class TopoHost : MonoBehaviour
{
    public static TopoHost Instance { get; private set; }

    private const float MinClearanceMetres = 30f;     // closer to the ground than this counts as landed / on deck

    // Pre-drawn layers: pixels per layer, how far one may be zoomed in on (stretched) or out of (shrunk) and still
    // stand in for the live layer, and the world layer's scale.
    private const double LayerPixels = 8_000_000;
    private const double MaxStretch = 1.4, MaxShrink = 3.0;
    private const double WorldDpp = 0.1;
    private const double MinMissionSpan = 4.0;         // degrees

    private enum HookState { Waiting, Applied, Failed }

    private readonly TopoLayer layer = new TopoLayer();
    private readonly MapSceneRef map = new MapSceneRef();
    private readonly OverlayRenderer live;
    private readonly List<OverlayRenderer> drawn = new List<OverlayRenderer>();    // pre-drawn, finest first
    private readonly List<GeoRect> front = new List<GeoRect>();
    private OverlayRenderer legendSource;
    private bool drawnPlanned;
    private float nextPlanTry;
    private readonly List<MapDecoration> maps = new List<MapDecoration>();
    private readonly HashSet<MapBase> undecoratable = new HashSet<MapBase>();

    private HookState mapHooks;
    private float nextHookCheck, nextPrune, nextAircraftCheck;
    private bool missionActive;
    private int errors;

    private string aircraftName;
    private float aircraftAltitude = float.NaN;

    public TopoHost() => live = new OverlayRenderer(map, "live", -0.05f);

    private void Awake() => Instance = this;

    private void Update()
    {
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            // Log the first few, then stay quiet; a broken frame must never break the game's.
            if (errors++ < 5)
                ModLog.Error($"Update failed: {ex}");
        }
    }

    private void Tick()
    {
        if (mapHooks == HookState.Waiting && Time.unscaledTime >= nextHookCheck)
        {
            nextHookCheck = Time.unscaledTime + 1f;
            // A WindowHost registers itself with WindowHostManager when it is built, which only happens while
            // NoesisGUI runs (the main menu view has one) – from then on Noesis types are safe to touch.
            if (Singleton<WindowHostManager>.InstanceExists())
                ApplyMapHooks();
        }
        if (!missionActive)
            return;

        layer.EnsureRegistered();
        if (Settings.ToggleKey.Value.IsDown())
        {
            layer.Toggle();
            ModLog.Info($"Topography {(layer.Visible ? "on" : "off")}.");
        }

        bool on = layer.Visible;
        bool dark = Singleton<ThemeTracker>.InstanceExists() && Singleton<ThemeTracker>.Instance.CurrentTheme == ThemeMode.Dark;
        UpdateAircraft();
        float danger = Settings.AircraftClearance.Value ? aircraftAltitude : float.NaN;
        if (on && Settings.Predraw.Value && !drawnPlanned && Time.unscaledTime >= nextPlanTry)
        {
            nextPlanTry = Time.unscaledTime + 1f;
            drawnPlanned = PlanDrawnLayers();
        }
        TickLayers(on, dark, danger);

        if (Time.unscaledTime >= nextPrune)
        {
            nextPrune = Time.unscaledTime + 2f;
            for (int i = maps.Count - 1; i >= 0; i--)
            {
                if (!maps[i].Alive)
                {
                    maps[i].Detach();
                    maps.RemoveAt(i);
                }
            }
        }
        ReadoutMode mode = Settings.Readout.Value;
        bool readout = mode == ReadoutMode.Always || (mode == ReadoutMode.WithOverlay && on);
        string highlighted = live.Showing && !float.IsNaN(live.ShownDanger) ? aircraftName : null;
        foreach (MapDecoration decoration in maps)
            decoration.Update(on, readout, legendSource, highlighted);
    }

    /// <summary>
    /// Which layers show and which draw: the finest pre-drawn layer that has the whole view at about its own scale
    /// makes the live layer unnecessary (unless the aircraft highlight needs it); every layer leaves out what the
    /// layers in front of it show. One layer at a time copies heights on the main thread, the live one first.
    /// </summary>
    private void TickLayers(bool on, bool dark, float danger)
    {
        bool haveView = map.TryView(out GeoRect view, out double viewDpp);
        // Beyond the poles there is nothing to draw.
        view = new GeoRect(view.West, Math.Max(-90.0, view.South), view.East, Math.Min(90.0, view.North));
        bool predraw = on && Settings.Predraw.Value;
        OverlayRenderer primary = null;
        var showing = new bool[drawn.Count];
        for (int i = 0; i < drawn.Count; i++)
        {
            OverlayRenderer layer = drawn[i];
            showing[i] = predraw && haveView && layer.HasImage && viewDpp <= layer.AreaDpp * MaxShrink;
            if (showing[i] && primary == null && layer.Area.Value.Contains(view) && viewDpp >= layer.AreaDpp / MaxStretch)
                primary = layer;
        }

        bool liveOn = on && (primary == null || !float.IsNaN(danger));
        live.Tick(liveOn, liveOn, dark, danger, mayStart: true);
        bool busy = live.Filling;
        front.Clear();
        if (live.Showing)
            front.Add(live.ShownRect);
        OverlayRenderer firstShowing = null;
        for (int i = 0; i < drawn.Count; i++)
        {
            OverlayRenderer layer = drawn[i];
            layer.SetHoles(front);
            layer.Tick(predraw, showing[i], dark, float.NaN, mayStart: !busy);
            busy |= layer.Filling;
            if (layer.Showing)
            {
                front.Add(layer.ShownRect);
                firstShowing ??= layer;
            }
        }
        legendSource = live.Showing ? live : primary ?? firstShowing ?? live;
    }

    /// <summary>
    /// The pre-drawn layers for this mission: the area of every unit of every side plus half its size on each side
    /// (at least <see cref="MinMissionSpan"/>°), at up to four times the DEM's detail; around it two areas four
    /// times larger at a quarter of the detail each; and the whole world. False while no unit is known yet.
    /// </summary>
    private bool PlanDrawnLayers()
    {
        if (!map.Find())
            return false;
        var lons = new List<double>();
        var lats = new List<double>();
        foreach (Taskforce side in new[] { Globals._playerTaskforce, Globals._enemyTaskforce, Globals._alliedTaskforce,
                                           Globals._neutralTaskforce, Globals._secondEnemyTaskforce })
        {
            if (side == null)
                continue;
            foreach (ObjectBase unit in side._taskforceUnits)
                AddPosition(unit, lons, lats);
            foreach (ObjectBase unit in side._taskforceDisabledUnits)
                AddPosition(unit, lons, lats);
        }
        if (lons.Count == 0)
            return false;

        double west = Min(lons), east = Max(lons);
        if (east - west > 180.0)
        {
            // Across the date line: count western longitudes on from 180.
            for (int i = 0; i < lons.Count; i++)
                if (lons[i] < 0)
                    lons[i] += 360.0;
            west = Min(lons);
            east = Max(lons);
        }
        double south = Min(lats), north = Max(lats);
        double cx = (west + east) / 2, cy = (south + north) / 2;
        double halfW = Math.Max(MinMissionSpan, (east - west) * 2) / 2, halfH = Math.Max(MinMissionSpan / 2, (north - south) * 2) / 2;
        double dpp = Math.Max(1.0 / HeightReader.DemPerDegree / 4, Math.Sqrt(4 * halfW * halfH / LayerPixels));

        float z = -0.045f;
        for (int k = 0; k < 3 && dpp < WorldDpp / 1.5; k++)
        {
            double hw = Math.Min(180.0, halfW), s = Math.Max(-90.0, cy - halfH), n = Math.Min(90.0, cy + halfH);
            var level = new OverlayRenderer(map, $"mission x{1 << (2 * k)}", z);
            level.SetArea(new GeoRect(cx - hw, s, cx + hw, n), dpp, dem: dpp < 1.5 / HeightReader.DemPerDegree);
            drawn.Add(level);
            ModLog.Info($"Pre-drawn layer {drawn.Count}: {new GeoRect(cx - hw, s, cx + hw, n)}, " +
                        $"{dpp * Raster.MetresPerDegree:0} m per pixel");
            halfW *= 4;
            halfH *= 4;
            dpp *= 4;
            z += 0.004f;
        }
        var world = new OverlayRenderer(map, "world", z);
        world.SetArea(new GeoRect(-180, -90, 180, 90), WorldDpp, dem: false);
        drawn.Add(world);
        ModLog.Info($"Pre-drawn layers for {lons.Count} units, plus the world at {WorldDpp * Raster.MetresPerDegree / 1000:0} km per pixel.");
        return true;
    }

    private static void AddPosition(ObjectBase unit, List<double> lons, List<double> lats)
    {
        if (unit == null)
            return;
        double lon = unit._geoPosition.Longitude, lat = unit._geoPosition.Latitude;
        if (double.IsNaN(lon) || double.IsNaN(lat) || (lon == 0 && lat == 0))
            return;
        lons.Add(lon);
        lats.Add(lat);
    }

    private static double Min(List<double> values)
    {
        double m = double.MaxValue;
        foreach (double v in values)
            m = Math.Min(m, v);
        return m;
    }

    private static double Max(List<double> values)
    {
        double m = double.MinValue;
        foreach (double v in values)
            m = Math.Max(m, v);
        return m;
    }

    private void ApplyMapHooks()
    {
        try
        {
            mapHooks = Patches.ApplyMapHooks() ? HookState.Applied : HookState.Failed;
        }
        catch (Exception ex)
        {
            mapHooks = HookState.Failed;
            ModLog.Error($"Map hook failed - no legend or cursor readout: {ex}");
        }
        if (mapHooks == HookState.Applied)
            ModLog.Info("Map hook in place (legend and cursor readout).");
    }

    internal void MissionReady()
    {
        try
        {
            missionActive = true;
            layer.EnsureRegistered();
        }
        catch (Exception ex)
        {
            ModLog.Error($"Mission start handling failed: {ex}");
        }
    }

    internal void MissionEnded()
    {
        try
        {
            missionActive = false;
            live.Shutdown();
            foreach (OverlayRenderer layer in drawn)
                layer.Shutdown();
            drawn.Clear();
            drawnPlanned = false;
            legendSource = null;
            map.Forget();
            foreach (MapDecoration map in maps)
                map.Detach();
            maps.Clear();
            undecoratable.Clear();
            aircraftName = null;
            aircraftAltitude = float.NaN;
        }
        catch (Exception ex)
        {
            ModLog.Error($"Mission end handling failed: {ex}");
        }
    }

    /// <summary>Every frame from each map control showing the live map: decorate the ones we haven't yet.</summary>
    internal void MapRendered(RenderedLayer rendered)
    {
        try
        {
            MapBase map = rendered.ParentMap;
            if (!missionActive || map == null || undecoratable.Contains(map))
                return;
            foreach (MapDecoration known in maps)
            {
                if (known.Map == map)
                    return;
            }
            MapDecoration decoration = MapDecoration.TryAttach(map, Describe);
            if (decoration == null)
            {
                undecoratable.Add(map);
                ModLog.Warn("Map template not as expected - no legend or cursor readout on this map.");
                return;
            }
            maps.Add(decoration);
            ModLog.Info("Legend and cursor readout attached to the map.");
        }
        catch (Exception ex)
        {
            if (errors++ < 5)
                ModLog.Error($"Map decoration failed: {ex}");
        }
    }

    private HeightSample Sample(double lon, double lat)
    {
        using (var reader = new HeightReader(map.Find() ? map.Lowres : null))
            return reader.Sample(lon, lat);
    }

    // The cursor readout: terrain height (or water depth) and, with an aircraft selected, its clearance.
    private string Describe(GeoPosition position, out bool warning)
    {
        warning = false;
        HeightSample s = Sample(position.Longitude, position.Latitude);
        if (!s.Known)
            return null;
        HeightUnits units = Settings.Units.Value;
        string approx = s.Coarse ? "about " : "";
        if (s.Water)
            return $"Sea, {approx}{HeightText.Format(s.Metres, units)} deep";

        string text = $"Terrain {approx}{HeightText.Format(s.Metres, units)}";
        if (!float.IsNaN(aircraftAltitude) && Settings.AircraftClearance.Value)
        {
            float clearance = aircraftAltitude - s.Metres;
            string who = string.IsNullOrEmpty(aircraftName) ? "aircraft" : aircraftName;
            if (clearance > 0f)
            {
                text += $"\n{who} clears it by {HeightText.Format(clearance, units)}";
            }
            else
            {
                text += $"\n{HeightText.Format(-clearance, units)} above {who}";
                warning = true;
            }
        }
        return text;
    }

    // The selected unit, if it is an aircraft or helicopter in the air: its altitude above sea level.
    private void UpdateAircraft()
    {
        if (Time.unscaledTime < nextAircraftCheck)
            return;
        nextAircraftCheck = Time.unscaledTime + 0.25f;
        aircraftAltitude = float.NaN;
        aircraftName = null;
        if (!Singleton<RenderPosition>.InstanceExists())
            return;
        ObjectBase unit = Singleton<RenderPosition>.Instance.getSelectedObject();
        if (unit == null || unit.IsDestroyed || !unit.IsAirUnit)
            return;
        float altitude = Utils.unityHeightToMeter(unit.transform.position.y);
        HeightSample ground = Sample(unit._geoPosition.Longitude, unit._geoPosition.Latitude);
        float below = ground.Known && !ground.Water ? ground.Metres : 0f;
        if (altitude - below < MinClearanceMetres)
            return;
        aircraftAltitude = altitude;
        aircraftName = unit.getName();
    }
}
