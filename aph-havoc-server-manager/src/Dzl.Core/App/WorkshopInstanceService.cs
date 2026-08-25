using Dzl.Core.Config;

namespace Dzl.Core.App;

/// <summary>Connects locally downloaded Steam Workshop content to one concrete server instance.</summary>
public static class WorkshopInstanceService
{
    public static string? TryWorkshopId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var parts = path.Replace('/', Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
            if (parts[i].Length >= 5 && parts[i].All(char.IsDigit)) return parts[i];
        return null;
    }

    public static IReadOnlyList<string> ConfiguredWorkshopIds(DzlConfig cfg) => cfg.Mods
        .Where(m => m.Enabled)
        .Select(m => TryWorkshopId(m.Path))
        .Where(id => id is not null)
        .Cast<string>()
        .Distinct(StringComparer.Ordinal)
        .ToList();

    public static OpResult EnableForInstance(
        string configPath, string instanceName, string workshopId, string sourceDir, bool copyKeys = true)
    {
        if (!Profiles.List(configPath).Contains(instanceName, StringComparer.OrdinalIgnoreCase))
            return new(false, $"server instance '{instanceName}' was not found");
        if (string.IsNullOrWhiteSpace(workshopId) || !workshopId.All(char.IsDigit))
            return new(false, "a numeric Workshop id is required");
        if (!Directory.Exists(sourceDir))
            return new(false, $"Workshop item {workshopId} is not downloaded yet");

        try
        {
            var cfg = Profiles.Load(instanceName, configPath);
            var mods = cfg.Mods.ToList();
            var index = mods.FindIndex(m =>
                string.Equals(TryWorkshopId(m.Path), workshopId, StringComparison.Ordinal) ||
                string.Equals(Path.GetFullPath(m.Path), Path.GetFullPath(sourceDir), StringComparison.OrdinalIgnoreCase));
            var entry = new ModEntry { Path = Path.GetFullPath(sourceDir), Enabled = true, Side = "both" };
            if (index >= 0) mods[index] = entry; else mods.Add(entry);

            var copied = copyKeys ? CopyPublicKeys(sourceDir, Profiles.InstanceDir(instanceName, configPath)) : 0;
            Profiles.Save(cfg with { Mods = mods }, instanceName, configPath);
            return new(true, $"enabled Workshop {workshopId} for '{instanceName}'" +
                             (copyKeys ? $" · {copied} public key(s) copied" : ""));
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public static int CopyPublicKeys(string modDir, string instanceDir)
    {
        if (!Directory.Exists(modDir)) return 0;
        var keys = Directory.EnumerateFiles(modDir, "*.bikey", SearchOption.AllDirectories).ToList();
        if (keys.Count == 0) return 0;
        var target = Path.Combine(instanceDir, "keys");
        Directory.CreateDirectory(target);
        var copied = 0;
        foreach (var source in keys)
        {
            var dest = Path.Combine(target, Path.GetFileName(source));
            if (File.Exists(dest) && File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(dest))) continue;
            File.Copy(source, dest, overwrite: true);
            copied++;
        }
        return copied;
    }
}
