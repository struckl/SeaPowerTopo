using System;
using System.Reflection;
using SeaPower.LiveMap;
using SeapowerUI;

namespace SeaPowerTopo;

/// <summary>
/// The few game members the mod needs that the game keeps internal, looked up once by reflection. Each lookup that
/// fails is logged once and its feature falls back (see the callers).
/// </summary>
internal static class GameApi
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    // Held as a plain Delegate on purpose: Unity's ECS start-up loads every type of every loaded assembly, and a field of
    // type Func<MapPalette, bool> makes this class unloadable on 0.8.7 (no MapPalette there) - ECS then fails and no
    // mission loads, although this globe DLL stays off on 0.8.7. tools/compat-check --classload guards this.
    private static readonly Delegate PaletteDark = Getter<MapPalette, bool>("IsDark");

    private static readonly Type LayerEntry = typeof(GizmoLayerManager).GetNestedType("Entry", BindingFlags.NonPublic);

    private static readonly ConstructorInfo LayerCtor = LayerEntry == null ? null :
        typeof(MapLayer).GetConstructor(Any, null, new[] { typeof(MapDisplayState), LayerEntry.MakeByRefType(), typeof(bool) }, null);

    /// <summary>Whether a map shows its night colours (in Auto: at the centre of the view).</summary>
    public static bool IsDark(MapPalette palette) => palette != null && PaletteDark != null && ((Func<MapPalette, bool>)PaletteDark)(palette);

    /// <summary>A new entry for a map's Layers menu; null if the game's layer type has changed.</summary>
    public static MapLayer NewLayer(MapDisplayState owner, int number, string name, bool visible)
    {
        if (LayerCtor == null)
            return null;
        object entry = Activator.CreateInstance(LayerEntry, number, name, true);
        return (MapLayer)LayerCtor.Invoke(new[] { owner, entry, (object)visible });
    }

    private static Func<T, TResult> Getter<T, TResult>(string property)
    {
        MethodInfo get = typeof(T).GetProperty(property, Any)?.GetGetMethod(nonPublic: true);
        if (get == null)
        {
            ModLog.Warn($"{typeof(T).Name}.{property} not found (game updated?)");
            return null;
        }
        return (Func<T, TResult>)Delegate.CreateDelegate(typeof(Func<T, TResult>), get);
    }
}
