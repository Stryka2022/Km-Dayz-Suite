using Dzl.Core.Projects;

namespace Dzl.Core.Servers;

/// <summary><paramref name="Offline"/> = client-only offline sandbox (no server). Populated by
/// <see cref="Dzl.Core.App.ServerService.List"/> (which loads each instance's config); the raw
/// folder <see cref="Discover"/> path leaves it false.</summary>
public sealed record ServerInstance(
    string Name,
    string Dir,
    string CfgPath,
    bool Offline = false,
    string DisplayName = "",
    string InstallPath = "",
    int Port = 0)
{
    public string FriendlyName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    public string FolderName => Path.GetFileName(Dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}

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

    /// <summary>Pick a collision-free editable game port without making every new instance predictable.</summary>
    public static int RandomPort(IEnumerable<int> used, int min = 2302, int max = 65000)
    {
        if (min < 1024 || max > 65535 || min > max)
            throw new ArgumentOutOfRangeException(nameof(min), "port range must be within 1024..65535");

        var set = new HashSet<int>(used.Where(p => p is >= 1024 and <= 65535));
        if (set.Count >= max - min + 1)
            throw new InvalidOperationException("no free server ports are available");

        for (var attempt = 0; attempt < 4096; attempt++)
        {
            var candidate = Random.Shared.Next(min, max + 1);
            if (!set.Contains(candidate)) return candidate;
        }

        for (var candidate = min; candidate <= max; candidate++)
            if (!set.Contains(candidate)) return candidate;

        throw new InvalidOperationException("no free server ports are available");
    }
}
