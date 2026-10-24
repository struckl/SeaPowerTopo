using System;
using System.Collections.Generic;
using MapControl;
using Noesis;
using SeaPower;
using SeaPowerTopo.Render;
using SeaPowerTopo.Terrain;

namespace SeaPowerTopo.Ui;

/// <summary>
/// Legend and cursor readout on one map control. Both sit in a grid laid over the map inside the map template's
/// root grid, so they go wherever the map window goes (maximised, resized, popped out). The grid lets clicks
/// through; only the legend button and the open legend take them.
/// <para>The legend button sits bottom right, right-aligned with the map scale just above it, and opens the colour
/// key above itself; a click on the key closes it again.</para>
/// <para>The readout listens on that root grid: preview events tunnel through it before they reach the map, so it
/// sees every mouse move even when the game's map input handling marks the move as handled.</para>
/// </summary>
internal sealed class MapDecoration
{
    /// <summary>Readout text for a map position, or null for none; <c>warning</c> colours it red.</summary>
    public delegate string Describe(GeoPosition position, out bool warning);

    private const float Inset = 10f, Gap = 6f;
    private const float NoScaleBottom = 40f;          // above the map's bottom edge when there is no scale to go by
    private const float ScaleSearchSeconds = 2f;

    public readonly MapBase Map;

    private readonly Panel host;
    private readonly Grid root;
    private readonly Border readout;
    private readonly TextBlock readoutText;
    private readonly StackPanel dock;
    private readonly Button button;
    private readonly Border legend;
    private readonly StackPanel legendBody;
    private readonly Describe describe;
    private readonly MouseEventHandler moveHandler, leaveHandler;
    private readonly SolidColorBrush panelBrush, textBrush, warningBrush, dangerBrush;

    private bool readoutAllowed;
    private bool loggedFailure;

    private MapScale scale;
    private float nextScaleSearch;
    private Thickness dockMargin;
    private bool? shownOpen;

    private bool legendBuilt;
    private Palette legendPalette, legendSea;
    private float legendInterval, legendDanger, legendKeel;
    private bool legendLayer;
    private string legendAircraft, legendSubmarine;
    private HeightUnits legendUnits;

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

