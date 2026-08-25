using Dzl.Core.Projects;

namespace Dzl.Core.Servers;

/// <summary><paramref name="Offline"/> = client-only offline sandbox (no server). Populated by
/// <see cref="Dzl.Core.App.ServerService.List"/> (which loads each instance's config); the raw
/// folder <see cref="Discover"/> path leaves it false.</summary>
public sealed record ServerInstance(string Name, string Dir, string CfgPath, bool Offline = false);

public static class ServerInstances
{
    public static List<ServerInstance> Discover(string root)
    {
        var list = new List<ServerInstance>();
        var dir = ProjectPaths.ServersDir(root);
        if (!Directory.Exists(dir)) return list;
        foreach (var d in Directory.GetDirectories(dir))
        {
            var cfg = Path.Combine(d, "serverDZ.cfg");
            if (File.Exists(cfg)) list.Add(new ServerInstance(Path.GetFileName(d), d, cfg));
        }
        return list;
    }

    /// <summary>First free port at/after 2302 not in <paramref name="used"/>.</summary>
    public static int NextPort(IEnumerable<int> used)
    {
        var set = new HashSet<int>(used);
        var p = 2302;
        while (set.Contains(p)) p++;
        return p;
    }
}
