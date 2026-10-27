using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using LogLevel = BepInEx.Logging.LogLevel;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace AnchorChainBetaShim;

/// <summary>
/// Dev tool, never shipped. Anchor Chain's preloader looks for <c>AnchorChain.dll</c> in the game's mod folders once,
/// in its <c>Awake</c>; on the Sea Power public beta (build 261026) the game lists those folders only later, so the
/// search finds nothing and no Anchor Chain mod loads (seapower-modders/AnchorChain#19). This plugin waits until the
/// list holds the folders and then starts Anchor Chain the way the preloader would. It does nothing where the
/// preloader managed on its own.
/// <para>Anchor Chain 1.1.0 also never leaves its load loop once a plugin's entry point throws (it logs "Aborting
/// chainload; unable to load any more plugins" and goes round again), which freezes the game on the beta where some
/// mods fail to patch. While it loads, that log line is turned into an exception that ends the loop – everything that
/// could load has loaded by then.</para>
/// <para>For testing one mod without the others the beta breaks: <c>BepInEx/config/dev.sewerlabs.anchorchainbetashim.only.txt</c>
/// with one mod folder name per line (e.g. <c>SeaPowerTopo</c>, <c>ShotDirector</c>, a Workshop item id) limits Anchor
/// Chain to those folders.</para>
/// </summary>
[BepInPlugin("dev.sewerlabs.anchorchainbetashim", "Anchor Chain beta shim (dev)", "1.0.0")]
public class Shim : BaseUnityPlugin
{
    private const float GiveUpSeconds = 60f;
    private const string StuckLine = "Aborting chainload; unable to load any more plugins";

    private sealed class LoopEnded : Exception
    {
    }

    private static HashSet<string> only;
    private static FieldInfo allDirectories;

    private IEnumerator Start()
    {
        // After the start-up load screen (however long a prompt holds it up): mods started in the middle of it can
        // keep the unit data from loading.
        while (!StartupLoaded())
            yield return new WaitForSecondsRealtime(0.5f);
        float until = Time.realtimeSinceStartup + GiveUpSeconds;
        while (Time.realtimeSinceStartup < until)
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "AnchorChain"))
            {
                Logger.LogInfo("Anchor Chain is loaded, nothing to do.");
                yield break;
            }
            string dll = FindChainloader();
            if (dll != null)
            {
                Start(dll);
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
        Logger.LogError($"No AnchorChain.dll in the game's mod folders after {GiveUpSeconds:0} s.");
    }

    private static bool StartupLoaded() =>
        Singleton<UnitMetadataProvider>.InstanceExists() && Singleton<UnitMetadataProvider>.Instance.UnitData != null &&
        Singleton<LoadScreen>.InstanceExists() && Singleton<LoadScreen>.Instance.LoadText == "Done";

    private static string FindChainloader()
    {
        if (!Singleton<FileManager>.InstanceExists())
            return null;
        foreach (SearchDirectory dir in Singleton<FileManager>.Instance.Directories.ToList())
        {
            DirectoryInfo info = dir.DirectoryInfo;
            if (info == null || !info.Exists)
                continue;
            string dll = Directory.GetFiles(info.FullName, "AnchorChain.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (dll != null)
                return dll;
        }
        return null;
    }

    private void Start(string dll)
    {
        var harmony = new Harmony("dev.sewerlabs.anchorchainbetashim");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ManualLogSource), nameof(ManualLogSource.Log)),
                          prefix: new HarmonyMethod(AccessTools.Method(typeof(Shim), nameof(EndStuckLoop))));
            Assembly assembly = Assembly.LoadFile(dll);
            Type loader = assembly.GetType("AnchorChain.AnchorChainLoader", throwOnError: true);
            string onlyFile = Path.Combine(Paths.ConfigPath, "dev.sewerlabs.anchorchainbetashim.only.txt");
            if (File.Exists(onlyFile))
            {
                only = new HashSet<string>(File.ReadAllLines(onlyFile).Select(l => l.Trim()).Where(l => l.Length > 0),
                                           StringComparer.OrdinalIgnoreCase);
                allDirectories = AccessTools.Field(loader, "_allDirectories");
                harmony.Patch(AccessTools.Method(loader, "Setup"),
                              postfix: new HarmonyMethod(AccessTools.Method(typeof(Shim), nameof(KeepOnly))));
                Logger.LogWarning($"Anchor Chain limited to the folders {string.Join(", ", only)} ({onlyFile}).");
            }
            Logger.LogInfo($"Starting Anchor Chain from {dll}");
            loader.GetMethod("LoadPlugins").Invoke(Activator.CreateInstance(loader), null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is LoopEnded)
        {
            Logger.LogWarning("Anchor Chain could not load every plugin (see the errors above); the others are running.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Starting Anchor Chain failed: {ex}");
        }
        finally
        {
            harmony.UnpatchSelf();
        }
    }

    private static void KeepOnly()
    {
        if (only != null && allDirectories?.GetValue(null) is HashSet<DirectoryInfo> dirs)
            dirs.RemoveWhere(d => !only.Contains(d.Name));
    }

    private static void EndStuckLoop(ManualLogSource __instance, LogLevel level, object data)
    {
        if (level == LogLevel.Error && __instance.SourceName == "AnchorChain" && data is string s && s.StartsWith(StuckLine))
            throw new LoopEnded();
    }
}
