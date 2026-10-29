using System;
using System.Collections.Generic;
using System.ComponentModel;
using SeaPower;
using SeapowerUI;

namespace SeaPowerTopo;

/// <summary>
/// The "Topography" entry in the tactical map's Layers menu (the menu lists <c>MapDisplayState.Tactical.Layers</c>,
/// so one more <see cref="MapLayer"/> there gets a checkbox like the stock layers). Its state is kept in our config;
/// the top bar's Topography button and the toggle key switch the same entry.
/// </summary>
internal sealed class TopoLayer
{
    private const int PreferredNumber = 600;          // Better Tac Map uses 500-509
    private const string Name = "SEWERLABS_TOPOGRAPHY";
    private const string DisplayName = "Topography (heights and depths)";

    private MapLayer layer;
    private bool failed;

    public bool Visible => layer?.Visible ?? Settings.Visible.Value;

    /// <summary>Adds the entry once the game's tactical display state exists (it lives for the whole session).</summary>
    public void EnsureRegistered()
    {
        if (layer != null || failed)
            return;
        try
        {
#if FLATMAP
            GizmoLayerManager state = GizmoLayerManager.Instance;
#else
            MapDisplayState state = MapDisplayState.Tactical;
#endif
            if (state.LayersByName.TryGetValue(Name, out MapLayer existing))
            {
                Adopt(existing);
                return;
            }
            int number = PreferredNumber;
            while (state.LayersById.ContainsKey(number))
                number++;

            // MapLayer looks its menu text up in the language table; give it ours first.
            Singleton<LanguageResourceHandler>.Instance.LanguageDictionary[("MapLayers" + Name).ToLowerInvariant()] = DisplayName;
#if FLATMAP
            var created = new MapLayer(number, Name, true);
            state.Layers.Add(created);
            state.LayersByName[Name] = created;
            state.LayersById[number] = created;
#else
            MapLayer created = GameApi.NewLayer(state, number, Name, Settings.Visible.Value);
            if (created == null || !(state.LayersByName is IDictionary<string, MapLayer> byName) ||
                !(state.LayersById is IDictionary<int, MapLayer> byId))
            {
                failed = true;
                ModLog.Warn("The map's layer list has changed - no Topography entry in the Layers menu; the top bar button and the toggle key still work.");
                return;
            }
            state.Layers.Add(created);
            byName[Name] = created;
            byId[number] = created;
#endif
            Adopt(created);
            ModLog.Info($"Map layer '{DisplayName}' added to the Layers menu (#{number}), {(created.Visible ? "on" : "off")}.");
        }
        catch (Exception ex)
        {
            failed = true;
            ModLog.Error($"Could not add the map layer, use the top bar button or the toggle key instead: {ex}");
        }
    }

    public void Toggle()
    {
        if (layer != null)
            layer.Visible = !layer.Visible;
        else
            Settings.Visible.Value = !Settings.Visible.Value;
    }

    private void Adopt(MapLayer l)
    {
        layer = l;
        l.Visible = Settings.Visible.Value;
        l.PropertyChanged += OnLayerChanged;
    }

    private void OnLayerChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapLayer.Visible) && layer != null && Settings.Visible.Value != layer.Visible)
            Settings.Visible.Value = layer.Visible;
    }
}
