using Dzl.Core.Bases;
using Dzl.Core.Config;
using Dzl.Core.Env;
using Dzl.Core.Mods;
using Dzl.Core.Projects;
using Dzl.Core.Servers;

namespace Dzl.Core.App;

public sealed record CreateServerResult(bool Ok, string Name, string Dir, int Port, string Message);

public sealed class ServerService
{
    private readonly string _configPath;
    public ServerService(string configPath) { _configPath = configPath; }

    /// <summary>All server instances — an instance IS a config preset, so this enumerates the
    /// presets and derives each entry's Dir/CfgPath from its serverDZ.cfg path.</summary>
    public IReadOnlyList<ServerInstance> List()
    {
        var list = new List<ServerInstance>();
        foreach (var name in Profiles.List(_configPath))
        {
            try
            {
                var cfg = Profiles.Load(name, _configPath);
                var cfgPath = cfg.ConfigName;
                var dir = Path.GetDirectoryName(cfgPath) ?? "";
                list.Add(new ServerInstance(name, dir, cfgPath, cfg.OfflineMode,
                    cfg.DisplayName, cfg.ServerInstallPathOverride, cfg.Port));
            }
            catch { /* skip unreadable instance */ }
        }
        return list;
    }

    /// <summary>Scaffold a new server instance (from a base/template if given, else from the DayZ install)
    /// and save it as a preset (atomically), optionally activating it.</summary>
    public CreateServerResult Create(string name, string map, int? port = null, bool activate = true,
                                     string? baseName = null, string? modPreset = null, bool offline = false,
                                     string? displayName = null, string? instanceFolderName = null,
                                     string? serverInstallPathOverride = null, string? connectIp = null)
    {
        Profiles.EnsureDefault(_configPath);
        var friendlyName = string.IsNullOrWhiteSpace(displayName) ? name.Trim() : displayName.Trim();
        if (friendlyName.Length == 0)
            return new CreateServerResult(false, name, "", 0, "enter a server display name");
        var safeName = ProjectPaths.SafeInstanceName(
            string.IsNullOrWhiteSpace(instanceFolderName) ? name : instanceFolderName);

        var (baseCfg, _, _) = Profiles.ResolveActive(_configPath);
        var root = ProjectPaths.Root(baseCfg);
        var instanceDir = ProjectPaths.ServerDir(root, safeName);
        var template = MapAliases.MissionTemplate(map);

        if (Profiles.List(_configPath).Contains(safeName, StringComparer.OrdinalIgnoreCase))
            return new CreateServerResult(false, safeName, instanceDir, 0,
                $"an instance using folder/profile name '{safeName}' already exists");
        if (Directory.Exists(instanceDir))
            return new CreateServerResult(false, safeName, instanceDir, 0,
                $"the folder '{instanceDir}' already exists; choose another folder name");

        var usedPorts = Profiles.List(_configPath)
            .Select(n => { try { return Profiles.Load(n, _configPath).Port; } catch { return 0; } })
            .Where(p => p > 0)
            .ToHashSet();
        if (port is < 1024 or > 65535)
            return new CreateServerResult(false, safeName, instanceDir, 0, "port must be between 1024 and 65535");
        if (port is { } requestedPort && usedPorts.Contains(requestedPort))
            return new CreateServerResult(false, safeName, instanceDir, 0,
                $"port {requestedPort} is already used by another instance");
        var chosenPort = port ?? ServerInstances.RandomPort(usedPorts);

        string sourceNote;
        if (!string.IsNullOrWhiteSpace(baseName) && ServerBases.Exists(root, baseName!))
        {
            // From a base/template: copy its serverDZ.cfg + mpmissions, then add the runtime bits.
            ServerBases.CopyInto(root, baseName!, instanceDir);
            Directory.CreateDirectory(Path.Combine(instanceDir, "profiles"));
            Directory.CreateDirectory(Path.Combine(instanceDir, "profiles_client"));
            ServerScaffold.EnsureFilePatching(Path.Combine(instanceDir, "serverDZ.cfg"));
            sourceNote = $"from base '{baseName}'";
        }
        else
        {
            var report = ServerScaffold.Scaffold(baseCfg.DayzPath, instanceDir, template);
            sourceNote = $"from DayZ install; {report.Notes}".TrimEnd(';', ' ');
        }

        // The new instance's own mission folder (the one just scaffolded/copied), or the expected path if
        // none landed on disk. Used to repoint BOTH dzl's per-instance Mission and the serverDZ.cfg template.
        var instMpm = Path.Combine(instanceDir, "mpmissions");
        var instMission = (Directory.Exists(instMpm) ? Directory.GetDirectories(instMpm).FirstOrDefault() : null)
                          ?? Path.Combine(instMpm, template);

        // Point the serverDZ.cfg template at this instance's mission (absolute) — DayZ forces $currentdir to
        // the exe dir, so a bare name (from DefaultServerCfg or a copied base) would load the install's mission.
        ServerScaffold.EnsureAbsoluteTemplate(Path.Combine(instanceDir, "serverDZ.cfg"), instMission);
        ServerScaffold.EnsureHostname(Path.Combine(instanceDir, "serverDZ.cfg"), friendlyName);

        // Repoint Mission at the new instance. ServerPreset.Build can't (it's pure, no disk) — without this
        // the new instance inherits the active preset's Mission, which may be an absolute path to ANOTHER
        // instance, so every new server would point at the old one's mpmissions.
        var cfg = ServerPreset.Build(baseCfg, instanceDir, chosenPort) with
        {
            Mission = instMission,
            OfflineMode = offline,
            DisplayName = friendlyName,
            InstanceFolderName = safeName,
            ServerInstallPathOverride = (serverInstallPathOverride ?? "").Trim(),
            ConnectIp = string.IsNullOrWhiteSpace(connectIp) ? ServerNetwork.DetectConnectIp() : connectIp.Trim(),
            Mode = !offline && !string.IsNullOrWhiteSpace(serverInstallPathOverride) ? "normal" : baseCfg.Mode,
        };
        if (!string.IsNullOrWhiteSpace(modPreset))
        {
            if (ModPresetStore.Load(modPreset!, _configPath) is { } loadout)
                cfg = cfg with { Mods = ModPresetStore.Apply(cfg.Mods, loadout), ModPreset = loadout.Name };
            else
                sourceNote += $"; mod preset '{modPreset}' not found — not applied";
        }
        Profiles.Save(cfg, safeName, _configPath);
        if (activate) Profiles.SetActive(safeName, _configPath);

        return new CreateServerResult(true, safeName, instanceDir, chosenPort,
            $"'{friendlyName}' created as '{safeName}' {sourceNote}");
    }

    /// <summary>List the available bases (templates) under the active ProjectsRoot.</summary>
    public IReadOnlyList<BaseInfo> ListBases()
    {
        var (cfg, _, _) = Profiles.ResolveActive(_configPath);
        return ServerBases.List(ProjectPaths.Root(cfg));
    }
}
