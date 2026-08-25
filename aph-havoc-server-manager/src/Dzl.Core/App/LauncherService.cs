using Dzl.Core.Config;
using Dzl.Core.Economy;
using Dzl.Core.Env;
using Dzl.Core.Launch;
using Dzl.Core.Logs;
using Dzl.Core.Mods;
using Dzl.Core.Projects;

namespace Dzl.Core.App;

public sealed record TargetState(string State, string? Source, string? Mode, int? Pid);
public sealed record StatusReport(
    string Mode, int Port, string? ActivePreset,
    TargetState Server, TargetState Client,
    IReadOnlyDictionary<string, string> Paths,
    IReadOnlyList<ModView> Mods,
    IReadOnlyDictionary<string, string?> Logs);
public sealed record ModView(string Path, string Side);
public sealed record PresetView(string Name, bool Active);
public sealed record ModPresetView(string Name, int ModCount, bool Active);
public sealed record LogsResult(string Which, string? Path, IReadOnlyList<string> Lines);
public sealed record OpResult(bool Ok, string Message);

public sealed class LauncherService
{
    private readonly string _configPath;
    public LauncherService(string configPath) { _configPath = configPath; }

    private (DzlConfig cfg, string savePath, string active) Resolve()
    {
        Profiles.EnsureDefault(_configPath);
        return Profiles.ResolveActive(_configPath);
    }

    private TargetState TargetOf(IReadOnlyDictionary<string, ProcInfo> live, string t)
        => live.TryGetValue(t, out var i)
            ? new TargetState("up", i.Source, i.Mode, i.Pid)
            : new TargetState("down", null, null, null);

    public StatusReport Status()
    {
        var (cfg, _, active) = Resolve();
        var live = StateFile.ReadLive(_configPath, ProcessManager.ImageOf);
        var logs = LogResolver.Resolve(cfg.ProfilesPath, cfg.ClientProfilesPath);
        var paths = new Dictionary<string, string>
        {
            ["dayz_path"] = cfg.DayzPath,
            ["dayz_server_path"] = ProcessManager.ServerInstallPath(cfg),
            ["profiles_path"] = cfg.ProfilesPath,
            ["client_profiles_path"] = cfg.ClientProfilesPath,
            ["config_dir"] = Path.GetDirectoryName(_configPath) ?? ".",
            ["presets_dir"] = Profiles.PresetsDir(_configPath),
            ["projects_root"] = ProjectPaths.Root(cfg),
        };
        var mods = cfg.Mods.Where(m => m.Enabled).Select(m => new ModView(m.Path, m.Side)).ToList();
        return new StatusReport(cfg.Mode, cfg.Port, string.IsNullOrEmpty(active) ? null : active,
            TargetOf(live, "server"), TargetOf(live, "client"), paths, mods, logs);
    }

    public IReadOnlyList<ModView> Mods()
    {
        var (cfg, _, _) = Resolve();
        return cfg.Mods.Where(m => m.Enabled).Select(m => new ModView(m.Path, m.Side)).ToList();
    }

    public IReadOnlyList<PresetView> Presets()
    {
        var (_, _, active) = Resolve();
        return Profiles.List(_configPath).Select(n => new PresetView(n, n == active)).ToList();
    }

    public OpResult SetPreset(string name)
    {
        if (!Profiles.List(_configPath).Contains(name)) return new OpResult(false, $"no preset '{name}'");
        Profiles.SetActive(name, _configPath);
        return new OpResult(true, $"active preset -> '{name}'");
    }

    public OpResult SaveActivePresetAs(string name)
    {
        var (cfg, _, _) = Resolve();
        Profiles.Save(cfg, name, _configPath);
        Profiles.SetActive(name, _configPath);
        return new OpResult(true, $"saved & active: '{name}'");
    }

    // --- Mod presets (loadouts) — named mod lists, independent of server instances ---

    private string ActiveInstanceName()
    {
        var (_, _, active) = Resolve();
        return string.IsNullOrEmpty(active) ? "default" : active;
    }

