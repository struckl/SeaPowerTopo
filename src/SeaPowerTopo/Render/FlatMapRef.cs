using System;
using MapControl;
using Noesis;
using SeapowerUI;
using MapPanel = MapControl.MapPanel;
using GeoPosition = SeaPower.GeoPosition;

namespace SeaPowerTopo.Render;

/// <summary>A map the overlay draws into: where it looks, how far it is zoomed, whether it shows its night colours.</summary>
internal abstract class OverlayTarget
{
    /// <summary>Counts the maps used: a layer that sees a new one drops what it put into the old one.</summary>
    public int Generation { get; protected set; }

    /// <summary>The map is there to draw into.</summary>
    public abstract bool Find();

    /// <summary>
    /// What the map shows: a lon/lat rectangle round the view, its scale in degrees of latitude per pixel, and (on
    /// the globe) the part of the globe in view.
    /// </summary>
    public abstract bool TryView(out GeoRect view, out double viewDpp, out GlobeCap cap);

    /// <summary>The map shows its night colours.</summary>
    public abstract bool Dark { get; }

    public abstract void Forget();
}

/// <summary>
/// A flat lon/lat map: the mission editor's (a Noesis map control drawing the stock tiles as pictures, not the globe
/// scene). The overlay goes into it as a picture of its own, laid on a lon/lat rectangle by a <c>MapPanel</c> among
/// the map's background elements – above the tiles, below the unit symbols.
/// </summary>
internal sealed class FlatMapRef : OverlayTarget
{
    private MapPanel panel;

    public MapBase Map { get; private set; }
    public MapViewModel ViewModel { get; private set; }

    /// <summary>Draw into this map from now on (a new one drops everything put into the last).</summary>
    public void Use(MapBase map)
    {
        if (ReferenceEquals(map, Map))
            return;
        Forget();
        Map = map;
        ViewModel = map.DataContext as MapViewModel;
        Generation++;
    }

    public override bool Find() =>
        Map != null && ViewModel != null && VisualTreeHelper.GetParent(Map) != null && Map.ActualWidth >= 2f && Map.ActualHeight >= 2f;

    public override bool TryView(out GeoRect view, out double viewDpp, out GlobeCap cap)
    {
        view = default;
        viewDpp = 0;
        cap = default;
        if (!Find())
            return false;
        float w = Map.ActualWidth, h = Map.ActualHeight;
        GeoPosition nw = Map.ViewToGeoPosition(new Point(0f, 0f)), se = Map.ViewToGeoPosition(new Point(w, h));
        double west = nw.Longitude, east = se.Longitude, north = Math.Min(90.0, nw.Latitude), south = Math.Max(-90.0, se.Latitude);
        if (east <= west)
            east += 360.0;
        if (north <= south)
            return false;
        view = new GeoRect(west, south, east, north);
        viewDpp = (nw.Latitude - se.Latitude) / h;
        return true;
    }

    public override bool Dark => GameApi.IsDark(ViewModel?.Palette);

    /// <summary>A new picture on the map, hidden until it has an image and a place.</summary>
    public Image AddImage()
    {
        if (panel == null)
        {
            panel = new MapPanel { IsHitTestVisible = false };
            panel.ParentMap = Map;
            ViewModel.BackgroundElements.Add(panel);
        }
        // Noesis shows a Unity texture's first row at the bottom; the overlay's first row is its north edge.
        var image = new Image
        {
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5f, 0.5f),
            RenderTransform = new ScaleTransform(1f, -1f),
        };
        panel.Children.Add(image);
        return image;
    }

    /// <summary>Lays a picture on a lon/lat rectangle.</summary>
    public static void Place(Image image, GeoRect rect) =>
        MapPanel.SetBoundingBox(image, new BoundingBox(rect.South, rect.West, rect.North, rect.East));

    public void RemoveImage(Image image) => panel?.Children.Remove(image);

    public override void Forget()
    {
        if (panel != null)
        {
            try
            {
                ViewModel?.BackgroundElements.Remove(panel);
                panel.ParentMap = null;
            }
            catch (Exception ex)
            {
                ModLog.Debug($"Editor map overlay removal: {ex.Message}");
            }
        }
        panel = null;
        Map = null;
        ViewModel = null;
    }
}
