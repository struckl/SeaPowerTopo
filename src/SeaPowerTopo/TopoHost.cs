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
/// </summary>
internal sealed class TopoHost : MonoBehaviour
{
    public static TopoHost Instance { get; private set; }

    private const float MinClearanceMetres = 30f;     // closer to the ground than this counts as landed / on deck

    private enum HookState { Waiting, Applied, Failed }

    private readonly TopoLayer layer = new TopoLayer();
    private readonly OverlayRenderer overlay = new OverlayRenderer();
    private readonly List<MapDecoration> maps = new List<MapDecoration>();
    private readonly HashSet<MapBase> undecoratable = new HashSet<MapBase>();

    private HookState mapHooks;
    private float nextHookCheck, nextPrune, nextAircraftCheck;
    private bool missionActive;
    private int errors;

    private string aircraftName;
    private float aircraftAltitude = float.NaN;

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
        overlay.Tick(on, dark, danger);

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
        string highlighted = float.IsNaN(overlay.ShownDanger) ? null : aircraftName;
        foreach (MapDecoration map in maps)
            map.Update(on, readout, overlay, highlighted);
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
            overlay.Shutdown();
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
        using (var reader = new HeightReader(overlay.Lowres))
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
