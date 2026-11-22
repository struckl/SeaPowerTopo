using System.IO;
using System.Runtime.CompilerServices;
using AnchorChain;
using BepInEx;
using SeaPowerTopo.Render;

namespace SeaPowerTopo;

/// <summary>
/// Entry point, run by R.A.T.S. Keel (it calls TriggerEntryPoint of an [ACPlugin] class). The start itself is R.A.T.S. Rigging's:
/// the plugin names it as a dependency, so Keel starts it first and keeps this mod out when it is missing or too old.
/// </summary>
[ACPlugin(Guid, Name, Version)]
[ACDependency("dev.sewerlabs.seapowerrigging", "1.0.0", null)]
public class Plugin : IAnchorChainMod
{
    public const string Guid = "dev.sewerlabs.seapowertopo";
    public const string Name = "R.A.T.S. Topographic Map";
    public const string Version = "1.8.1";

    public void TriggerEntryPoint() => Start();

    // Apart from the entry point, so this class has no Rigging type in it and loads on its own.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start() => Rig.Start<TopoHost>(Settings.Bind, Hooks.Declare, LoadThemes);

    // The player's own themes, before anything asks for a look.
    private static void LoadThemes()
    {
        ThemeLibrary.SetFolder(Path.Combine(Paths.ConfigPath, "SeaPowerTopo", "themes"));
        ThemeLibrary.Prepare();
        ThemeLibrary.Load(Rig.Log.Info, Rig.Log.Warn);
    }
}
