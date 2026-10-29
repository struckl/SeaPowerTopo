using System;
using System.Collections.Generic;
using MapControl;
using Noesis;
using SeaPower;
using SeaPowerTopo.Render;
using SeaPowerTopo.Terrain;

namespace SeaPowerTopo.Ui;

/// <summary>
/// The mod's controls on one map control: a row of buttons in the map window's title bar – Topography on/off, the
/// look, the legend, the cursor readout on/off – the look list, the legend and short notices dropping down below it
/// at the top right of the map, and the readout next to the pointer.
/// <para>The buttons go into the title bar's button row, left of the window menu (≡), like the game's own map
/// controls. A map without such a title bar (the mission editor, a map in another window) gets the same row at its
/// own top right instead. Everything else sits in a grid laid over the map inside the map template's root grid, so it
/// goes wherever the map goes; the grid lets clicks through, only the buttons and the open lists take them.</para>
/// <para>The readout listens on that root grid: preview events tunnel through it before they reach the map, so it
/// sees every mouse move even when the game's map input handling marks the move as handled.</para>
/// </summary>
internal sealed class MapDecoration
{
    /// <summary>Readout text for a map position, or null for none; <c>warning</c> colours it red.</summary>
    public delegate string Describe(GeoPosition position, out bool warning);

    private const float Inset = 10f, Gap = 6f;
    private const float HeaderSearchSeconds = 1f;
    private const int HeaderSearches = 20;
    private const float BarFontSize = 11f;

    public readonly MapBase Map;

    private readonly Panel host;
    private readonly Grid root;
    private readonly Border readout;
    private readonly TextBlock readoutText;
    private readonly StackPanel dock, bar;
    private readonly ToggleButton topoToggle, readoutToggle;
    private readonly Button legendButton, lookButton;
    private readonly Border notice, picker;
    private readonly TextBlock noticeText, lookText;
    private readonly StackPanel pickerList;
    private float noticeUntil;
    private bool pickerOpen;
    private string lookShown;
    private int lookRevision = -1;
    private readonly Border legend;
    private readonly StackPanel legendBody;
    private readonly Describe describe;
    private readonly MouseEventHandler moveHandler, leaveHandler;
    private readonly SolidColorBrush panelBrush, textBrush, warningBrush, dangerBrush;

    // Where the button row is: the title bar's button row (and the window menu's place in it), or the dock.
    private Panel header;
    private float nextHeaderSearch;
    private int headerSearches;

    private bool readoutAllowed;
    private bool loggedFailure;
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
            Margin = new Thickness(0, Gap, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = legendBody,
            Visibility = Visibility.Collapsed,
        };
        legend.MouseLeftButtonDown += (sender, args) =>
        {
            args.Handled = true;
            Settings.LegendOpen.Value = false;
        };

        // The title bar's buttons are 20 px high: text only, small.
        topoToggle = new ToggleButton
        {
            Focusable = false,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = BarContent("Topography", swatches: true),
            ToolTip = $"Topography: land heights and sea depths on the map ({Settings.ToggleKey.Value}). Also in the map menu under Map Layers.",
        };
        topoToggle.Click += (sender, args) =>
        {
            args.Handled = true;
            TopoHost.Instance?.ToggleLayer();
        };

        // The look button names the theme on the map; a click opens the list of themes of the map's current theme.
        lookText = new TextBlock { FontSize = BarFontSize, VerticalAlignment = VerticalAlignment.Center };
        lookButton = new Button
        {
            Focusable = false,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = lookText,
            Visibility = Visibility.Collapsed,
        };
        lookButton.Click += (sender, args) =>
        {
            args.Handled = true;
            pickerOpen = !pickerOpen;
            picker.Visibility = pickerOpen ? Visibility.Visible : Visibility.Collapsed;
        };

        legendButton = new Button
        {
            Focusable = false,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = BarContent("Legend", swatches: false),
            Visibility = Visibility.Collapsed,
        };
        legendButton.Click += (sender, args) =>
        {
            args.Handled = true;
            Settings.LegendOpen.Value = !Settings.LegendOpen.Value;
        };