    public IReadOnlyList<ModPresetView> ModPresets()
    {
        var (cfg, _, _) = Resolve();
        return ModPresetStore.List(_configPath)
            .Select(p => new ModPresetView(p.Name, p.Mods.Count,
                string.Equals(p.Name, cfg.ModPreset, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Snapshot the active server's enabled mods as a named loadout (overwrites an
    /// existing preset of the same name) and record it as the instance's applied preset.</summary>
    public OpResult SaveModPreset(string name)
    {
        var (cfg, _, _) = Resolve();
        var (ok, message) = ModPresetStore.Save(ModPresetStore.FromMods(name, cfg.Mods), _configPath);
        if (!ok) return new OpResult(false, message);
        Profiles.Save(cfg with { ModPreset = name.Trim() }, ActiveInstanceName(), _configPath);
        return new OpResult(true, message);
    }

    /// <summary>Apply a loadout to the active server: preset mods on (preset order + sides), the
    /// rest of its list off. Takes effect on the next start, like any mod edit.</summary>
    public OpResult ApplyModPreset(string name)
    {
        var preset = ModPresetStore.Load(name, _configPath);
        if (preset is null) return new OpResult(false, $"no mod preset '{name}'");
        var (cfg, _, _) = Resolve();
        Profiles.Save(cfg with { Mods = ModPresetStore.Apply(cfg.Mods, preset), ModPreset = preset.Name },
                      ActiveInstanceName(), _configPath);
        return new OpResult(true, $"applied mod preset '{preset.Name}' ({preset.Mods.Count} mods)");
    }

    /// <summary>Delete a loadout file. Instances still naming it keep the dangling reference
    /// (harmless — the UI just shows no selection).</summary>
    public OpResult DeleteModPreset(string name) =>
        ModPresetStore.Delete(name, _configPath)
            ? new OpResult(true, $"deleted mod preset '{name}'")
            : new OpResult(false, $"no mod preset '{name}'");

    public LogsResult Logs(string which, int lines)
    {
        var (cfg, _, _) = Resolve();
        var path = LogResolver.Resolve(cfg.ProfilesPath, cfg.ClientProfilesPath).GetValueOrDefault(which);
        var tail = path is null ? new List<string>() : LogTail.LastLines(path, lines);
        return new LogsResult(which, path, tail);
    }

    /// <summary>CLI/MCP starts pull the tray up as a monitor when configured; the tray's own
    /// starts ("tui") never re-launch it.</summary>
    private static void AutoLaunchTrayIfWanted(DzlConfig cfg, string source)
    {
        if (source is "cli" or "mcp" && cfg.AutoLaunchTray && !TrayLauncher.IsTrayRunning())
            TrayLauncher.LaunchMonitor(AppContext.BaseDirectory);
    }

    /// <summary>The facade never throws: a spawn failure (bad DayzPath, missing exe) must come
    /// back to the CLI/MCP/tray as an OpResult, not an unhandled Win32Exception.</summary>
    private static OpResult Op(Func<string> action)
    {
        try { return new OpResult(true, action()); }
        catch (Exception ex) { return new OpResult(false, ex.Message); }
    }

    /// <summary>An offline-sandbox instance has no server — every server op fails fast with
    /// this instead of spawning anything.</summary>
    private static OpResult? OfflineGuard(DzlConfig cfg) =>
        cfg.OfflineMode ? new OpResult(false, "offline instance has no server — start the client instead") : null;

    /// <summary>A live recorded process for the target blocks a second spawn — a duplicate would
    /// orphan the first from dzl's tracking (the statefile keeps one PID per target).</summary>
    private OpResult? AlreadyUpGuard(string target)
    {
        var live = StateFile.ReadLive(_configPath, ProcessManager.ImageOf);
        return live.TryGetValue(target, out var i)
            ? new OpResult(false, $"{target} already up (pid {i.Pid}, {i.Source}/{i.Mode}) — stop or restart it instead")
            : null;
    }

    /// <summary><paramref name="noConnect"/> starts the client with mods + mission but without
    /// <c>-connect</c>/<c>-port</c>, so it stays in the main menu instead of auto-joining.</summary>
    public OpResult Start(string mode, bool client, string source = "cli", bool noConnect = false)
    {
        var (cfg, _, _) = Resolve();
        if (OfflineGuard(cfg) is { } blocked) return blocked;
        if (AlreadyUpGuard("server") is { } up) return up;
        if (client && AlreadyUpGuard("client") is { } upC) return upC;
        AutoLaunchTrayIfWanted(cfg, source);
        return Op(() =>
        {
            ProcessManager.Spawn(mode, "server", cfg, source, _configPath);
            if (client) ProcessManager.Spawn(mode, "client", cfg, source, _configPath, connect: !noConnect);
            return $"started server{(client ? $" + client{(noConnect ? " (no connect)" : "")}" : "")} ({mode})";
        });
    }

    public OpResult Stop(bool client, string source = "cli")  // source unused by Stop; keep for symmetry
    {
        var (cfg, _, _) = Resolve();
        return Op(() =>
        {
            ProcessManager.Stop("server", cfg, _configPath);
            if (client) ProcessManager.Stop("client", cfg, _configPath);
            return $"stopped server{(client ? " + client" : "")}";
        });
    }

    public OpResult Restart(string mode, string source = "cli")
    {
        var (cfg, _, _) = Resolve();
        if (OfflineGuard(cfg) is { } blocked) return blocked;
        return Op(() =>
        {
            ProcessManager.Restart(mode, cfg, _configPath, source);
            return $"restarted server ({mode})";
        });
    }

    /// <summary><paramref name="connect"/> (client only): false drops <c>-connect</c>/<c>-port</c> —
    /// the game loads mods + mission but stays in the main menu. On an offline instance the client
    /// never connects regardless of the flag.</summary>
    public OpResult StartTarget(string target, string mode, string source = "tui", bool connect = true)
    {
        var (cfg, _, _) = Resolve();
        if (target == "server" && OfflineGuard(cfg) is { } blocked) return blocked;
        if (AlreadyUpGuard(target) is { } up) return up;
        AutoLaunchTrayIfWanted(cfg, source);
        var doConnect = connect && !cfg.OfflineMode;
        return Op(() =>
        {
            ProcessManager.Spawn(mode, target, cfg, source, _configPath, doConnect);
            return $"started {target} ({mode}){(target == "client" && !doConnect ? " (no connect)" : "")}";
        });
    }

    public OpResult StopTarget(string target, string source = "tui")
    {
        var (cfg, _, _) = Resolve();
        return Op(() =>
        {
            ProcessManager.Stop(target, cfg, _configPath);
            return $"stopped {target}";
        });
    }

    public OpResult RestartTarget(string target, string mode, string source = "tui")
    {
        var (cfg, _, _) = Resolve();
        if (target == "server" && OfflineGuard(cfg) is { } blocked) return blocked;
        return Op(() =>
        {
            if (target == "server")
                ProcessManager.Restart(mode, cfg, _configPath, source);
            else
            {
                ProcessManager.Stop(target, cfg, _configPath);
                ProcessManager.Spawn(mode, target, cfg, source, _configPath, connect: !cfg.OfflineMode);
            }
            return $"restarted {target} ({mode})";
        });
    }

    /// <summary>Which mpmissions folder the server will actually load (from the active instance's
    /// serverDZ.cfg template) and whether that's the instance's own mission or the install's.</summary>
    public MissionCheckResult CheckMission()
    {
        var (cfg, _, _) = Resolve();
        return MissionCheck.Evaluate(cfg);
    }

    /// <summary>Repoint the active instance's serverDZ.cfg template at its own mission (absolute path) so
    /// the server loads it instead of the install's.</summary>
    public OpResult FixMissionTemplate()
    {
        var (cfg, _, _) = Resolve();
        var mission = MissionLocator.Resolve(cfg)?.MissionDir;
        if (mission is null) return new OpResult(false, "no instance mission to point the template at");
        return Op(() =>
        {
            ServerScaffold.EnsureAbsoluteTemplate(cfg.ConfigName, mission);
            return "mission template now points at this instance";
        });
    }

    /// <summary>State of the active offline instance's mission init.c (does it carry the dzl offline
    /// bootstrap that spawns a character on a lone diag client).</summary>
    public OfflineInitResult CheckOfflineInit()
    {
        var (cfg, _, _) = Resolve();
        return OfflineInit.Check(cfg);
    }

    /// <summary>Back up the mission init.c and inject the offline bootstrap.</summary>
    public OpResult PatchOfflineInit()
    {
        var (cfg, _, _) = Resolve();
        var (ok, message) = OfflineInit.Patch(cfg);
        return new OpResult(ok, message);
    }

    /// <summary>Deploy the bundled DzlDevTools mod (source + prebuilt PBO) into the workspace. Opt-in;
    /// does not touch any instance loadout.</summary>
    public OpResult ImportDevTools()
    {
        var (cfg, _, _) = Resolve();
        var r = Projects.DevToolsAssets.Deploy(cfg);
        return new OpResult(r.Ok, r.Message);
    }
}
