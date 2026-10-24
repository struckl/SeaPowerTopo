using System;
using System.ComponentModel;
using SeaPower;
using SeapowerUI;

namespace SeaPowerTopo;

/// <summary>
/// The "Topography" entry in the map's Layers menu (the menu lists <c>GizmoLayerManager.Layers</c>, so one more
/// <see cref="MapLayer"/> there gets a checkbox like the stock layers). Its state is kept in our config.
/// </summary>
internal sealed class TopoLayer
{
    private const int PreferredNumber = 600;          // Better Tac Map uses 500-509
    private const string Name = "SEWERLABS_TOPOGRAPHY";
    private const string DisplayName = "Topography (heights and depths)";

    private MapLayer layer;
    private bool failed;

    public bool Visible => layer?.Visible ?? Settings.Visible.Value;

    /// <summary>Adds the entry once the game's layer manager exists (it lives for the whole session).</summary>
    public void EnsureRegistered()
    {
        if (layer != null || failed)
            return;
        try
        {
            GizmoLayerManager manager = GizmoLayerManager.Instance;
            if (manager.LayersByName.TryGetValue(Name, out MapLayer existing))
            {
                Adopt(existing);
                return;
            }
            int number = PreferredNumber;
            while (manager.LayersById.ContainsKey(number))
                number++;

            // MapLayer looks its menu text up in the language table; give it ours first.
            Singleton<LanguageResourceHandler>.Instance.LanguageDictionary[("MapLayers" + Name).ToLowerInvariant()] = DisplayName;
            var created = new MapLayer(number, Name, true);
            manager.Layers.Add(created);
            manager.LayersByName[Name] = created;
            manager.LayersById[number] = created;
            Adopt(created);
            ModLog.Info($"Map layer '{DisplayName}' added to the Layers menu (#{number}), {(created.Visible ? "on" : "off")}.");
        }
        catch (Exception ex)
        {
            failed = true;
            ModLog.Error($"Could not add the map layer, use the toggle key instead: {ex}");
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