        readoutToggle = new ToggleButton
        {
            Focusable = false,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = BarContent("Cursor height", swatches: false),
            ToolTip = "Terrain height or sea depth next to the mouse pointer: lit while it shows; click to switch it off or on (also with Topography off)",
        };
        readoutToggle.Click += (sender, args) =>
        {
            args.Handled = true;
            TopoHost.Instance?.ToggleReadout();
        };

        bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        bar.Children.Add(topoToggle);
        bar.Children.Add(lookButton);
        bar.Children.Add(legendButton);
        bar.Children.Add(readoutToggle);

        pickerList = new StackPanel { Orientation = Orientation.Vertical };
        picker = new Border
        {
            Background = panelBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 6, 6, 5),
            Margin = new Thickness(0, Gap, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = pickerList,
            Visibility = Visibility.Collapsed,
        };

        noticeText = new TextBlock { Foreground = textBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
        notice = new Border
        {
            Background = panelBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, Gap, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = noticeText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        // Top right, below the title bar: what the buttons open, newest notice first.
        dock = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, Inset, 0),
        };
        dock.Children.Add(notice);
        dock.Children.Add(picker);
        dock.Children.Add(legend);

        // No background: hit tests fall through it to the map everywhere but on the dock.
        root = new Grid();
        root.Children.Add(dock);
        root.Children.Add(cursorLayer);
        host.Children.Add(root);

