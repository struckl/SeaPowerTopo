using System;
using MapControl;
using Noesis;
using SeaPower;
using SeaPowerTopo.Render;

namespace SeaPowerTopo.Ui;

/// <summary>
/// Legend and cursor readout on one map control. Both sit in a click-through grid laid over the map inside the
/// map template's root grid, so they go wherever the map window goes (maximised, resized, popped out).
/// <para>The readout listens on that root grid: preview events tunnel through it before they reach the map, so it
/// sees every mouse move even when the game's map input handling marks the move as handled.</para>
/// </summary>
internal sealed class MapDecoration
{
    /// <summary>Readout text for a map position, or null for none; <c>warning</c> colours it red.</summary>
    public delegate string Describe(GeoPosition position, out bool warning);

    public readonly MapBase Map;

    private readonly Panel host;
    private readonly Grid root;
    private readonly Border readout;
    private readonly TextBlock readoutText;
    private readonly Border legend;
    private readonly StackPanel legendRows;
    private readonly Describe describe;
    private readonly MouseEventHandler moveHandler, leaveHandler;
    private readonly SolidColorBrush panelBrush, textBrush, warningBrush, dangerBrush;

    private bool readoutAllowed;
    private bool loggedFailure;

    private bool legendBuilt;
    private Palette legendPalette;
    private float legendInterval, legendDanger;
    private string legendAircraft;
    private HeightUnits legendUnits;
    private Corner legendCorner;

    /// <summary>Decorates the map, or returns null if its template isn't laid out as expected.</summary>
    public static MapDecoration TryAttach(MapBase map, Describe describe) =>
        VisualTreeHelper.GetParent(map) is Panel host ? new MapDecoration(map, host, describe) : null;

