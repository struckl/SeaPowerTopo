global using Rats.Shared;

namespace Rats.Shared;

/// <summary>What the shared skeleton (the R.A.T.S. Shared repo SeaPowerShared, compiled into this DLL) needs to know about this mod.</summary>
internal static class ModInfo
{
    /// <summary>Config file name, Harmony id and the loaded-once marker.</summary>
    public const string Guid = SeaPowerTopo.Plugin.Guid;

    public const string Name = SeaPowerTopo.Plugin.Name;
    public const string Version = SeaPowerTopo.Plugin.Version;

    /// <summary>The log source and the host object's name: the assembly's base name, as before the skeleton.</summary>
    public const string LogSource = "SeaPowerTopo";
}