        moveHandler = OnMouseMove;
        leaveHandler = OnMouseLeave;
        host.PreviewMouseMove += moveHandler;
        host.MouseLeave += leaveHandler;
        PlaceBar();
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
            header?.Children.Remove(bar);
            header = null;
        }
        catch (Exception ex)
        {
            ModLog.Debug($"Map decoration detach: {ex.Message}");
        }
    }

    /// <param name="overlayOn">The Topography layer is on (the look and legend buttons show only then).</param>
    /// <param name="readoutOn">The cursor readout may show.</param>
    /// <param name="aircraft">Name of the selected aircraft whose altitude is highlighted, or null.</param>
    /// <param name="submarine">Name of the selected submarine the thermal layer is shown for, or null.</param>
    public void Update(bool overlayOn, bool readoutOn, OverlayRenderer overlay, string aircraft, string submarine)
    {
        if (header == null && headerSearches < HeaderSearches && UnityEngine.Time.unscaledTime >= nextHeaderSearch)
            PlaceBar();

        readoutAllowed = readoutOn;
        if (!readoutOn && readout.Visibility == Visibility.Visible)
            readout.Visibility = Visibility.Collapsed;
        SetChecked(topoToggle, overlayOn);
        SetChecked(readoutToggle, readoutOn);

        bool drawn = overlayOn && overlay != null && overlay.Palette != null && overlay.HasImage;
        SetVisible(lookButton, drawn);
        SetVisible(legendButton, drawn && Settings.Legend.Value);
        if (notice.Visibility == Visibility.Visible && UnityEngine.Time.unscaledTime > noticeUntil)
            notice.Visibility = Visibility.Collapsed;
        if (!drawn)
        {
            SetVisible(picker, false);
            SetVisible(legend, false);
            pickerOpen = false;
            shownOpen = null;
            return;
        }

        Theme theme = overlay.Theme;
        if (theme != null)
            RefreshLookButton(theme);
        SetVisible(picker, pickerOpen);
        if (header != null)
            HangDockFromLookButton();

        bool open = Settings.LegendOpen.Value && Settings.Legend.Value;
        if (shownOpen != open)
        {
            shownOpen = open;
            legend.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            legendButton.ToolTip = open ? "Hide the legend" : "Show the legend: height and depth colours, contour step";
        }
        if (open)
            RefreshLegend(overlay, aircraft, submarine);
    }

    // In the title bar, what the buttons open hangs below the look button, right edges in line: clear of the game's
    // own controls in the map's top right corner (the time controls over a full-screen map).
    private void HangDockFromLookButton()
    {
        if (lookButton.ActualWidth <= 0f || root.ActualWidth <= 0f)
            return;
        Point edge = root.PointFromScreen(lookButton.PointToScreen(new Point(lookButton.ActualWidth, 0f)));
        float right = Math.Max(Inset, root.ActualWidth - edge.X);
        if (right < root.ActualWidth - 60f && Math.Abs(right - dock.Margin.Right) > 0.5f)
            dock.Margin = new Thickness(0f, 0f, right, 0f);
    }

    private static void SetChecked(ToggleButton button, bool on)
    {
        if (button.IsChecked != on)
            button.IsChecked = on;
    }

    private static void SetVisible(UIElement element, bool visible)
    {
        Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
        if (element.Visibility != v)
            element.Visibility = v;
    }

    // The buttons into the map window's title bar, left of its menu (≡); until that is found (the window's template
    // may not be applied yet), and for good where there is none, at the top of the dock.
    private void PlaceBar()
    {
        nextHeaderSearch = UnityEngine.Time.unscaledTime + HeaderSearchSeconds;
        headerSearches++;
        Panel row = FindHeaderRow(out int menuIndex);
        if (row == null)
        {
            if (VisualTreeHelper.GetParent(bar) == null)
            {
                bar.Margin = new Thickness(0, Gap, 0, 0);
                bar.Height = 22;
                dock.Children.Insert(0, bar);
            }
            if (headerSearches == HeaderSearches)
                ModLog.Info("No title bar on this map: the Topography buttons sit at its top right.");
            return;
        }
        if (VisualTreeHelper.GetParent(bar) is Panel old)
            old.Children.Remove(bar);
        bar.Margin = new Thickness(0);
        bar.Height = float.NaN;
        row.Children.Insert(menuIndex, bar);
        header = row;
        ModLog.Info("Topography buttons placed in the map's title bar.");
    }

    // The panel holding the map window's menu (≡) and its window buttons: in the window round the map, outside the
    // map itself.
    private Panel FindHeaderRow(out int menuIndex)
    {
        menuIndex = 0;
        DependencyObject window = Map;
        for (int up = 0; up < 40 && window != null && window.GetType().Name != "Window"; up++)
            window = VisualTreeHelper.GetParent(window);
        if (window == null || window.GetType().Name != "Window")
            return null;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(window);
        for (int seen = 0; queue.Count > 0 && seen < 3000; seen++)
        {
            DependencyObject d = queue.Dequeue();
            if (ReferenceEquals(d, Map))
                continue;
            if (d is Menu menu && VisualTreeHelper.GetParent(menu) is Panel row && !ReferenceEquals(row, dock))
            {
                menuIndex = row.Children.IndexOf(menu);
                return menuIndex >= 0 ? row : null;
            }
            if (d is Visual v)
            {
                int n = VisualTreeHelper.GetChildrenCount(v);
                for (int i = 0; i < n; i++)
                    queue.Enqueue(VisualTreeHelper.GetChild(v, i));
            }
        }
        return null;
    }

    /// <summary>A line of text below the buttons for a few seconds (the theme that was just chosen).</summary>
    public void ShowNotice(string text, float seconds = 2.5f)
    {
        noticeText.Text = text;
        notice.Visibility = Visibility.Visible;
        noticeUntil = UnityEngine.Time.unscaledTime + seconds;
    }

    // The button shows the theme on the map; the picker lists the themes of its map theme, the current one in bold.
    private void RefreshLookButton(Theme theme)
    {
        if (lookShown == theme.Id && lookRevision == Themes.Revision)
            return;
        lookShown = theme.Id;
        lookRevision = Themes.Revision;
        lookText.Text = theme.Name;
        lookButton.ToolTip = $"Map look: {theme.Name} ({theme.Note}). Click to choose another; {Settings.LookKey.Value} switches to the next.";

        pickerList.Children.Clear();
        pickerList.Children.Add(new TextBlock
        {
            Text = theme.Dark ? "Look for the night map" : "Look for the day map",
            Foreground = textBrush,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (Theme t in Themes.Of(theme.Dark))
        {
            Theme choice = t;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Swatches(choice));
            content.Children.Add(new TextBlock
            {
                Text = choice.Name,
                FontSize = 12,
                FontWeight = choice == theme ? FontWeight.Bold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (choice.Custom)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "file",
                    FontSize = 10,
                    Opacity = 0.6f,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            var row = new Button
            {
                Focusable = false,
                Padding = new Thickness(6, 2, 8, 2),
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = content,
                ToolTip = choice.Note,
            };
            row.Click += (sender, args) =>
            {
                args.Handled = true;
                pickerOpen = false;
                picker.Visibility = Visibility.Collapsed;
                TopoHost.Instance?.SetLook(choice);
            };
            pickerList.Children.Add(row);
        }

        pickerList.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 3),
            Background = textBrush,
            Opacity = 0.25f,
        });
        pickerList.Children.Add(ActionRow("Save this look as a theme file", "Writes the look on the map as a file you can edit; the map follows when you save it.",
                                          () => TopoHost.Instance?.SaveLook()));
        pickerList.Children.Add(ActionRow("Open the themes folder", "Where the theme files live (BepInEx/config/SeaPowerTopo/themes).",
                                          () => TopoHost.Instance?.OpenThemeFolder()));
    }

    private Button ActionRow(string text, string tip, Action action)
    {
        var row = new Button
        {
            Focusable = false,
            Padding = new Thickness(6, 2, 8, 2),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { Text = text, FontSize = 11, FontStyle = FontStyle.Italic },
            ToolTip = tip,
        };
        row.Click += (sender, args) =>
        {
            args.Handled = true;
            pickerOpen = false;
            picker.Visibility = Visibility.Collapsed;
            action();
        };
        return row;
    }

    // Three little swatches of a theme: lowland, high ground, sea.
    private static UIElement Swatches(Theme t)
    {
        // Lowland, high ground and sea at fixed places along the key colours (a file's themes have any number of them).
        int[] low = t.LandFlat ?? Dim(At(t.LandKeys, 2 / 12f), t.LandBright);
        int[] high = t.LandFlat != null ? t.Coast ?? t.LandFlat : Dim(At(t.LandKeys, 9 / 12f), t.LandBright);
        int[] sea = t.Zones != null ? t.Zones[1] : At(t.SeaKeys, 2 / 11f);
        var swatches = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        foreach (int[] c in new[] { low, high, sea })
            swatches.Children.Add(new Rectangle { Width = 6, Height = 12, Fill = Solid(c) });
        return swatches;
    }

    private static int[] At(int[][] keys, float position) => keys[(int)Math.Round(position * (keys.Length - 1))];

    private static int[] Dim(int[] c, float k) => new[] { (int)(c[0] * k), (int)(c[1] * k), (int)(c[2] * k) };

    private static SolidColorBrush Solid(int[] c) => new SolidColorBrush(Color.FromArgb(255, (byte)c[0], (byte)c[1], (byte)c[2]));

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!readoutAllowed)
            return;
        try
        {
            if (dock.IsMouseOver || bar.IsMouseOver)
            {
                Hide();
                return;
            }
            Point p = e.GetPosition(Map);
            if (p.X < 0 || p.Y < 0 || p.X > Map.ActualWidth || p.Y > Map.ActualHeight || !TryGeo(p, out GeoPosition at))
            {
                Hide();
                return;
            }
            string text = describe(at, out bool warning);
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

    // The map position under a point of the map control; false off the globe (its edge is in view when zoomed out).
    private bool TryGeo(Point p, out GeoPosition at)
    {
#if FLATMAP
        at = Map.ViewToGeoPosition(p);                 // the flat map has no edge
        return true;
#else
        MapProjection projection = Map.MapProjection;
        ViewTransform transform = Map.ViewTransform;
        if (projection == null || transform == null)
        {
            at = Map.ViewToGeoPosition(p);
            return true;
        }
        Point m = transform.ViewToMap(p);
        return projection.TryMapToGeoPosition(m.X, m.Y, out at);
#endif
    }

    private void OnMouseLeave(object sender, MouseEventArgs e) => Hide();

    private void Hide()
    {
        if (readout.Visibility != Visibility.Collapsed)
            readout.Visibility = Visibility.Collapsed;
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
        Theme theme = palette.Theme;
        legendBody.Children.Add(new TextBlock
        {
            Text = $"{theme.Name} · {theme.Note}",
            Foreground = textBrush,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 5),
        });
        if (theme.IsChart)
        {
            AddChartRows(theme, interval, units);
        }
        else
        {
            var columns = new StackPanel { Orientation = Orientation.Horizontal };
            columns.Children.Add(Column("Terrain height", palette, units, highestFirst: true));
            if (sea != null)
            {
                StackPanel seaColumn = Column("Sea depth", sea, units, highestFirst: false);
                seaColumn.Margin = new Thickness(14, 0, 0, 0);
                columns.Children.Add(seaColumn);
            }
            legendBody.Children.Add(columns);
        }

        if (!float.IsNaN(danger))
        {
            string who = string.IsNullOrEmpty(aircraft) ? "selected aircraft" : aircraft;
            legendBody.Children.Add(Row(dangerBrush, $"As high as {who} ({HeightText.Format(danger, units, 10)}) or higher", 6f));
        }
        if (!float.IsNaN(keel))
            AddSubmarineRows(keel, layer, palette.Dark, string.IsNullOrEmpty(submarine) ? "the submarine" : submarine, units);
        if (interval > 0f && !theme.IsChart)
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

    // A chart theme's key: land, coastline, the four water shades by the shallow, safety and deep contours, and the lines.
    private void AddChartRows(Theme theme, float interval, HeightUnits units)
    {
        float safety = Settings.ChartSafety.Value;
        float shallow = Math.Max(2f, safety / 6f), deep = safety * 100f / 30f;
        string F(float metres) => HeightText.Format(metres, units, 10);
        legendBody.Children.Add(Row(Solid(theme.LandFlat), "Land"));
        if (theme.Coast != null)
            legendBody.Children.Add(Row(Solid(theme.Coast), "Coastline"));
        if (interval > 0f && theme.LandLine != null)
            legendBody.Children.Add(Row(Solid(theme.LandLine), $"Land contours every {F(interval)}, bold every {F(interval * 5f)}"));
        legendBody.Children.Add(Row(Solid(theme.Zones[0]), $"Water under {F(shallow)}", 5f));
        legendBody.Children.Add(Row(Solid(theme.Zones[1]), $"to {F(safety)}"));
        legendBody.Children.Add(Row(Solid(theme.Zones[2]), $"to {F(deep)}"));
        legendBody.Children.Add(Row(Solid(theme.Zones[3]), "deeper"));
        if (theme.SafetyLine != null)
            legendBody.Children.Add(Row(Solid(theme.SafetyLine), $"Safety contour {F(safety)}", 4f));
        if (theme.SeaLine != null)
            legendBody.Children.Add(Row(Solid(theme.SeaLine), "Depth contours"));
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

    // The word, after three little swatches (lowland, hills, sea) for the Topography button; the text colour comes
    // from the game's button style.
    private static UIElement BarContent(string text, bool swatches)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (swatches)
        {
            var colours = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            };
            foreach (Color c in new[] { Color.FromArgb(255, 118, 170, 84), Color.FromArgb(255, 206, 120, 64), Color.FromArgb(255, 66, 92, 130) })
                colours.Children.Add(new Rectangle { Width = 4, Height = 10, Fill = new SolidColorBrush(c) });
            content.Children.Add(colours);
        }
        content.Children.Add(new TextBlock { Text = text, FontSize = BarFontSize, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }
}