    private MapDecoration(MapBase map, Panel host, Describe describe)
    {
        Map = map;
        this.host = host;
        this.describe = describe;
        panelBrush = new SolidColorBrush(Color.FromArgb(215, 16, 20, 24));
        textBrush = new SolidColorBrush(Color.FromArgb(255, 236, 236, 236));
        warningBrush = new SolidColorBrush(Color.FromArgb(255, 255, 110, 90));
        dangerBrush = new SolidColorBrush(Color.FromArgb(255, 220, 34, 26));

        readoutText = new TextBlock { Foreground = textBrush, FontSize = 13 };
        readout = new Border
        {
            Background = panelBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3, 6, 3),
            Child = readoutText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        var cursorLayer = new Canvas { IsHitTestVisible = false };
        cursorLayer.Children.Add(readout);

        legendRows = new StackPanel { Orientation = Orientation.Vertical };
        legend = new Border
        {
            Background = panelBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(10),
            Child = legendRows,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        root = new Grid { IsHitTestVisible = false };
        root.Children.Add(legend);
        root.Children.Add(cursorLayer);
        host.Children.Add(root);

        moveHandler = OnMouseMove;
        leaveHandler = OnMouseLeave;
        host.PreviewMouseMove += moveHandler;
        host.MouseLeave += leaveHandler;
    }

    /// <summary>False once the map has left the visual tree (window closed, mission over).</summary>
    public bool Alive => VisualTreeHelper.GetParent(Map) != null;

    public void Detach()
    {
        try
        {
            host.PreviewMouseMove -= moveHandler;
            host.MouseLeave -= leaveHandler;
            host.Children.Remove(root);
        }
        catch (Exception ex)
        {
            ModLog.Debug($"Map decoration detach: {ex.Message}");
        }
    }

    /// <param name="overlayOn">The Topography layer is on (the legend shows only then).</param>
    /// <param name="readoutOn">The cursor readout may show.</param>
    /// <param name="aircraft">Name of the selected aircraft whose altitude is highlighted, or null.</param>
    public void Update(bool overlayOn, bool readoutOn, OverlayRenderer overlay, string aircraft)
    {
        readoutAllowed = readoutOn;
        if (!readoutOn && readout.Visibility == Visibility.Visible)
            readout.Visibility = Visibility.Collapsed;

        bool showLegend = overlayOn && Settings.Legend.Value && overlay.Palette != null && overlay.HasImage;
        legend.Visibility = showLegend ? Visibility.Visible : Visibility.Collapsed;
        if (showLegend)
            RefreshLegend(overlay, aircraft);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!readoutAllowed)
            return;
        try
        {
            Point p = e.GetPosition(Map);
            if (p.X < 0 || p.Y < 0 || p.X > Map.ActualWidth || p.Y > Map.ActualHeight)
            {
                Hide();
                return;
            }
            string text = describe(Map.ViewToGeoPosition(p), out bool warning);
            if (text == null)
            {
                Hide();
                return;
            }
            readoutText.Text = text;
            readoutText.Foreground = warning ? warningBrush : textBrush;

            // Below right of the pointer, flipped to the other side near the map's right and bottom edges.
            Point q = e.GetPosition(root);
            float w = readout.ActualWidth, h = readout.ActualHeight;
            float x = q.X + 18f, y = q.Y + 20f;
            if (x + w > root.ActualWidth - 4f)
                x = q.X - 12f - w;
            if (y + h > root.ActualHeight - 4f)
                y = q.Y - 12f - h;
            Canvas.SetLeft(readout, x);
            Canvas.SetTop(readout, y);
            readout.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            if (!loggedFailure)
            {
                loggedFailure = true;
                ModLog.Error($"Cursor readout failed: {ex}");
            }
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e) => Hide();

    private void Hide()
    {
        if (readout.Visibility != Visibility.Collapsed)
            readout.Visibility = Visibility.Collapsed;
    }

    private void RefreshLegend(OverlayRenderer overlay, string aircraft)
    {
        Palette palette = overlay.Palette;
        float interval = overlay.ShownInterval, danger = overlay.ShownDanger;
        HeightUnits units = Settings.Units.Value;
        Corner corner = Settings.LegendCorner.Value;
        bool sameDanger = float.IsNaN(danger) ? float.IsNaN(legendDanger) : danger == legendDanger;
        if (legendBuilt && palette == legendPalette && interval == legendInterval && sameDanger &&
            aircraft == legendAircraft && units == legendUnits && corner == legendCorner)
        {
            return;
        }
        legendBuilt = true;
        legendPalette = palette;
        legendInterval = interval;
        legendDanger = danger;
        legendAircraft = aircraft;
        legendUnits = units;
        legendCorner = corner;

        legend.HorizontalAlignment = corner == Corner.TopLeft || corner == Corner.BottomLeft
            ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        legend.VerticalAlignment = corner == Corner.TopLeft || corner == Corner.TopRight
            ? VerticalAlignment.Top : VerticalAlignment.Bottom;

        legendRows.Children.Clear();
        legendRows.Children.Add(new TextBlock
        {
            Text = "Terrain height",
            Foreground = textBrush,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        // Highest band on top; each swatch is labelled with the height its band starts at.
        for (int i = palette.Count - 1; i >= 0; i--)
        {
            string label = HeightText.Format(palette.Limits[i], units, 10) + (i == palette.Count - 1 ? " +" : "");
            var swatch = new SolidColorBrush(Color.FromArgb(255, palette.Rgb[i * 3], palette.Rgb[i * 3 + 1], palette.Rgb[i * 3 + 2]));
            legendRows.Children.Add(Row(swatch, label));
        }
        if (!float.IsNaN(danger))
        {
            string who = string.IsNullOrEmpty(aircraft) ? "selected aircraft" : aircraft;
            legendRows.Children.Add(Row(dangerBrush, $"As high as {who} ({HeightText.Format(danger, units, 10)}) or higher", 6f));
        }
        if (interval > 0f)
        {
            legendRows.Children.Add(new TextBlock
            {
                Text = $"Contours every {HeightText.Format(interval, units)}\nBold every {HeightText.Format(interval * 5f, units)}",
                Foreground = textBrush,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
    }

    private StackPanel Row(Brush swatch, string label, float topMargin = 1f)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, topMargin, 0, 1) };
        row.Children.Add(new Rectangle
        {
            Width = 16,
            Height = 11,
            Fill = swatch,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = textBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }
}
