using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MapControl;

namespace SeaPowerTopo;

/// <summary>
/// Every game method the mod hooks, in two stages. All targets of a stage are resolved before anything is patched.
/// <list type="bullet">
/// <item><c>MainGameViewModel.OnStart</c> / <c>GameMain.OnDestroy</c> — mission ready / mission unloading. Without
/// them the mod stays out of the way entirely.</item>
/// <item><c>RenderedLayer.OnRender</c> — runs every frame for the map control that shows the live map; it hands
/// over that map so the legend and the cursor readout can be attached to it. Without it only those two are off,
/// the overlay itself works.</item>
/// </list>
/// <para>The map hook must wait until NoesisGUI runs: patching a method JIT-compiles it, and <c>RenderedLayer</c>
/// registers Noesis dependency properties in its static constructor – before Noesis is initialised that is a
/// native crash on startup. <see cref="TopoHost"/> applies it once the first game window host exists (the main
/// menu has one).</para>
/// </summary>
internal static class Patches
{
    private sealed class Hook
    {
        public string Type, Method, Prefix, Postfix;
        public Type[] Args;                   // null = unique by name
    }

    private static readonly Hook[] GameHooks =
    {
        new Hook { Type = "SeapowerUI.ViewModels.MainGameViewModel", Method = "OnStart", Args = Type.EmptyTypes, Postfix = nameof(MissionReady) },
        new Hook { Type = "SeaPower.GameMain", Method = "OnDestroy", Args = Type.EmptyTypes, Prefix = nameof(MissionUnloading) },
    };

    private static readonly Hook[] MapHooks =
    {
        new Hook { Type = "MapControl.RenderedLayer", Method = "OnRender", Postfix = nameof(MapRendered) },
    };

    private static Harmony harmony;

    /// <summary>Stage one, at plugin load: hooks on plain game classes.</summary>
    public static bool ApplyGameHooks(Harmony instance)
    {
        harmony = instance;
        return Apply(GameHooks, "mod inactive");
    }

    /// <summary>Stage two, once NoesisGUI is up: the hook on the game's Noesis map layer.</summary>
    public static bool ApplyMapHooks() => harmony != null && Apply(MapHooks, "no legend or cursor readout");

    private static bool Apply(Hook[] hooks, string consequence)
    {
        var resolved = new List<(Hook hook, MethodBase target)>();
        foreach (Hook hook in hooks)
        {
            Type type = AccessTools.TypeByName(hook.Type);
            MethodBase target = type == null ? null : AccessTools.Method(type, hook.Method, hook.Args);
            if (target == null)
            {
                ModLog.Error($"{hook.Type}.{hook.Method} not found (game updated?) - {consequence}.");
                return false;
            }
            resolved.Add((hook, target));
        }

        foreach ((Hook hook, MethodBase target) in resolved)
        {
            harmony.Patch(target,
                prefix: hook.Prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), hook.Prefix)),
                postfix: hook.Postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), hook.Postfix)));
        }
        return true;
    }

    private static void MissionReady() => TopoHost.Instance?.MissionReady();

    private static void MissionUnloading() => TopoHost.Instance?.MissionEnded();

    private static void MapRendered(RenderedLayer __instance) => TopoHost.Instance?.MapRendered(__instance);
}
