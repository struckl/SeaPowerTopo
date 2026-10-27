using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AnchorChain;
using BepInEx;
using MapControl;
using Noesis;
using SeaPower;
using SeaPower.LiveMap;
using SeapowerUI.ViewModels;
using UnityEngine;
using Object = UnityEngine.Object;
using Path = System.IO.Path;

namespace ShotDirector;

/// <summary>
/// Dev tool for the Workshop screenshots, never shipped. Polls <c>BepInEx/shotdirector.cmd</c>: every line written
/// there is a command, run in order (one per frame, <c>wait</c> pauses the queue). Results and errors go to
/// <c>BepInEx/shotdirector.log</c>. Commands:
/// <code>
/// load missions/NATO/Trollfjorden.ini   start a mission (from the main menu)
/// click X Y | move X Y | key tab        mouse / keyboard on the main monitor (screen pixels, key names or hex)
/// wait SECONDS
/// waitload                               wait until no load screen runs (start-up or mission)
/// play                                   leave the mission briefing (its Play button)
/// mapfull                                map full screen (Tab)
/// view SOUTH WEST NORTH EAST             map view on these bounds (animated; the view model may undo it)
/// center LAT LON [ZOOM] | where          map centre and zoom level through the map's view model
/// deselect
/// theme light|dark|auto
/// units                                  log the player's units (index, name, object, lat/lon, altitude/depth)
/// select NAME                            select a unit (substring of its name)
/// pin NAME METRES                        hold a unit at this altitude (negative = depth) until unpin
/// tc FACTOR | pause | unpause            time compression
/// topo SETTING VALUE                     a SeaPowerTopo config entry, e.g. topo LegendOpen true
/// hud on|off
/// watermark off                          hide the beta's version watermark (for screenshots)
/// symbols off|on                       hide the map's unit symbols, labels and drawn areas (on screen only, not saved)
/// hide TITLE                             collapse the game window whose title contains TITLE (e.g. Formation Manager)
/// tree [UP|all]                          the UI tree from UP (6) levels above the map control (all: every view)
///                                        to shotdirector-tree.txt
/// press TEXT                             a button, menu item or tab whose text contains TEXT (first visible one)
/// menu [close]                           open (or close) the map window's menu (the ≡ in its title bar)
/// geo X Y                                map position under a screen point (physical pixels), the game's way and the float way
/// mapwin                                 map back to a window (not full screen)
/// shot NAME                             screenshot to BepInEx/shots/NAME.png
/// quit
/// </code>
/// </summary>
[ACPlugin("dev.sewerlabs.shotdirector", "Shot Director (dev)", "1.0.0")]
public class Plugin : IAnchorChainMod
{
    public void TriggerEntryPoint()
    {
        var host = new GameObject("ShotDirector");
        Object.DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent<Director>();
    }
}

internal sealed class Director : MonoBehaviour
{
    private static readonly string CommandFile = Path.Combine(Paths.BepInExRootPath, "shotdirector.cmd");
    private static readonly string LogFile = Path.Combine(Paths.BepInExRootPath, "shotdirector.log");
    private static readonly string ShotDir = Path.Combine(Paths.BepInExRootPath, "shots");

    private readonly Queue<string> queue = new Queue<string>();
    private float nextPoll, waitUntil;
    private bool waitingForLoad;
    private ObjectBase pinned;
    private float pinnedY;

    private void Start() => Log("ready");

