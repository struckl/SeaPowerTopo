using System;
using System.IO;
using AnchorChain;
using BepInEx;
using HarmonyLib;
using SeaPowerTopo.Render;
using UnityEngine;

namespace SeaPowerTopo;

/// <summary>
/// Entry point. Loaded by Anchor Chain (the Sea Power community chainloader on top of BepInEx 5), which scans
/// every mod folder — Steam Workshop items and <c>StreamingAssets/&lt;folder&gt;</c> — for classes implementing
/// <see cref="IAnchorChainMod"/>.
/// </summary>
[ACPlugin(Guid, Name, Version)]
public class Plugin : IAnchorChainMod
{
    public const string Guid = "dev.sewerlabs.seapowertopo";
    public const string Name = "Sea Power Topographic Map";
    public const string Version = "1.3.0";

    // Anchor Chain loads assemblies with Assembly.LoadFile, so the same DLL found in two folders would be two
    // separate assemblies with separate statics. The marker lives on the AppDomain so only the first one starts.
    private const string LoadedMarker = Guid + ".loaded";

    public void TriggerEntryPoint()
    {
        if (AppDomain.CurrentDomain.GetData(LoadedMarker) != null)
        {
            ModLog.Warn("Already loaded from another folder; skipping this copy.");
            return;
        }
        AppDomain.CurrentDomain.SetData(LoadedMarker, true);

        try
        {
            // Built for the globe map (public beta of 26 Oct 2026, the November release): older games lack its types.
            if (Type.GetType("SeapowerUI.MapDisplayState, Seapower-Scripts") == null)
            {
                ModLog.Error($"{Name} {Version} needs the Sea Power version with the 3D globe map (public beta from 26 Oct 2026); " +
                             "this game is older - mod inactive. Topographic Map 1.2.0 is the one for it.");
                return;
            }
            Settings.Init();
            if (!Settings.Enabled.Value)
            {
                ModLog.Info("Disabled in config.");
                return;
            }

            // The player's own themes, before anything asks for a look.
            ThemeLibrary.SetFolder(Path.Combine(Paths.ConfigPath, "SeaPowerTopo", "themes"));
            ThemeLibrary.Prepare();
            ThemeLibrary.Load(ModLog.Info, ModLog.Warn);

            // Only the hooks on plain game classes now; the map hook follows once NoesisGUI runs (see Patches).
            if (!Patches.ApplyGameHooks(new Harmony(Guid)))
                return;

            var host = new GameObject("SeaPowerTopo");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<TopoHost>();

            ModLog.Info($"{Name} {Version} loaded.");
        }
        catch (Exception ex)
        {
            // Never take the game down with us.
            ModLog.Error($"Startup failed, mod inactive: {ex}");
        }
    }
}
