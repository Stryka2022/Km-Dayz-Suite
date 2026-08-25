using System.Text.Json;
using Dzl.Core.Config;
using Dzl.Core.Projects;

namespace Dzl.Core.Mods;

/// <summary>One mod inside a saved loadout: path + side. Everything in a preset is enabled by
/// definition; the list order IS the load order.</summary>
public sealed record ModPresetEntry
{
    public string Path { get; init; } = "";
    public string Side { get; init; } = "both"; // both|server|client
}

/// <summary>A named, server-independent list of active mods (a loadout). Persisted as
/// <c>&lt;ProjectsRoot&gt;\mod-presets\&lt;name&gt;.json</c> (snake_case); the name is the file name.</summary>
public sealed record ModPreset
{
    public string Name { get; init; } = "";
    public List<ModPresetEntry> Mods { get; init; } = new();
}

/// <summary>Mod presets (loadouts) — distinct from server-instance "presets" (<see cref="Profiles"/>).
/// Pure loadout math (<see cref="Apply"/>, <see cref="FromMods"/>, <see cref="IsModified"/>) is split
/// from the thin file I/O (never throws) so it can be unit-tested.</summary>
public static class ModPresetStore
{
    /// <summary>Valid preset name: non-empty after trim, a plain file name (no path separators /
    /// invalid chars / "." / "..").</summary>
    public static bool IsValidName(string? name)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n) || n is "." or "..") return false;
        return n.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
    }

    // Case-insensitive path identity; trailing separators don't count.
    private static string Key(string path) =>
        path.Trim().TrimEnd('\\', '/').ToLowerInvariant();

    /// <summary>Pure: apply a loadout onto a server's mod list. Preset mods go first (preset order,
    /// enabled, side from the preset — an existing entry keeps its stored path string); every other
    /// current mod stays in the list, disabled, in its previous relative order; preset mods not in
    /// the current list are appended as new entries.</summary>
    public static List<ModEntry> Apply(IReadOnlyList<ModEntry> current, ModPreset preset)
    {
        var byKey = new Dictionary<string, ModEntry>();
        foreach (var m in current) byKey.TryAdd(Key(m.Path), m);

        var inPreset = new HashSet<string>();
        var result = new List<ModEntry>();
        foreach (var p in preset.Mods)
        {
            var k = Key(p.Path);
            if (!inPreset.Add(k)) continue;   // duplicate path in the preset file — first wins
            result.Add(byKey.TryGetValue(k, out var existing)
                ? existing with { Enabled = true, Side = p.Side }
                : new ModEntry { Path = p.Path, Enabled = true, Side = p.Side });
        }
        foreach (var m in current)
            if (!inPreset.Contains(Key(m.Path)))
                result.Add(m with { Enabled = false });
        return result;
    }

    /// <summary>Pure: snapshot the enabled mods (in order) as a preset.</summary>
    public static ModPreset FromMods(string name, IReadOnlyList<ModEntry> mods) => new()
    {
        Name = name,
        Mods = mods.Where(m => m.Enabled)
                   .Select(m => new ModPresetEntry { Path = m.Path, Side = m.Side }).ToList(),
    };

    /// <summary>Pure: true when the current enabled loadout (paths + order + sides) no longer
    /// matches the preset — drives the "(modified)" hint in the UI.</summary>
    public static bool IsModified(IReadOnlyList<ModEntry> current, ModPreset preset)
    {
        var live = FromMods(preset.Name, current).Mods;
        if (live.Count != preset.Mods.Count) return true;
        for (int i = 0; i < live.Count; i++)
            if (Key(live[i].Path) != Key(preset.Mods[i].Path) || live[i].Side != preset.Mods[i].Side)
                return true;
        return false;
    }

    // --- File I/O (thin, never throws) -------------------------------------

    /// <summary><c>&lt;ProjectsRoot&gt;\mod-presets</c> for the given config.</summary>
    public static string Dir(string configPath) =>
        ProjectPaths.ModPresetsDir(ProjectPaths.Root(GlobalStore.Load(configPath).ProjectsRoot,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    private static string FileOf(string name, string configPath) =>
        System.IO.Path.Combine(Dir(configPath), name.Trim() + ".json");

    public static List<ModPreset> List(string configPath)
    {
        var dir = Dir(configPath);
        if (!Directory.Exists(dir)) return new();
        var result = new List<ModPreset>();
        foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var p = Read(f);
            if (p is not null) result.Add(p);
        }
        return result;
    }

    public static ModPreset? Load(string name, string configPath)
    {
        if (!IsValidName(name)) return null;
        var dir = Dir(configPath);
        if (!Directory.Exists(dir)) return null;
        // Resolve the on-disk file so the returned Name carries its canonical casing
        // (the FS matches case-insensitively; recorded pointers must match List()).
        var file = Directory.EnumerateFiles(dir, name.Trim() + ".json").FirstOrDefault();
        return file is null ? null : Read(file);
    }

    // The file name is the identity — a stale "name" field inside the JSON never wins.
    private static ModPreset? Read(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var p = JsonSerializer.Deserialize<ModPreset>(File.ReadAllText(file), ConfigStore.Json);
            return p is null ? null : p with { Name = System.IO.Path.GetFileNameWithoutExtension(file) };
        }
        catch { return null; }
    }

    public static (bool Ok, string Message) Save(ModPreset preset, string configPath)
    {
        if (!IsValidName(preset.Name)) return (false, $"invalid mod preset name: '{preset.Name}'");
        try
        {
            var f = FileOf(preset.Name, configPath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f)!);
            File.WriteAllText(f, JsonSerializer.Serialize(preset with { Name = preset.Name.Trim() }, ConfigStore.Json));
            return (true, $"saved mod preset '{preset.Name.Trim()}' ({preset.Mods.Count} mods)");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static bool Delete(string name, string configPath)
    {
        if (!IsValidName(name)) return false;
        var f = FileOf(name, configPath);
        if (!File.Exists(f)) return false;
        try { File.Delete(f); return true; } catch { return false; }
    }
}
