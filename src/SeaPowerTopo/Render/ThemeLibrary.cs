using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SeaPowerTopo.Render;

/// <summary>
/// The player's own themes: the <c>.ini</c> files in the themes folder, read into <see cref="Theme"/>s that join the
/// built-in ones in <see cref="Themes"/>. Plain C# like the rest of the theme code (no BepInEx, no Unity), so the
/// offline preview loads files with the very same code.
/// <para>A file that cannot be read never reaches the game: it is skipped with a line in the log, and a theme whose
/// file broke while the map was running keeps its last working version.</para>
/// </summary>
internal static class ThemeLibrary
{
    private const int MaxFiles = 100, MaxBytes = 64 * 1024, MaxBaseDepth = 8;

    private static string folder, signature = "";
    private static readonly Dictionary<string, Theme> lastGood = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many themes came from files at the last load, and how many files had problems.</summary>
    public static int Loaded { get; private set; }
    public static int Problems { get; private set; }

    public static string Folder => folder;

    public static void SetFolder(string path) => folder = path;

    /// <summary>Makes sure the folder is there and holds the documented example.</summary>
    public static void Prepare()
    {
        try
        {
            Directory.CreateDirectory(folder);
            string example = Path.Combine(folder, "_example.ini");
            if (!File.Exists(example))
                File.WriteAllText(example, ThemeIni.Example, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // A read-only folder only means no example file.
        }
    }

    // Name, size and write time of every theme file: changes whenever one is added, removed or saved.
    private static string Signature()
    {
        var sb = new StringBuilder();
        foreach (string path in Files())
        {
            var info = new FileInfo(path);
            sb.Append(info.Name).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
        }
        return sb.ToString();
    }

    private static List<string> Files()
    {
        var files = new List<string>();
        if (folder == null || !Directory.Exists(folder))
            return files;
        foreach (string path in Directory.GetFiles(folder, "*.ini"))
        {
            if (!Path.GetFileName(path).StartsWith("_", StringComparison.Ordinal))
                files.Add(path);
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    // The theme files were added, changed or removed since the last load; a folder that cannot be listed counts as unchanged.
    public static bool Changed()
    {
        try
        {
            return Signature() != signature;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Reads all theme files and replaces the custom themes with them. Every problem goes to <paramref name="warnTo"/>.</summary>
    public static void Load(Action<string> info, Action<string> warnTo)
    {
        var customs = new List<Theme>();
        // Files with problems: every warning names the file it is about.
        var bad = new HashSet<string>();
        void Warn(string file, string message)
        {
            warnTo($"{file}: {message}");
            bad.Add(file);
        }
        try
        {
            signature = Signature();
            var byId = new Dictionary<string, ThemeFile>();
            List<ThemeFile> files = ReadAll(byId, Warn);
            BuildAll(files, byId, customs, Warn);
        }
        catch (Exception ex)
        {
            Warn("Theme files could not be loaded", ex.Message);
        }

        Themes.ReplaceCustom(customs);
        Loaded = customs.Count;
        Problems = bad.Count;
        info($"Theme files: {customs.Count} loaded from {folder}" + (bad.Count > 0 ? $", {bad.Count} with problems (see above)" : ""));
    }

    // Every theme file in the folder read and checked for a usable, free id (at most MaxFiles of them).
    private static List<ThemeFile> ReadAll(Dictionary<string, ThemeFile> byId, Action<string, string> warn)
    {
        var files = new List<ThemeFile>();
        foreach (string path in Files())
        {
            string name = Path.GetFileName(path);
            if (files.Count >= MaxFiles)
            {
                warn(name, $"more than {MaxFiles} theme files, the rest are ignored");
                break;
            }
            ThemeFile file = ReadOne(path, name, warn);
            if (file == null)
                continue;
            if (Themes.Builtin(file.Id) != null)
            {
                warn(name, $"the id '{file.Id}' belongs to a look that ships with the mod; give the file an Id of its own");
                continue;
            }
            if (byId.ContainsKey(file.Id))
            {
                warn(name, $"the id '{file.Id}' is already used by {byId[file.Id].FileName}.ini, ignored");
                continue;
            }
            files.Add(file);
            byId[file.Id] = file;
        }
        return files;
    }

    private static ThemeFile ReadOne(string path, string name, Action<string, string> warn)
    {
        string text;
        try
        {
            if (new FileInfo(path).Length > MaxBytes)
            {
                warn(name, $"larger than {MaxBytes / 1024} KB, ignored");
                return null;
            }
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            warn(name, $"cannot be read ({ex.Message})");
            return null;
        }
        var messages = new List<string>();
        bool ok = ThemeIni.TryRead(text, Path.GetFileNameWithoutExtension(path), out ThemeFile file, messages);
        foreach (string m in messages)
            warn(name, m);
        return ok ? file : null;
    }

    // Each file built on its base; a file that no longer builds keeps the last version of it that did.
    private static void BuildAll(List<ThemeFile> files, Dictionary<string, ThemeFile> byId, List<Theme> customs, Action<string, string> warn)
    {
        var built = new Dictionary<string, Theme>();
        var failed = new HashSet<string>();
        foreach (ThemeFile file in files)
        {
            Theme theme = Resolve(file, byId, built, failed, new HashSet<string>(), 0, warn);
            if (theme == null && lastGood.TryGetValue(file.FileName, out Theme old) && !built.ContainsKey(old.Id))
            {
                warn(file.FileName + ".ini", "keeping the last version that worked");
                built[old.Id] = theme = old;
            }
            if (theme != null)
            {
                lastGood[file.FileName] = theme;
                customs.Add(theme);
            }
        }
    }

    private static Theme Resolve(ThemeFile file, Dictionary<string, ThemeFile> byId, Dictionary<string, Theme> built,
                                 HashSet<string> failed, HashSet<string> chain, int depth, Action<string, string> warn)
    {
        if (built.TryGetValue(file.Id, out Theme done))
            return done;
        if (failed.Contains(file.Id))
            return null;
        string name = file.FileName + ".ini";
        Theme baseTheme = null;
        if (file.BaseId != null)
        {
            baseTheme = ResolveBase(file, byId, built, failed, chain, depth, warn);
            if (baseTheme == null)
            {
                failed.Add(file.Id);
                return null;
            }
        }

        var messages = new List<string>();
        bool ok = ThemeIni.TryBuild(file, baseTheme, out Theme theme, messages);
        foreach (string m in messages)
            warn(name, m);
        if (!ok)
        {
            failed.Add(file.Id);
            return null;
        }
        built[file.Id] = theme;
        return theme;
    }

    // The theme a file's Base names: another file's (built first) or one that ships; null with a warning if neither.
    private static Theme ResolveBase(ThemeFile file, Dictionary<string, ThemeFile> byId, Dictionary<string, Theme> built,
                                     HashSet<string> failed, HashSet<string> chain, int depth, Action<string, string> warn)
    {
        string name = file.FileName + ".ini";
        if (!byId.TryGetValue(file.BaseId, out ThemeFile baseFile))
        {
            Theme shipped = Themes.Builtin(file.BaseId);
            if (shipped == null)
                warn(name, $"unknown Base '{file.BaseId}' (the id of a look that ships, or the Id of another theme file)");
            return shipped;
        }
        if (depth >= MaxBaseDepth || chain.Contains(file.Id) || chain.Contains(baseFile.Id))
        {
            warn(name, $"Base={file.BaseId} leads in a circle or is nested too deeply");
            return null;
        }
        chain.Add(file.Id);
        Theme baseTheme = Resolve(baseFile, byId, built, failed, chain, depth + 1, warn);
        chain.Remove(file.Id);
        if (baseTheme == null)
            warn(name, $"its Base '{file.BaseId}' did not load");
        return baseTheme;
    }

    /// <summary>
    /// Writes the theme as a new theme file of its own (a free id and file name made from its id) and loads it.
    /// Returns the new theme, or null with the reason in <paramref name="error"/>.
    /// </summary>
    public static Theme Save(Theme source, Action<string> info, Action<string> warn, out string path, out string error)
    {
        path = null;
        error = null;
        try
        {
            Prepare();
            string stem = source.Custom ? source.Id : "my-" + source.Id;
            string id = stem;
            for (int n = 2; Themes.Find(id) != null || File.Exists(Path.Combine(folder, id + ".ini")); n++)
                id = stem + "-" + n;

            Theme copy = source.Clone();
            copy.Id = id;
            string name = source.Custom ? source.Name : "My " + source.Name;
            copy.Name = id == stem ? name : name + " " + id.Substring(stem.Length + 1);
            path = Path.Combine(folder, id + ".ini");
            File.WriteAllText(path, ThemeIni.Write(copy, source.Name), new UTF8Encoding(false));
            Load(info, warn);
            Theme saved = Themes.Find(id);
            if (saved == null)
                error = "the saved file could not be read back, see the log";
            return saved;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