    private void Update()
    {
        if (Time.unscaledTime >= nextPoll)
        {
            nextPoll = Time.unscaledTime + 0.25f;
            Poll();
        }
        Hold();
        if (queue.Count == 0 || Time.unscaledTime < waitUntil)
            return;
        if (waitingForLoad)
        {
            if (!Singleton<LoadScreen>.InstanceExists() || Singleton<LoadScreen>.Instance.LoadText != "Done")
                return;
            waitingForLoad = false;
            Log("waitload done");
        }
        string line = queue.Dequeue();
        try
        {
            Run(line);
        }
        catch (Exception ex)
        {
            Log($"FAILED {line}: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
        }
    }

    private void Poll()
    {
        if (!File.Exists(CommandFile))
            return;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(CommandFile);
            File.Delete(CommandFile);
        }
        catch (IOException)
        {
            return;                                   // still being written; next poll
        }
        foreach (string l in lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")))
            queue.Enqueue(l);
    }

    private void Run(string line)
    {
        string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string rest = line.Length > a[0].Length ? line.Substring(a[0].Length).Trim() : "";
        switch (a[0].ToLowerInvariant())
        {
            case "load":
                Log($"load {Globals.OpenMission(Singleton<FileManager>.Instance.GetFile(rest))}");
                MissionManager.DoLoad();
                break;
            case "click":
                Native.Click(I(a[1]), I(a[2]));
                Log(line);
                break;
            case "move":
                Native.Move(I(a[1]), I(a[2]));
                Log(line);
                break;
            case "key":
                Native.Key(rest);
                Log(line);
                break;
            case "wait":
                waitUntil = Time.unscaledTime + F(a[1]);
                break;
            case "waitload":
                // The text reads "Done" from the end of one load screen to the start of the next.
                waitingForLoad = true;
                waitUntil = Time.unscaledTime + 2f;
                break;
            case "play":
                Play();
                break;
            case "mapfull":
                MapFull();
                break;
            case "view":
                View(D(a[1]), D(a[2]), D(a[3]), D(a[4]));
                break;
            case "center":
                // The map control is bound to the scene map's view model; setting it there sticks.
                Globals._mainGameViewModel.Map.DisplayMap.Center = new GeoPosition(D(a[1]), D(a[2]));
                if (a.Length > 3)
                    Globals._mainGameViewModel.Map.DisplayMap.Zoom = F(a[3]);
                Log(line);
                break;
            case "where":
                GeoPosition c = Globals._mainGameViewModel.Map.DisplayMap.Center;
                Log($"where {c.Latitude:0.000},{c.Longitude:0.000} zoom {Globals._mainGameViewModel.Map.DisplayMap.Zoom:0.00}");
                break;
            case "deselect":
                Singleton<RenderPosition>.Instance.deselectObjectAndDetachCamera();
                Log(line);
                break;
            case "get":
                Log($"get {a[1]} = {Reflect.Get(a[1])}");
                break;
            case "set":
                Reflect.Set(a[1], rest.Substring(a[1].Length).Trim());
                Log($"set {a[1]} = {Reflect.Get(a[1])}");
                break;
            case "call":
                Log($"call {a[1]} -> {Reflect.Call(a[1], a.Skip(2).ToArray())}");
                break;
            case "theme":
                MapThemeSettings.Shared(MapThemeSettings.Tactical).Theme =
                    (ThemeMode)Enum.Parse(typeof(ThemeMode), a[1], ignoreCase: true);
                Log(line);
                break;
            case "units":
                Units();
                break;
            case "select":
                Select(Find(rest));
                break;
            case "pin":
                Pin(Find(string.Join(" ", a.Skip(1).Take(a.Length - 2))), F(a[a.Length - 1]));
                break;
            case "unpin":
                pinned = null;
                Log("unpinned");
                break;
            case "tc":
                GameTime.TimeCompression = F(a[1]);
                Log(line);
                break;
            case "pause":
                GameTime.Pause(muteSounds: false);
                Log(line);
                break;
            case "unpause":
                GameTime.UnPause();
                Log(line);
                break;
            case "topo":
                Topo(a[1], rest.Substring(a[1].Length).Trim());
                break;
            case "hud":
                MainGameViewModel.SetHudVisible(a[1] == "on");
                Log(line);
                break;
            case "watermark":
                if (Singleton<VersionWatermark>.InstanceExists())
                    Singleton<VersionWatermark>.Instance.gameObject.SetActive(a.Length > 1 && a[1] == "on");
                Log(line);
                break;
            case "hide":
                HideWindow(rest);
                break;
            case "symbols":
                Symbols(a[1] == "on");
                break;
            case "tree":
                if (a.Length > 1 && a[1] == "all")
                    TreeAll();
                else
                    Tree(a.Length > 1 ? I(a[1]) : 6);
                break;
            case "press":
                Press(rest);
                break;
            case "geo":
                Geo(F(a[1]), F(a[2]));
                break;
            case "menu":
                OpenMapMenu(open: !(a.Length > 1 && a[1] == "close"));
                break;
            case "mapwin":
                if (Globals._mainGameViewModel?.Map?.IsFullscreen == true)
                    Globals._mainGameViewModel.ToggleMapFullscreen();
                Log("mapwin");
                break;
            case "shot":
                Directory.CreateDirectory(ShotDir);
                string path = Path.Combine(ShotDir, rest + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Log($"shot {path}");
                break;
            case "quit":
                Log("quit");
                Application.Quit();
                break;
            default:
                Log($"unknown command: {line}");
                break;
        }
    }

    private void Play()
    {
        foreach (FrameworkElement e in Elements())
        {
            if (e.DataContext is MissionBriefViewModel brief && brief.PlayCommand != null)
            {
                brief.PlayCommand.Execute(null);
                Log("play");
                return;
            }
        }
        Log("play: no briefing open");
    }

    // Every element of every Noesis view.
    private static IEnumerable<FrameworkElement> Elements()
    {
        foreach (NoesisView view in Object.FindObjectsByType<NoesisView>(FindObjectsSortMode.None))
        {
            if (view.Content == null)
                continue;
            var stack = new Stack<DependencyObject>();
            stack.Push(view.Content);
            int seen = 0;
            while (stack.Count > 0 && seen++ < 60000)
            {
                DependencyObject d = stack.Pop();
                if (d is FrameworkElement e)
                    yield return e;
                if (!(d is Visual v))
                    continue;
                int n = VisualTreeHelper.GetChildrenCount(v);
                for (int i = 0; i < n; i++)
                    stack.Push(VisualTreeHelper.GetChild(v, i));
            }
        }
    }

    private void MapFull()
    {
        MainGameViewModel vm = Globals._mainGameViewModel;
        if (vm?.Map == null)
        {
            Log("mapfull: no map yet");
            return;
        }
        if (!vm.Map.IsFullscreen || !vm.MapVisible)
            vm.ToggleMapFullscreen();
        Log($"mapfull: visible {vm.MapVisible}, fullscreen {vm.Map.IsFullscreen}");
    }

    // The largest live map control: the one the topo mod decorated, else any in the Noesis trees.
    private void View(double south, double west, double north, double east)
    {
        MapBase map = Maps().OrderByDescending(m => m.ActualWidth * m.ActualHeight).FirstOrDefault();
        if (map == null)
        {
            Log("view: no map control found");
            return;
        }
        map.ZoomToBounds(new BoundingBox(south, west, north, east));
        Log($"view {south},{west} – {north},{east} on a {map.ActualWidth:0}x{map.ActualHeight:0} map");
    }

    private static IEnumerable<MapBase> Maps()
    {
        foreach (NoesisView view in Object.FindObjectsByType<NoesisView>(FindObjectsSortMode.None))
        {
            if (view.Content == null)
                continue;
            var stack = new Stack<DependencyObject>();
            stack.Push(view.Content);
            int seen = 0;
            while (stack.Count > 0 && seen++ < 60000)
            {
                DependencyObject d = stack.Pop();
                if (d is MapBase map && map.ActualWidth > 0)
                {
                    yield return map;
                    continue;
                }
                if (!(d is Visual v))
                    continue;
                int n = VisualTreeHelper.GetChildrenCount(v);
                for (int i = 0; i < n; i++)
                    stack.Push(VisualTreeHelper.GetChild(v, i));
            }
        }
    }

    // The Noesis tree around the largest map control: from UP levels above it down, one line per element with its
    // type, name, place in the window, data context and (for buttons) command, text and tooltip.
    private void Tree(int up)
    {
        MapBase map = Maps().OrderByDescending(m => m.ActualWidth * m.ActualHeight).FirstOrDefault();
        if (map == null)
        {
            Log("tree: no map control found");
            return;
        }
        Visual top = map;
        for (int i = 0; i < up && VisualTreeHelper.GetParent(top) is Visual parent; i++)
            top = parent;
        Visual window = top;
        while (VisualTreeHelper.GetParent(window) is Visual parent)
            window = parent;
        var lines = new List<string>();
        Dump(top, window, 0, lines);
        string file = Path.Combine(Paths.BepInExRootPath, "shotdirector-tree.txt");
        File.WriteAllLines(file, lines);
        Log($"tree: {lines.Count} elements from {up} levels above the map to {file}");
    }

    private void TreeAll()
    {
        var lines = new List<string>();
        foreach (NoesisView view in Object.FindObjectsByType<NoesisView>(FindObjectsSortMode.None))
        {
            if (view.Content is Visual content)
            {
                lines.Add($"== NoesisView {view.name}");
                Dump(content, content, 0, lines);
            }
        }
        string file = Path.Combine(Paths.BepInExRootPath, "shotdirector-tree.txt");
        File.WriteAllLines(file, lines);
        Log($"tree: {lines.Count} elements of every view to {file}");
    }

    // The text an element shows: its string content, or the first text block inside it.
    private static string TextOf(FrameworkElement e)
    {
        if (e is HeaderedItemsControl h && h.Header is string header)
            return header;
        if (e is ContentControl c && c.Content is string content)
            return content;
        var stack = new Stack<DependencyObject>();
        stack.Push(e);
        while (stack.Count > 0)
        {
            DependencyObject d = stack.Pop();
            if (d is TextBlock t && !string.IsNullOrEmpty(t.Text))
                return t.Text;
            if (d is Visual v)
            {
                for (int i = VisualTreeHelper.GetChildrenCount(v) - 1; i >= 0; i--)
                    stack.Push(VisualTreeHelper.GetChild(v, i));
            }
        }
        return null;
    }

    private void Press(string text)
    {
        foreach (FrameworkElement e in Elements())
        {
            if (!(e is ButtonBase || e is MenuItem || e is TabItem) || !e.IsVisible)
                continue;
            string shown = TextOf(e);
            if (shown == null || shown.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            switch (e)
            {
                case MenuItem item when item.HasItems:
                    item.IsSubmenuOpen = true;
                    break;
                case MenuItem item:
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
                    if (item.Command != null && item.Command.CanExecute(item.CommandParameter))
                        item.Command.Execute(item.CommandParameter);
                    break;
                case TabItem tab:
                    tab.IsSelected = true;
                    break;
                case ToggleButton toggle when toggle.Command == null:
                    toggle.IsChecked = toggle.IsChecked != true;
                    toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, toggle));
                    break;
                case ButtonBase button:
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                    if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
                        button.Command.Execute(button.CommandParameter);
                    break;
            }
            Log($"press '{shown}' ({e.GetType().Name})");
            return;
        }
        Log($"press: nothing visible says '{text}'");
    }

    private void Symbols(bool on)
    {
        MapBase map = Maps().OrderByDescending(m => m.ActualWidth * m.ActualHeight).FirstOrDefault();
        if (map == null)
        {
            Log("symbols: no map");
            return;
        }
        int n = 0;
        foreach (DependencyObject d in Descendants(map))
        {
            if (d is FrameworkElement e && (e.GetType().Name == "GizmoLayer" || e.GetType().Name == "MapLabelLayer" || e.GetType().Name == "MapGeometryLayer"))
            {
                e.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                n++;
            }
        }
        Log($"symbols {(on ? "on" : "off")}: {n} layers");
    }

    // The game's windows are elements of a type named Window with their title as the first text inside.
    private void HideWindow(string title)
    {
        foreach (FrameworkElement e in Elements())
        {
            if (e.GetType().Name != "Window" || !e.IsVisible)
                continue;
            string shown = TextOf(e);
            if (shown == null || shown.IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            e.Visibility = Visibility.Collapsed;
            Log($"hide '{shown}'");
            return;
        }
        Log($"hide: no window '{title}'");
    }

    private void Geo(float x, float y)
    {
        MapBase map = Maps().OrderByDescending(m => m.ActualWidth * m.ActualHeight).FirstOrDefault();
        if (map == null)
        {
            Log("geo: no map");
            return;
        }
        Point p = map.PointFromScreen(new Point(x, y));
        bool on = map.TryViewToGeoPosition(p, out GeoPosition precise);
        Point m = map.ViewTransform.ViewToMap(p);
        bool on2 = map.MapProjection.TryMapToGeoPosition(m.X, m.Y, out GeoPosition viaFloat);
        GeoPosition plain = map.ViewToGeoPosition(p);
        Log($"geo screen {x},{y} -> map {p.X:0.0},{p.Y:0.0} of {map.ActualWidth:0}x{map.ActualHeight:0}: " +
            $"precise {precise.Latitude:0.0000},{precise.Longitude:0.0000} ({on}), float {viaFloat.Latitude:0.0000},{viaFloat.Longitude:0.0000} ({on2}), " +
            $"plain {plain.Latitude:0.0000},{plain.Longitude:0.0000}; centre {map.Center}");
    }

    private void OpenMapMenu(bool open)
    {
        MapBase map = Maps().OrderByDescending(m => m.ActualWidth * m.ActualHeight).FirstOrDefault();
        DependencyObject d = map;
        while (d != null && d.GetType().Name != "Window")
            d = VisualTreeHelper.GetParent(d);
        MenuItem item = d == null ? null : Descendants(d).OfType<MenuItem>().FirstOrDefault();
        if (item == null)
        {
            Log("menu: no menu in the map window");
            return;
        }
        item.IsSubmenuOpen = open;
        Log($"menu: {(open ? "opened" : "closed")}, {item.Items.Count} entries");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            DependencyObject d = stack.Pop();
            yield return d;
            if (d is Visual v)
            {
                for (int i = VisualTreeHelper.GetChildrenCount(v) - 1; i >= 0; i--)
                    stack.Push(VisualTreeHelper.GetChild(v, i));
            }
        }
    }

    private static void Dump(Visual v, Visual window, int depth, List<string> lines)
    {
        if (lines.Count > 20000)
            return;
        var e = v as FrameworkElement;
        string where = "";
        try
        {
            Point p = window.PointFromScreen(v.PointToScreen(new Point(0f, 0f)));
            where = e != null ? $" @{p.X:0},{p.Y:0} {e.ActualWidth:0}x{e.ActualHeight:0}" : "";
        }
        catch (Exception) { }
        string text = "";
        if (v is ButtonBase b)
        {
            string command = b.GetBindingExpression(ButtonBase.CommandProperty)?.ParentBinding?.Path?.Path;
            text += $" cmd={command ?? b.Command?.GetType().Name}";
        }
        if (v is ContentControl cc && cc.Content is string s)
            text += $" content=\"{s}\"";
        if (v is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
            text += $" text=\"{tb.Text}\"";
        if (v is HeaderedItemsControl hic && hic.Header is string hs)
            text += $" header=\"{hs}\"";
        if (e?.ToolTip is string tip)
            text += $" tip=\"{tip}\"";
        if (e != null && e.Visibility != Visibility.Visible)
            text += $" {e.Visibility}";
        string context = e?.DataContext != null && (VisualTreeHelper.GetParent(v) as FrameworkElement)?.DataContext != e.DataContext
            ? $" dc={e.DataContext.GetType().Name}" : "";
        lines.Add($"{new string(' ', depth * 2)}{v.GetType().Name}{(string.IsNullOrEmpty(e?.Name) ? "" : " #" + e.Name)}{where}{context}{text}");
        int n = VisualTreeHelper.GetChildrenCount(v);
        for (int i = 0; i < n; i++)
        {
            if (VisualTreeHelper.GetChild(v, i) is Visual child)
                Dump(child, window, depth + 1, lines);
        }
    }

    private static List<ObjectBase> AllUnits() =>
        Object.FindObjectsByType<ObjectBase>(FindObjectsSortMode.None)
            .Where(u => u != null && !u.IsDestroyed && u._taskforce != null)
            .ToList();

    private void Units()
    {
        foreach (ObjectBase u in AllUnits().Where(u => u.IsPlayerObject))
        {
            float m = Utils.unityHeightToMeter(u.transform.position.y);
            Log($"unit '{u.getName()}' [{u.gameObject.name}] {u.GetType().Name} air={u.IsAirUnit} " +
                $"at {u._geoPosition.Latitude:0.000},{u._geoPosition.Longitude:0.000} y={m:0} m");
        }
    }

    private ObjectBase Find(string name)
    {
        ObjectBase unit = AllUnits().FirstOrDefault(u => u.IsPlayerObject && u.getName().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                       ?? AllUnits().FirstOrDefault(u => u.gameObject.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        if (unit == null)
            throw new InvalidOperationException($"no unit '{name}'");
        return unit;
    }

    private void Select(ObjectBase unit)
    {
        Singleton<RenderPosition>.Instance.switchToObject(unit, performSmoothMove: false);
        Log($"selected '{unit.getName()}'");
    }

    private void Pin(ObjectBase unit, float metres)
    {
        pinned = unit;
        pinnedY = Utils.heightInMeterToUnity(metres);
        if (unit is Submarine sub && metres < 0f)
            sub.setDepth(-pinnedY);
        Log($"pinned '{unit.getName()}' at {metres} m");
    }

    // Keeps a pinned unit at its altitude or depth, however its own steering would move it.
    private void Hold()
    {
        if (pinned == null)
            return;
        if (pinned.IsDestroyed)
        {
            pinned = null;
            return;
        }
        Vector3 p = pinned.transform.position;
        p.y = pinnedY;
        pinned.transform.position = p;
        pinned.DesiredAltitude.Value = pinnedY;
        if (pinned.TryGetComponent(out Rigidbody body))
        {
            Vector3 v = body.linearVelocity;
            v.y = 0f;
            body.linearVelocity = v;
            body.position = p;
        }
    }

    private void Topo(string setting, string value)
    {
        Type settings = AppDomain.CurrentDomain.GetAssemblies()
            .Select(asm => asm.GetType("SeaPowerTopo.Settings", throwOnError: false))
            .FirstOrDefault(t => t != null);
        FieldInfo field = settings?.GetField(setting, BindingFlags.Public | BindingFlags.Static);
        if (field == null)
        {
            Log($"topo: no setting {setting}");
            return;
        }
        object entry = field.GetValue(null);
        PropertyInfo prop = entry.GetType().GetProperty("Value");
        Type type = prop.PropertyType;
        object parsed = type.IsEnum ? Enum.Parse(type, value, true) : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        prop.SetValue(entry, parsed);
        Log($"topo {setting} = {parsed}");
    }

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static void Log(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {message}";
        try { File.AppendAllText(LogFile, line + System.Environment.NewLine); }
        catch (IOException) { }
        UnityEngine.Debug.Log("[ShotDirector] " + message);
    }
}

/// <summary>
/// Dotted member paths for get / set / call: a type's full name followed by static and then instance members,
/// e.g. <c>SeaPower.Globals._mainGameViewModel.Map.DisplayMap.Zoom</c>. <c>Singleton</c> types are reached as
/// <c>SeaPower.RenderPosition.@</c> (the singleton instance).
/// </summary>
internal static class Reflect
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    public static object Get(string path)
    {
        object target = Walk(path.Split('.'), out _, out _, last: false);
        return target;
    }

    public static void Set(string path, string value)
    {
        string[] parts = path.Split('.');
        object owner = Walk(parts, out Type ownerType, out string member, last: true);
        MemberInfo m = ownerType.GetMember(member, Any).First();
        Type type = m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType;
        object parsed = Parse(value, type);
        if (m is PropertyInfo prop)
            prop.SetValue(owner, parsed);
        else
            ((FieldInfo)m).SetValue(owner, parsed);
    }

    public static object Call(string path, string[] args)
    {
        string[] parts = path.Split('.');
        object owner = Walk(parts, out Type ownerType, out string member, last: true);
        MethodInfo method = ownerType.GetMethods(Any).First(x => x.Name == member && x.GetParameters().Length >= args.Length &&
                                                                 x.GetParameters().Skip(args.Length).All(pp => pp.IsOptional));
        ParameterInfo[] ps = method.GetParameters();
        object[] values = ps.Select((pp, i) => i < args.Length ? Parse(args[i], pp.ParameterType) : pp.DefaultValue).ToArray();
        return method.Invoke(owner, values);
    }

    // Resolves the longest type-name prefix, then walks the members. With last = true the final member is left
    // for the caller and the object owning it is returned.
    private static object Walk(string[] parts, out Type ownerType, out string member, bool last)
    {
        Type type = null;
        int typeParts = 0;
        for (int n = parts.Length; n > 0 && type == null; n--)
        {
            type = FindType(string.Join(".", parts.Take(n)));
            typeParts = n;
        }
        if (type == null)
            throw new InvalidOperationException($"no type in {string.Join(".", parts)}");
        object current = null;
        int end = last ? parts.Length - 1 : parts.Length;
        for (int k = typeParts; k < end; k++)
        {
            if (parts[k] == "@")
            {
                current = type.GetProperty("Instance", Any)?.GetValue(null)
                          ?? FindType("Singleton`1")?.MakeGenericType(type).GetProperty("Instance", Any)?.GetValue(null);
                continue;
            }
            MemberInfo m = type.GetMember(parts[k], Any).FirstOrDefault()
                           ?? throw new InvalidOperationException($"no member {parts[k]} on {type.Name}");
            current = m is PropertyInfo p ? p.GetValue(current) : m is FieldInfo f ? f.GetValue(current) : throw new InvalidOperationException($"{parts[k]} is a method");
            if (current == null)
                throw new InvalidOperationException($"{parts[k]} is null");
            type = current.GetType();
        }
        ownerType = type;
        member = last ? parts[parts.Length - 1] : null;
        return current;
    }

    private static Type FindType(string name) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(asm => { try { return asm.GetType(name, false); } catch { return null; } })
            .FirstOrDefault(t => t != null);

    private static object Parse(string s, Type type)
    {
        if (type == typeof(string))
            return s;
        if (type.IsEnum)
            return Enum.Parse(type, s, true);
        if (type == typeof(GeoPosition))
        {
            string[] ll = s.Split(',');
            return new GeoPosition(double.Parse(ll[0], CultureInfo.InvariantCulture), double.Parse(ll[1], CultureInfo.InvariantCulture));
        }
        return Convert.ChangeType(s, type, CultureInfo.InvariantCulture);
    }
}

internal static class Native
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    private const uint LeftDown = 0x02, LeftUp = 0x04, KeyUp = 0x02;

    private static readonly Dictionary<string, byte> Keys = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
    {
        ["tab"] = 0x09, ["enter"] = 0x0D, ["esc"] = 0x1B, ["space"] = 0x20, ["f6"] = 0x75, ["m"] = 0x4D,
    };

    public static void Move(int x, int y) => SetCursorPos(x, y);

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    public static void Key(string name)
    {
        byte vk = Keys.TryGetValue(name, out byte k) ? k : Convert.ToByte(name, 16);
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KeyUp, UIntPtr.Zero);
    }
}
