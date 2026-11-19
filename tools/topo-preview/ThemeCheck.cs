using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeaPowerTopo.Render;

namespace SeaPowerTopo.Preview;

/// <summary>
/// --check-themes: every built-in look as a theme file, read back, and a file that only names its Base, must come out
/// of the real Raster code exactly as the original does (the Standard looks may differ by a rounding error at most);
/// and "Save this look as a theme file" must behave.
/// </summary>
internal static class ThemeCheck
{
    private static readonly (double lat, double lon, double span, float danger)[] Views =
    {
        (61.5, 8.0, 2.0, float.NaN),
        (36.0, -5.5, 3.0, 600f),
    };

    public static int Run(Terrain terrain, float depthFactor)
    {
        int failures = 0, checks = 0;
        failures += CheckSaving(ref checks);
        foreach (Theme original in Themes.Light.Concat(Themes.Dark).Where(t => !t.Custom).ToList())
            failures += CheckRoundTrip(terrain, original, depthFactor, ref checks);
        Console.WriteLine(failures == 0 ? $"All {checks} checks pass." : $"{failures} failures.");
        return failures == 0 ? 0 : 1;
    }

    private static int CheckRoundTrip(Terrain terrain, Theme original, float depthFactor, ref int checks)
    {
        var warnings = new List<string>();
        string text = ThemeIni.Write(original, original.Name);
        Theme copy = null;
        bool ok = ThemeIni.TryRead(text, original.Id, out ThemeFile file, warnings) && ThemeIni.TryBuild(file, null, out copy, warnings);
        var onlyBase = new ThemeFile { FileName = "x", Id = "x", BaseId = original.Id };
        Theme inherited = null;
        ok = ok && ThemeIni.TryBuild(onlyBase, original, out inherited, warnings);
        if (!ok || warnings.Count > 0)
        {
            Console.WriteLine($"FAIL {original.Id}: {string.Join("; ", warnings)}");
            return 1;
        }

        int failures = 0;
        foreach (var v in Views)
        {
            byte[] reference = Draw(terrain, original, v, depthFactor);
            foreach ((string what, Theme other) in new[] { ("written and read back", copy), ("Base only", inherited) })
            {
                checks++;
                Compare(reference, Draw(terrain, other, v, depthFactor), out int different, out int worst);
                bool pass = different == 0 || (original.Legacy && worst <= 1);
                if (!pass)
                    failures++;
                Console.WriteLine($"{(pass ? "ok  " : "FAIL")} {original.Id,-16} {what,-22} view {v.lat},{v.lon}: {different} values differ, worst {worst}");
            }
        }
        return failures;
    }

    private static void Compare(byte[] a, byte[] b, out int different, out int worst)
    {
        different = 0;
        worst = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d = Math.Abs(a[i] - b[i]);
            if (d > 0)
                different++;
            worst = Math.Max(worst, d);
        }
    }

    // "Save this look as a theme file": a free id each time, the file read back as a theme of its own, the example left alone.
    private static int CheckSaving(ref int checks)
    {
        string dir = Path.Combine(Path.GetTempPath(), "topo-preview-saving");
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        ThemeLibrary.SetFolder(dir);
        ThemeLibrary.Prepare();
        ThemeLibrary.Load(m => { }, m => Console.WriteLine("  " + m));
        int failures = 0, count = 0;
        void Expect(bool pass, string what)
        {
            count++;
            if (!pass)
                failures++;
            Console.WriteLine($"{(pass ? "ok  " : "FAIL")} saving: {what}");
        }

        Expect(File.Exists(Path.Combine(dir, "_example.ini")) && ThemeLibrary.Loaded == 0, "the example file is written and not loaded");
        var warnings = new List<string>();
        Theme black = Themes.Builtin("black");
        Theme first = ThemeLibrary.Save(black, m => { }, m => warnings.Add(m), out string path, out _);
        Expect(first != null && first.Id == "my-black" && first.Name == "My Black" && first.Custom && Path.GetFileName(path) == "my-black.ini", "a built-in look is saved as my-black");
        Theme second = ThemeLibrary.Save(black, m => { }, m => warnings.Add(m), out _, out _);
        Expect(second != null && second.Id == "my-black-2" && second.Name == "My Black 2" && !Themes.Dark.Contains(first) && Themes.Dark.Exists(t => t.Id == "my-black"), "saving again takes a free id");
        Theme third = ThemeLibrary.Save(Themes.Find("my-black-2"), m => { }, m => warnings.Add(m), out _, out _);
        Expect(third != null && third.Id == "my-black-2-2", "saving a file theme makes another file");
        Expect(ThemeLibrary.Loaded == 3 && warnings.Count == 0 && !ThemeLibrary.Changed(), "all three are loaded, no warnings, nothing left to reload");
        File.AppendAllText(Path.Combine(dir, "my-black.ini"), "LandLines=1,2,3\n");
        Expect(ThemeLibrary.Changed(), "an edited file is noticed");
        ThemeLibrary.Load(m => { }, m => { });
        Expect(Themes.Find("my-black").LandLine[0] == 1 && Themes.Dark.Count(t => t.Custom) == 3, "an edited file is read again");
        File.WriteAllText(Path.Combine(dir, "my-black.ini"), "Id=my-black\nBase=nowhere\n");
        ThemeLibrary.Load(m => { }, m => { });
        Expect(Themes.Find("my-black") != null && Themes.Find("my-black").LandLine[0] == 1 && ThemeLibrary.Problems == 1, "a file that breaks keeps the last version that worked");
        File.Delete(Path.Combine(dir, "my-black.ini"));
        ThemeLibrary.Load(m => { }, m => { });
        Expect(Themes.Find("my-black") == null && Themes.Dark.Count(t => t.Custom) == 2, "a deleted file's theme is gone");
        ThemeLibrary.SetFolder(null);
        ThemeLibrary.Load(m => { }, m => { });
        checks += count;
        return failures;
    }

    private static byte[] Draw(Terrain terrain, Theme theme, (double lat, double lon, double span, float danger) v, float depthFactor)
    {
        RasterJob job = Program.NewJob(theme, v.lat, v.lon, v.span, 640, 360, globe: false, depth: true,
                                       Look.DefaultChartSafety, v.danger, float.NaN);
        SourceGrid src = terrain.Fill(job, depthFactor, out _);
        Raster.Resample(src, job, () => false);
        Raster.Shade(job, () => false);
        return job.Rgba;
    }
}
