namespace Dzl.Core.Config;

public sealed record DzlConfig
{
    public string DayzPath { get; init; } = @"E:\Steam\steamapps\common\DayZ";
    public string DayzToolsPath { get; init; } = @"E:\Steam\steamapps\common\DayZ Tools";

    /// <summary>Dedicated DayZ server install (Steam app 223350). Empty = fall back to <see cref="DayzPath"/>.
    /// snake_case: dayz_server_path.</summary>
    public string DayzServerPath { get; init; } = "";

    /// <summary>Human-facing label for this server instance. Empty preserves the profile/folder key
    /// as the displayed name for older configurations. snake_case: display_name.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Filesystem-safe, unique folder/profile key for this instance. Empty preserves the
    /// active profile name for older configurations. snake_case: instance_folder_name.</summary>
    public string InstanceFolderName { get; init; } = "";

    /// <summary>Optional dedicated DayZ Server installation used only by this instance. Empty falls
    /// back to the machine-wide DayZ server/client path. snake_case: server_install_path_override.</summary>
    public string ServerInstallPathOverride { get; init; } = "";

    public string ProfilesPath { get; init; } = @"E:\Steam\steamapps\common\DayZ\profiles";
    public string ClientProfilesPath { get; init; } = @"E:\Steam\steamapps\common\DayZ\profiles_client";
    /// <summary>Single home for everything dzl creates — mod source projects at
    /// <c>&lt;ProjectsRoot&gt;\&lt;Mod&gt;</c> and server instances at <c>&lt;ProjectsRoot&gt;\servers\&lt;instance&gt;</c>.
    /// Empty = resolve to <c>%USERPROFILE%\DayZProjects</c> (see <c>ProjectPaths.Root</c>). snake_case: projects_root.</summary>
    public string ProjectsRoot { get; init; } = "";

