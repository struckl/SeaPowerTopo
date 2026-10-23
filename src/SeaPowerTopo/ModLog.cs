using BepInEx.Logging;

namespace SeaPowerTopo;

/// <summary>Thin wrapper around a BepInEx log source; everything lands in <c>BepInEx/LogOutput.log</c>.</summary>
internal static class ModLog
{
    private static readonly ManualLogSource Source = Logger.CreateLogSource("SeaPowerTopo");

    public static void Info(string message) => Source.LogInfo(message);
    public static void Warn(string message) => Source.LogWarning(message);
    public static void Error(string message) => Source.LogError(message);

    public static void Debug(string message)
    {
        if (Settings.VerboseLog != null && Settings.VerboseLog.Value)
            Source.LogInfo("[dbg] " + message);
    }
}
