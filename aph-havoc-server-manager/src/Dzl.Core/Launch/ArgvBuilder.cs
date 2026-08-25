using Dzl.Core.Config;

namespace Dzl.Core.Launch;

public static class ArgvBuilder
{
    public static List<string> ModsForTarget(DzlConfig c, string target) =>
        c.Mods.Where(m => m.Enabled).Where(m =>
            (target == "server" && m.Side == "both") ||
            (target == "client" && (m.Side is "both" or "client")))
         .Select(m => m.Path).ToList();

    public static List<string> ServerOnlyMods(DzlConfig c) =>
        c.Mods.Where(m => m.Enabled && m.Side == "server").Select(m => m.Path).ToList();

    public static List<string> ParamsFor(DzlConfig c, string target, string mode) => (target, mode) switch
    {
        ("server", "debug")  => c.ServerParamsDebug,
        ("server", "normal") => c.ServerParamsNormal,
        ("client", "debug")  => c.ClientParamsDebug,
        ("client", "normal") => c.ClientParamsNormal,
        _ => new()
    };

    private static string ProfilesArg(string path, string dayz)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(dayz);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, full)
            : full;
    }

    /// <summary>Working directory to launch a target from.</summary>
    /// <remarks>DayZ resolves <c>-config</c> and the mission template <b>relative to the working
    /// directory</b> (it rejects an absolute <c>-config</c>). When a server instance keeps its own
    /// <c>serverDZ.cfg</c> at an absolute path (under ProjectsRoot), we run the server <b>from that
    /// instance dir</b> so its serverDZ.cfg + mpmissions are picked up. Otherwise the server uses the
    /// game install (debug) or dedicated server install (normal); the client always uses the game install.</remarks>
    public static string WorkingDir(DzlConfig c, string target, string mode)
    {
        if (target == "server" && Path.IsPathRooted(c.ConfigName))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(c.ConfigName));
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        }
        if (target == "server" && mode == "normal")
            return ProcessManager.ServerInstallPath(c);
        return c.DayzPath;
    }

    /// <summary>The <c>-config</c> value, passed through as-is. DayZ 1.29 accepts an absolute path (verified
    /// live) and the engine forces <c>$currentdir</c> to the exe dir regardless of the launcher's working
    /// directory — so an absolute instance path is the only way its own serverDZ.cfg is honored. A relative
    /// value still resolves against the install dir (the engine's current dir).</summary>
    private static string ConfigArg(string configName) => configName;

    /// <summary>Build the launch argv. <paramref name="connect"/> applies to the client only:
    /// false drops <c>-connect</c> AND <c>-port</c> (the port is the connect port — dead weight
    /// without it) — mods and mission still load, but the game stays in the main menu
    /// (DayZDiag boots the mission offline) instead of auto-joining the server.</summary>
    public static List<string> Build(string mode, string target, DzlConfig c, bool connect = true)
    {
        if (target == "server")
        {
            var a = new List<string>();
            if (mode == "debug") a.Add("-server");
            var profilesBase = mode == "normal" ? ProcessManager.ServerInstallPath(c) : c.DayzPath;
            a.Add($"-profiles={ProfilesArg(c.ProfilesPath, profilesBase)}");
            a.Add($"-mod={string.Join(';', ModsForTarget(c, "server"))}");
            a.Add($"-config={ConfigArg(c.ConfigName)}");
            a.Add($"-port={c.Port}");
            var so = ServerOnlyMods(c);
            if (so.Count > 0) a.Add($"-serverMod={string.Join(';', so)}");
            a.AddRange(ParamsFor(c, "server", mode));
            return a;
        }
        if (target == "client")
        {
            var a = new List<string>
            {
                $"-profiles={ProfilesArg(c.ClientProfilesPath, c.DayzPath)}",
                $"-mod={string.Join(';', ModsForTarget(c, "client"))}",
                $"-mission={c.Mission}",
            };
            if (connect)
            {
                a.Add($"-connect={c.ConnectIp}");
                a.Add($"-port={c.Port}");
            }
            a.Add($"-name={c.PlayerName}");
            a.AddRange(ParamsFor(c, "client", mode));
            return a;
        }
        throw new ArgumentException($"unknown target: {target}");
    }
}