    public string ExeDebug { get; init; } = "DayZDiag_x64.exe";
    public string ExeNormal { get; init; } = "DayZServer_x64.exe";
    public string ClientExeDebug { get; init; } = "DayZDiag_x64.exe";
    public string ClientExeNormal { get; init; } = "DayZ_x64.exe";
    public List<string> ScanRoots { get; init; } = new() { @"P:\@Dependencies", @"P:\@PackedMods", @"P:\" };
    public int Port { get; init; } = 2302;
    public string Mission { get; init; } = "./mpmissions/dayzOffline.chernarusplus";
    public string PlayerName { get; init; } = "DevMacie";
    public string ConfigName { get; init; } = "serverDZ.cfg";
    public string ConnectIp { get; init; } = "127.0.0.1";
    public List<ModEntry> Mods { get; init; } = new();
    public List<string> LogsShown { get; init; } = new() { "script", "rpt", "adm", "client" };
    public string Mode { get; init; } = "debug";
    /// <summary>Name of the last-applied mod preset (loadout) — "" = none. Drives the preset
    /// combo selection in the UI; a dangling name (preset deleted) is harmless. snake_case: mod_preset.</summary>
    public string ModPreset { get; init; } = "";

    /// <summary>Client-only offline sandbox: the instance has no server — the dashboard hides the
    /// server card and the client always starts without -connect/-port (mods + mission load, no
    /// auto-join). snake_case: offline_mode.</summary>
    public bool OfflineMode { get; init; }

    public int ModWidthIdx { get; init; }
    public List<string> ServerParamsDebug { get; init; } = new() { "-filePatching", "-dologs", "-adminLog", "-freezecheck", "-limitFPS=120" };
    public List<string> ServerParamsNormal { get; init; } = new() { "-dologs", "-adminLog", "-freezecheck" };
    public List<string> ClientParamsDebug { get; init; } = new() { "-window", "-nosplash", "-filePatching", "-doLogs", "-scriptDebug=true" };
    public List<string> ClientParamsNormal { get; init; } = new() { "-window", "-nosplash" };

    // Per-instance Workshop automation and notification policy.
    public bool AutoUpdateWorkshopMods { get; init; }
    public bool AutoCopyWorkshopKeys { get; init; } = true;
    public int WorkshopUpdateIntervalMinutes { get; init; } = 30;
    public string WorkshopUpdatePolicy { get; init; } = "when-empty";
    public Dictionary<string, long> WorkshopKnownUpdates { get; init; } = new();
    public Dictionary<string, long> WorkshopPendingUpdates { get; init; } = new();
    public Dictionary<string, string> WorkshopPendingTitles { get; init; } = new();
    public long WorkshopWarningDeadlineUtc { get; init; }
    public List<int> WorkshopWarningMinutesSent { get; init; } = new();
    public bool DiscordNotificationsEnabled { get; init; }
    public bool NotifyWorkshopUpdates { get; init; } = true;
    public bool NotifyServerLifecycle { get; init; } = true;
    public bool NotifyAdminActivity { get; init; } = true;
    public bool NotifyLogAlerts { get; init; } = true;
    public bool NotifyRemoteActivity { get; init; } = true;

    /// <summary>When true the tray hosts the named-pipe automation server so the dzl CLI and the Claude
    /// MCP integration can drive this process. Off by default (opt-in). snake_case:
    /// enable_automation_server.</summary>
    public bool EnableAutomationServer { get; init; } = false;

    /// <summary>When a server is started from CLI/MCP and the tray isn't running, auto-launch it
    /// (hidden, as a monitor). Default on. snake_case: auto_launch_tray.</summary>
    public bool AutoLaunchTray { get; init; } = true;

    /// <summary>Mount the P: work drive when the tray app launches. Off by default. snake_case:
    /// automount_work_drive.</summary>
    public bool AutomountWorkDrive { get; init; } = false;

    /// <summary>Override for the work-drive source folder (P: mount source / junction anchor). Empty =
    /// auto-derive from DayZ Tools settings.ini. snake_case: work_drive_source.</summary>
    public string WorkDriveSource { get; init; } = "";

    /// <summary>Run preflight before every build and block on error-severity findings. On by
    /// default — AddonBuilder reports "Build Successful" even for configs it silently mangles.
    /// snake_case: preflight_before_build.</summary>
    public bool PreflightBeforeBuild { get; init; } = true;

    /// <summary>Folder for signing keys (empty = ProjectsRoot\keys). snake_case: keys_dir.</summary>
    public string KeysDir { get; init; } = "";

    /// <summary>Creator's signing key name (empty = cached author). snake_case: signing_key.</summary>
    public string SigningKey { get; init; } = "";

    /// <summary>Code editor launcher for "Open in editor". snake_case: editor_path.</summary>
    public string EditorPath { get; init; } = "";

    /// <summary>Path to steamcmd.exe (Workshop download). snake_case: steamcmd_path.</summary>
    public string SteamCmdPath { get; init; } = "";

    /// <summary>Override folder for steamcmd downloads (blank = &lt;ProjectsRoot&gt;\workshop). snake_case: workshop_dir.</summary>
    public string WorkshopDir { get; init; } = "";

    /// <summary>steamcmd login username (empty = anonymous). snake_case: steam_login.</summary>
    public string SteamLogin { get; init; } = "";

    /// <summary>Steam web access token for in-app Subscribe. snake_case: steam_access_token.</summary>
    public string SteamAccessToken { get; init; } = "";

    public static DzlConfig Default() => new();

    // Two-tier split: DzlConfig stays the runtime composite every consumer uses; persistence + editing
    // split into GlobalConfig (machine env, config.json) and InstanceConfig (per-server, instances/<name>.json).

    /// <summary>Extract the machine-global slice (with the given active instance name).</summary>
    public GlobalConfig GlobalPart(string activeInstance = "") => new()
    {
        DayzPath = DayzPath,
        DayzToolsPath = DayzToolsPath,
        DayzServerPath = DayzServerPath,
        ProjectsRoot = ProjectsRoot,
        ExeDebug = ExeDebug,
        ExeNormal = ExeNormal,
        ClientExeDebug = ClientExeDebug,
        ClientExeNormal = ClientExeNormal,
        ScanRoots = ScanRoots,
        LogsShown = LogsShown,
        ModWidthIdx = ModWidthIdx,
        EnableAutomationServer = EnableAutomationServer,
        AutoLaunchTray = AutoLaunchTray,
        AutomountWorkDrive = AutomountWorkDrive,
        WorkDriveSource = WorkDriveSource,
        PreflightBeforeBuild = PreflightBeforeBuild,
        KeysDir = KeysDir,
        SigningKey = SigningKey,
        EditorPath = EditorPath,
        SteamCmdPath = SteamCmdPath,
        WorkshopDir = WorkshopDir,
        SteamLogin = SteamLogin,
        SteamAccessToken = SteamAccessToken,
        ActiveInstance = activeInstance,
    };

    /// <summary>Extract the per-server slice.</summary>
    public InstanceConfig InstancePart() => new()
    {
        DisplayName = DisplayName,
        InstanceFolderName = InstanceFolderName,
        ServerInstallPathOverride = ServerInstallPathOverride,
        ProfilesPath = ProfilesPath,
        ClientProfilesPath = ClientProfilesPath,
        Port = Port,
        Mission = Mission,
        PlayerName = PlayerName,
        ConfigName = ConfigName,
        ConnectIp = ConnectIp,
        Mods = Mods,
        Mode = Mode,
        ModPreset = ModPreset,
        OfflineMode = OfflineMode,
        ServerParamsDebug = ServerParamsDebug,
        ServerParamsNormal = ServerParamsNormal,
        ClientParamsDebug = ClientParamsDebug,
        ClientParamsNormal = ClientParamsNormal,
        AutoUpdateWorkshopMods = AutoUpdateWorkshopMods,
        AutoCopyWorkshopKeys = AutoCopyWorkshopKeys,
        WorkshopUpdateIntervalMinutes = WorkshopUpdateIntervalMinutes,
        WorkshopUpdatePolicy = WorkshopUpdatePolicy,
        WorkshopKnownUpdates = WorkshopKnownUpdates,
        WorkshopPendingUpdates = WorkshopPendingUpdates,
        WorkshopPendingTitles = WorkshopPendingTitles,
        WorkshopWarningDeadlineUtc = WorkshopWarningDeadlineUtc,
        WorkshopWarningMinutesSent = WorkshopWarningMinutesSent,
        DiscordNotificationsEnabled = DiscordNotificationsEnabled,
        NotifyWorkshopUpdates = NotifyWorkshopUpdates,
        NotifyServerLifecycle = NotifyServerLifecycle,
        NotifyAdminActivity = NotifyAdminActivity,
        NotifyLogAlerts = NotifyLogAlerts,
        NotifyRemoteActivity = NotifyRemoteActivity,
    };

    /// <summary>Compose the runtime config from the global slice + one server instance.</summary>
    public static DzlConfig Compose(GlobalConfig g, InstanceConfig i) => new()
    {
        DayzPath = g.DayzPath,
        DayzToolsPath = g.DayzToolsPath,
        DayzServerPath = g.DayzServerPath,
        ProjectsRoot = g.ProjectsRoot,
        ExeDebug = g.ExeDebug,
        ExeNormal = g.ExeNormal,
        ClientExeDebug = g.ClientExeDebug,
        ClientExeNormal = g.ClientExeNormal,
        ScanRoots = g.ScanRoots,
        LogsShown = g.LogsShown,
        ModWidthIdx = g.ModWidthIdx,
        EnableAutomationServer = g.EnableAutomationServer,
        AutoLaunchTray = g.AutoLaunchTray,
        AutomountWorkDrive = g.AutomountWorkDrive,
        WorkDriveSource = g.WorkDriveSource,
        PreflightBeforeBuild = g.PreflightBeforeBuild,
        KeysDir = g.KeysDir,
        SigningKey = g.SigningKey,
        EditorPath = g.EditorPath,
        SteamCmdPath = g.SteamCmdPath,
        WorkshopDir = g.WorkshopDir,
        SteamLogin = g.SteamLogin,
        SteamAccessToken = g.SteamAccessToken,
        DisplayName = i.DisplayName,
        InstanceFolderName = i.InstanceFolderName,
        ServerInstallPathOverride = i.ServerInstallPathOverride,
        ProfilesPath = i.ProfilesPath,
        ClientProfilesPath = i.ClientProfilesPath,
        Port = i.Port,
        Mission = i.Mission,
        PlayerName = i.PlayerName,
        ConfigName = i.ConfigName,
        ConnectIp = i.ConnectIp,
        Mods = i.Mods,
        Mode = i.Mode,
        ModPreset = i.ModPreset,
        OfflineMode = i.OfflineMode,
        ServerParamsDebug = i.ServerParamsDebug,
        ServerParamsNormal = i.ServerParamsNormal,
        ClientParamsDebug = i.ClientParamsDebug,
        ClientParamsNormal = i.ClientParamsNormal,
        AutoUpdateWorkshopMods = i.AutoUpdateWorkshopMods,
        AutoCopyWorkshopKeys = i.AutoCopyWorkshopKeys,
        WorkshopUpdateIntervalMinutes = i.WorkshopUpdateIntervalMinutes,
        WorkshopUpdatePolicy = i.WorkshopUpdatePolicy,
        WorkshopKnownUpdates = i.WorkshopKnownUpdates,
        WorkshopPendingUpdates = i.WorkshopPendingUpdates,
        WorkshopPendingTitles = i.WorkshopPendingTitles,
        WorkshopWarningDeadlineUtc = i.WorkshopWarningDeadlineUtc,
        WorkshopWarningMinutesSent = i.WorkshopWarningMinutesSent,
        DiscordNotificationsEnabled = i.DiscordNotificationsEnabled,
        NotifyWorkshopUpdates = i.NotifyWorkshopUpdates,
        NotifyServerLifecycle = i.NotifyServerLifecycle,
        NotifyAdminActivity = i.NotifyAdminActivity,
        NotifyLogAlerts = i.NotifyLogAlerts,
        NotifyRemoteActivity = i.NotifyRemoteActivity,
    };
}

public sealed record ModEntry
{
    public string Path { get; init; } = "";
    public bool Enabled { get; init; }
    public string Side { get; init; } = "both"; // both|server|client
}
