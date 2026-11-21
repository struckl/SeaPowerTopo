global using Rats.Rigging;
using System;
using BepInEx.Configuration;

namespace SeaPowerTopo;

/// <summary>This mod's R.A.T.S. Rigging object and its parts, for the code that is not handed one.</summary>
internal static class Rig
{
    public static RigMod Mod { get; private set; }

    public static RigLog Log => Mod.Log;

    public static RigSettings Settings => Mod.Settings;

    public static RigHooks Hooks => Mod.Hooks;

    public static RigMission Mission => Mod.Mission;

    public static RigOptions Options => Mod.Options;

    /// <summary>The mod's start, from Plugin: makes the mod's Rigging object, then starts the mod.</summary>
    public static void Start<THost>(Action<ConfigFile> bindSettings, Action declareHooks, Action steps = null) where THost : RigHost
    {
        Mod = RigMod.Create(new RigInfo(Plugin.Guid, Plugin.Name, Plugin.Version, "SeaPowerTopo", typeof(Plugin).Assembly));
        Mod.Start<THost>(bindSettings, declareHooks, steps);
    }
}
