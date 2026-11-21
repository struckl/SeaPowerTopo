using MapControl;

namespace SeaPowerTopo;

/// <summary>
/// Every game method the mod hooks (Rig.Hooks, R.A.T.S. Rigging):
/// - MainGameViewModel.OnStart / GameMain.OnDestroy - mission ready / mission unloading (Rig.Mission), at load. Without
/// them the mod stays out of the way entirely.
/// - RenderedLayer.OnRender - runs every frame for the map control that shows the live map; it hands over that map so the
/// legend and the cursor readout can be attached to it. Without it only those two are off, the overlay itself works.
/// Applied once NoesisGUI runs: patching a method JIT-compiles it, and RenderedLayer registers Noesis dependency properties
/// in its static constructor - before Noesis is initialised that is a native crash on startup.
/// </summary>
internal static class Hooks
{
    public static void Declare()
    {
        Rig.Mission.Declare();
        Rig.Hooks.Add("Map", HookStage.AfterNoesis, "no legend or cursor readout")
            .Hook("MapControl.RenderedLayer", "OnRender", null, typeof(Hooks), postfix: nameof(MapRendered));
    }

    private static void MapRendered(RenderedLayer __instance) => TopoHost.Instance?.MapRendered(__instance);
}