        legendBody = new StackPanel { Orientation = Orientation.Vertical };
        legend = new Border
        {
            Background = panelBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, Gap),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = legendBody,
            Visibility = Visibility.Collapsed,
        };
        legend.MouseLeftButtonDown += (sender, args) =>
        {
            args.Handled = true;
            Settings.LegendOpen.Value = false;
        };

        button = new Button
        {
            Focusable = false,
            Padding = new Thickness(6, 2, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            Content = ButtonContent(),
        };
        button.Click += (sender, args) =>
        {
            args.Handled = true;
            Settings.LegendOpen.Value = !Settings.LegendOpen.Value;
        };

        dockMargin = new Thickness(0, 0, Inset, NoScaleBottom);
        dock = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = dockMargin,
            Visibility = Visibility.Collapsed,
        };
        dock.Children.Add(legend);
        dock.Children.Add(button);

        // No background: hit tests fall through it to the map everywhere but on the dock.
        root = new Grid();
        root.Children.Add(dock);
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

    /// <param name="overlayOn">The Topography layer is on (the legend button shows only then).</param>
    /// <param name="readoutOn">The cursor readout may show.</param>
    /// <param name="aircraft">Name of the selected aircraft whose altitude is highlighted, or null.</param>
    /// <param name="submarine">Name of the selected submarine the thermal layer is shown for, or null.</param>
    public void Update(bool overlayOn, bool readoutOn, OverlayRenderer overlay, string aircraft, string submarine)
    {
        readoutAllowed = readoutOn;
        if (!readoutOn && readout.Visibility == Visibility.Visible)
            readout.Visibility = Visibility.Collapsed;

        bool showButton = overlayOn && Settings.Legend.Value && overlay.Palette != null && overlay.HasImage;
        dock.Visibility = showButton ? Visibility.Visible : Visibility.Collapsed;
        if (!showButton)
            return;
        PlaceDock();

        bool open = Settings.LegendOpen.Value;
        if (shownOpen != open)
        {
            shownOpen = open;
            legend.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            button.ToolTip = open ? "Hide the legend" : "Show the legend: height and depth colours, contour step";
        }
        if (open)
            RefreshLegend(overlay, aircraft, submarine);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!readoutAllowed)
            return;
        try
        {
            if (dock.IsMouseOver)
            {
                Hide();
                return;
            }
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

    // Bottom right: right edge in line with the map scale's, just above it. Without a scale (not found, hidden,
    // not in the bottom right), a fixed distance above the map's bottom edge.
    private void PlaceDock()
    {
        if (scale != null && VisualTreeHelper.GetParent(scale) == null)
            scale = null;
        if (scale == null && UnityEngine.Time.unscaledTime >= nextScaleSearch)
        {
            nextScaleSearch = UnityEngine.Time.unscaledTime + ScaleSearchSeconds;
            scale = FindScale();
        }

        float right = Inset, bottom = NoScaleBottom;
        float width = root.ActualWidth, height = root.ActualHeight;
        if (scale != null && scale.Visibility == Visibility.Visible && scale.ActualHeight > 0f && height > 0f)
        {
            Point top = root.PointFromScreen(scale.PointToScreen(new Point(0f, 0f)));
            float scaleRight = width - (top.X + scale.ActualWidth);
            if (top.Y > height / 2f && scaleRight >= 0f && scaleRight < width / 2f)
            {
                right = scaleRight;
                bottom = height - top.Y + Gap;
            }
        }
        if (Math.Abs(right - dockMargin.Right) > 0.5f || Math.Abs(bottom - dockMargin.Bottom) > 0.5f)
        {
            dockMargin = new Thickness(0f, 0f, right, bottom);
            dock.Margin = dockMargin;
        }
    }

    // The stock map scale in the map's template, breadth first (it sits near the top of the tree).
    private MapScale FindScale()
    {
        var queue = new Queue<Visual>();
        queue.Enqueue(host);
        for (int visited = 0; queue.Count > 0 && visited < 4000; visited++)
        {
            Visual v = queue.Dequeue();
            if (v is MapScale found)
                return found;
            if (ReferenceEquals(v, root))
                continue;
            int n = VisualTreeHelper.GetChildrenCount(v);
            for (int i = 0; i < n; i++)
            {
                if (VisualTreeHelper.GetChild(v, i) is Visual child)
                    queue.Enqueue(child);
            }
        }
        return null;
    }

    private void RefreshLegend(OverlayRenderer overlay, string aircraft, string submarine)
    {
        Palette palette = overlay.Palette, sea = overlay.SeaPalette;
        float interval = overlay.ShownInterval, danger = overlay.ShownDanger, keel = overlay.ShownKeel;
        bool layer = overlay.ShownLayer;
        HeightUnits units = Settings.Units.Value;
        bool sameDanger = float.IsNaN(danger) ? float.IsNaN(legendDanger) : danger == legendDanger;
        bool sameKeel = float.IsNaN(keel) ? float.IsNaN(legendKeel) : keel == legendKeel;
        if (legendBuilt && palette == legendPalette && sea == legendSea && interval == legendInterval && sameDanger &&
            sameKeel && layer == legendLayer && aircraft == legendAircraft && submarine == legendSubmarine &&
            units == legendUnits)
        {
            return;
        }
        legendBuilt = true;
        legendPalette = palette;
        legendSea = sea;
        legendInterval = interval;
        legendDanger = danger;
        legendKeel = keel;
        legendLayer = layer;
        legendAircraft = aircraft;
        legendSubmarine = submarine;
        legendUnits = units;

        legendBody.Children.Clear();
        var columns = new StackPanel { Orientation = Orientation.Horizontal };
        columns.Children.Add(Column("Terrain height", palette, units, highestFirst: true));
        if (sea != null)
        {
            StackPanel seaColumn = Column("Sea depth", sea, units, highestFirst: false);
            seaColumn.Margin = new Thickness(14, 0, 0, 0);
            columns.Children.Add(seaColumn);
        }
        legendBody.Children.Add(columns);

        if (!float.IsNaN(danger))
        {
            string who = string.IsNullOrEmpty(aircraft) ? "selected aircraft" : aircraft;
            legendBody.Children.Add(Row(dangerBrush, $"As high as {who} ({HeightText.Format(danger, units, 10)}) or higher", 6f));
        }
        if (!float.IsNaN(keel))
            AddSubmarineRows(keel, layer, palette.Dark, string.IsNullOrEmpty(submarine) ? "the submarine" : submarine, units);
        if (interval > 0f)
        {
            string text = $"Contours every {HeightText.Format(interval, units)}\nBold every {HeightText.Format(interval * 5f, units)}";
            if (sea != null)
                text += "\nDepth lines at the band edges";
            legendBody.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = textBrush,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
    }

    // One band per row, each labelled with the height (depth) it starts at: land highest on top, sea shallowest
    // on top, so the two columns read like a profile from the peaks down to the deep.
    private StackPanel Column(string title, Palette palette, HeightUnits units, bool highestFirst)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical };
        column.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = textBrush,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        int n = palette.Count;
        for (int k = 0; k < n; k++)
        {
            int i = highestFirst ? n - 1 - k : k;
            string label = HeightText.Format(palette.Limits[i], units, 10) + (i == n - 1 ? " +" : "");
            var swatch = new SolidColorBrush(Color.FromArgb(255, palette.Rgb[i * 3], palette.Rgb[i * 3 + 1], palette.Rgb[i * 3 + 2]));
            column.Children.Add(Row(swatch, label));
        }
        return column;
    }

    // The thermal layer's depth bands, where the submarine is below it, and water too shallow for it.
    private void AddSubmarineRows(float keel, bool layer, bool dark, string who, HeightUnits units)
    {
        legendBody.Children.Add(new TextBlock
        {
            Text = $"Thermal layer · {who} at {HeightText.Format(keel, units, 5)}",
            Foreground = textBrush,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 8, 0, 2),
        });
        if (LayerMap.Disabled)
        {
            legendBody.Children.Add(new TextBlock { Text = "None in this mission", Foreground = textBrush, FontSize = 11 });
        }
        else if (!layer)
        {
            legendBody.Children.Add(new TextBlock { Text = "Zoom in to see it", Foreground = textBrush, FontSize = 11 });
        }
        else
        {
            float[] edges = Raster.LayerBands;
            for (int band = 0; band <= edges.Length; band++)
            {
                Raster.LayerColour(band, dark, out float r, out float g, out float b);
                string label = band == 0 ? $"Layer shallower than {HeightText.Format(edges[0], units, 5)}"
                    : band == edges.Length ? $"Layer {HeightText.Format(edges[band - 1], units, 5)} or deeper"
                    : $"Layer from {HeightText.Format(edges[band - 1], units, 5)}";
                legendBody.Children.Add(Row(new SolidColorBrush(Color.FromArgb(255, (byte)r, (byte)g, (byte)b)), label));
            }
            Color stripes = dark ? Color.FromArgb(255, 40, 175, 170) : Color.FromArgb(255, 0, 190, 180);
            legendBody.Children.Add(Row(new SolidColorBrush(stripes), $"Stripes: {who} is below the layer there", 3f));
            legendBody.Children.Add(new TextBlock
            {
                Text = "No tint: no layer (water under 500 ft)",
                Foreground = textBrush,
                FontSize = 11,
            });
        }
        legendBody.Children.Add(Row(dangerBrush, $"Shallower than {who} is deep", 4f));
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

    // Three little swatches (lowland, hills, sea) and the word; text colour comes from the game's button style.
    private static UIElement ButtonContent()
    {
        var swatches = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };
        foreach (Color c in new[] { Color.FromArgb(255, 118, 170, 84), Color.FromArgb(255, 206, 120, 64), Color.FromArgb(255, 66, 92, 130) })
            swatches.Children.Add(new Rectangle { Width = 4, Height = 10, Fill = new SolidColorBrush(c) });
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(swatches);
        content.Children.Add(new TextBlock { Text = "Legend", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }
}
