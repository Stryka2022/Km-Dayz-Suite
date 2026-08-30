using Dzl.Core.Config;
using Dzl.Core.Projects;
using System.Text.RegularExpressions;

namespace Dzl.Core.App;

/// <summary>Connects locally downloaded Steam Workshop content to one concrete server instance.</summary>
public static class WorkshopInstanceService
{
    private const string ManagedPrefix = "@Workshop_";

    public static string? TryWorkshopId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var parts = path.Replace('/', Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i].Length >= 5 && parts[i].All(char.IsDigit)) return parts[i];
            // Instance-local deployments are named @Workshop_<id>. Keep recognizing legacy
            // numeric cache paths as well as the managed destination.
            var match = Regex.Match(parts[i], @"(?:^|[^0-9])([0-9]{5,})$");
            if (match.Success) return match.Groups[1].Value;
        }
        return null;
    }

    /// <summary>The concrete folder into which a selected server receives a Workshop item.</summary>
    public static string DeploymentDir(string configPath, string instanceName, string workshopId) =>
        Path.Combine(InstanceRoot(configPath, instanceName), ManagedPrefix + workshopId);

    public static IReadOnlyList<string> ConfiguredWorkshopIds(DzlConfig cfg) => cfg.Mods
        .Where(m => m.Enabled)
        .Select(m => TryWorkshopId(m.Path))
        .Where(id => id is not null)
        .Cast<string>()
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Deploy Workshop content into one selected instance, then add that instance-local folder to
    /// its loadout. The shared Steam/steamcmd cache remains the download source only; server configs
    /// never point at it. Deployment is staged before replacing an older copy so an interrupted copy
    /// cannot leave a half-updated active mod.
    /// </summary>
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
            var target = DeploymentDir(configPath, instanceName, workshopId);
            var deployed = DeployCopy(sourceDir, target);
            if (!deployed.Ok) return deployed;

            var cfg = Profiles.Load(instanceName, configPath);
            var mods = cfg.Mods.ToList();
            var index = mods.FindIndex(m =>
                string.Equals(TryWorkshopId(m.Path), workshopId, StringComparison.Ordinal) ||
                string.Equals(Path.GetFullPath(m.Path), Path.GetFullPath(sourceDir), StringComparison.OrdinalIgnoreCase));
            var entry = new ModEntry { Path = target, Enabled = true, Side = "both" };
            if (index >= 0) mods[index] = entry; else mods.Add(entry);

            var copied = copyKeys ? CopyPublicKeys(target, InstanceRoot(configPath, instanceName)) : 0;
            Profiles.Save(cfg with { Mods = mods }, instanceName, configPath);
            return new(true, $"installed Workshop {workshopId} for '{instanceName}' → {target}" +
                             (copyKeys ? $" · {copied} public key(s) copied" : ""));
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    /// <summary>Remove a Workshop item from one server instance's active mod loadout. The shared
    /// Workshop download is deliberately kept on disk so another instance can continue using it and
    /// the item can be enabled again without downloading it.</summary>
    public static OpResult DisableForInstance(string configPath, string instanceName, string workshopId)
    {
        if (!Profiles.List(configPath).Contains(instanceName, StringComparer.OrdinalIgnoreCase))
            return new(false, $"server instance '{instanceName}' was not found");
        if (string.IsNullOrWhiteSpace(workshopId) || !workshopId.All(char.IsDigit))
            return new(false, "a numeric Workshop id is required");

        try
        {
            var cfg = Profiles.Load(instanceName, configPath);
            var removed = cfg.Mods
                .Where(m => string.Equals(TryWorkshopId(m.Path), workshopId, StringComparison.Ordinal))
                .ToList();
            var mods = cfg.Mods.Except(removed).ToList();
            if (mods.Count == cfg.Mods.Count)
                return new(true, $"Workshop {workshopId} is already uninstalled from '{instanceName}'");

            Profiles.Save(cfg with { Mods = mods }, instanceName, configPath);
            var root = InstanceRoot(configPath, instanceName);
            var deleted = 0;
            foreach (var entry in removed)
            {
                if (!IsManagedDeployment(entry.Path, root, workshopId)) continue;
                if (Junction.IsReparsePointEntry(entry.Path)) Junction.Remove(entry.Path);
                else if (Directory.Exists(entry.Path)) Directory.Delete(entry.Path, recursive: true);
                deleted++;
            }
            return new(true, $"uninstalled Workshop {workshopId} from '{instanceName}'" +
                             (deleted > 0 ? " · instance files removed" : "") +
                             " · shared download kept");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    private static string InstanceRoot(string configPath, string instanceName)
    {
        var cfg = Profiles.Load(instanceName, configPath);
        var configured = cfg.ConfigName;
        if (Path.IsPathRooted(configured) && Path.GetDirectoryName(Path.GetFullPath(configured)) is { } dir)
            return dir;
        return Profiles.InstanceDir(instanceName, configPath);
    }

    private static bool IsManagedDeployment(string path, string instanceRoot, string workshopId)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
        var actual = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var expected = Path.GetFullPath(Path.Combine(instanceRoot, ManagedPrefix + workshopId))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static OpResult DeployCopy(string sourceDir, string targetDir)
    {
        var source = Path.GetFullPath(sourceDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(targetDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return new(true, "already deployed");

        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, $".dzl-workshop-stage-{Path.GetFileName(target)}-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".dzl-workshop-backup-{Path.GetFileName(target)}-{Guid.NewGuid():N}");
        try
        {
            CopyTree(source, stage);
            if (Junction.IsReparsePointEntry(target)) Junction.Remove(target);
            else if (Directory.Exists(target)) Directory.Move(target, backup);
            Directory.Move(stage, target);
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            return new(true, "deployed");
        }
        catch (Exception ex)
        {
            try
            {
                if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
                if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            }
            catch { /* preserve the original deployment error */ }
            return new(false, $"could not deploy Workshop files to {target}: {ex.Message}. Stop the server and retry if files are in use");
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
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
