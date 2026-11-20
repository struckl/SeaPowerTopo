using System.IO;
using System.Runtime.CompilerServices;
using AnchorChain;
using BepInEx;
using SeaPowerTopo.Render;

namespace SeaPowerTopo;

/// <summary>
/// Entry point, started by a mod loader (Keel or Anchor Chain) through TriggerEntryPoint. Sea Power exists in two versions with
/// different map code (0.8.7's flat map, and the 3D globe build): the package carries two builds of the same sources,
/// SeaPowerTopo.dll for the globe build and SeaPowerTopoFlat.dll (FLATMAP defined) for 0.8.7. Each stays off on the game it is
/// not made for (ModEntry, in the shared skeleton).
/// </summary>
[ACPlugin(PluginGuid, Name, Version)]
public class Plugin : IAnchorChainMod
{
    public const string Guid = "dev.sewerlabs.seapowertopo";
    public const string Name = "R.A.T.S. Topographic Map";
    public const string Version = "1.7.0";

#if FLATMAP
    // Both loaders skip a plugin whose GUID they have already seen, so the two builds register under different ones.
    public const string PluginGuid = Guid + ".flatmap";
#else
    public const string PluginGuid = Guid;
#endif

    public void TriggerEntryPoint() => ModEntry.Trigger(Start);

    // Apart from the check in Trigger, so nothing in here is compiled on a game it isn't for.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start() => ModEntry.Start<TopoHost>(Settings.Bind, Hooks.Declare, LoadThemes);

    // The player's own themes, before anything asks for a look.
    private static void LoadThemes()
    {
        ThemeLibrary.SetFolder(Path.Combine(Paths.ConfigPath, "SeaPowerTopo", "themes"));
        ThemeLibrary.Prepare();
        ThemeLibrary.Load(ModLog.Info, ModLog.Warn);
    }
}
