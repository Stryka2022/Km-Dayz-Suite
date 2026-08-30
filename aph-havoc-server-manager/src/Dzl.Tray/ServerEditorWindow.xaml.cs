using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Servers;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;
using System.Globalization;
using TextBox = System.Windows.Controls.TextBox;

namespace Dzl.Tray;

/// <summary>
/// Inline per-server editor (Settings / Mods / Params tabs) for the ACTIVE server instance.
/// <see cref="MainViewModel"/> is shared so the loadout grid, params and per-server fields all
/// read/write the active instance without opening another application window.
/// </summary>
public partial class ServerEditorWindow : UserControl
{
    private readonly MainViewModel _vm;
    private readonly Action _close;

    /// <param name="tab">0 = Settings, 1 = Mods, 2 = Params.</param>
    public ServerEditorWindow(MainViewModel vm, int tab, Action close)
    {
        InitializeComponent();
        _vm = vm;
        _close = close;
        DataContext = vm;
        Loaded += (_, _) =>
        {
            LoadEditor();
            LoadParamsEditor();
            LoadDayzServerConfig();
            LoadWorkshopSettings();
            LoadFileLocations();
            Tabs.SelectedIndex = tab;
            if (_vm.IsOfflineInstance)
            {
                // Offline sandbox has no server — params editing applies to the client only.
                ParamTarget.SelectedIndex = 1;
                ParamTarget.IsEnabled = false;
            }
        };
    }

    private void LoadEditor()
    {
        var c = _vm.Cfg;
        CfgPort.Text = c.Port.ToString();
        CfgMission.Text = c.Mission;
        CfgConfigName.Text = c.ConfigName;
        CfgPlayerName.Text = c.PlayerName;
        CfgConnectIp.Text = c.ConnectIp;
        CfgProfilesPath.Text = c.ProfilesPath;
        CfgClientProfilesPath.Text = c.ClientProfilesPath;
        SrvMode.SelectedIndex = c.Mode == "normal" ? 1 : 0;
        SrvRenameBox.Text = "";
        SrvCloneBox.Text = "";
        SrvError.Visibility = Visibility.Collapsed;
    }

    private void OnRevertServer(object sender, RoutedEventArgs e) => LoadEditor();

    private void OnDetectConnectIp(object sender, RoutedEventArgs e) =>
        CfgConnectIp.Text = ServerNetwork.DetectConnectIp();

    private void OnSaveServer(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CfgPort.Text.Trim(), out var port))
        {
            SrvError.Text = "Port must be an integer.";
            SrvError.Visibility = Visibility.Visible;
            return;
        }
        SrvError.Visibility = Visibility.Collapsed;
        var mode = (SrvMode.SelectedItem as ComboBoxItem)?.Content as string ?? "debug";
        var edited = _vm.Cfg with
        {
            Port = port,
            Mission = CfgMission.Text.Trim(),
            ConfigName = CfgConfigName.Text.Trim(),
            PlayerName = CfgPlayerName.Text.Trim(),
            ConnectIp = CfgConnectIp.Text.Trim(),
            ProfilesPath = CfgProfilesPath.Text.Trim(),
            ClientProfilesPath = CfgClientProfilesPath.Text.Trim(),
            Mode = mode,
        };
        _vm.SaveActiveInstance(edited);
        LoadEditor();
        LoadParamsEditor();
    }

    private void OnCloneServer(object sender, RoutedEventArgs e)
    {
        var name = SrvCloneBox.Text.Trim();
        if (name.Length == 0) { SrvError.Text = "Enter a name to clone as."; SrvError.Visibility = Visibility.Visible; return; }
        var msg = _vm.CloneActive(name);
        if (!msg.StartsWith('✓')) { SrvError.Text = msg; SrvError.Visibility = Visibility.Visible; return; }
        LoadEditor();
        LoadParamsEditor();
    }

    private void OnRenameServer(object sender, RoutedEventArgs e)
    {
        var name = SrvRenameBox.Text.Trim();
        if (name.Length == 0) { SrvError.Text = "Enter a new name."; SrvError.Visibility = Visibility.Visible; return; }
        var msg = _vm.RenameActive(name);
        if (!msg.StartsWith('✓')) { SrvError.Text = msg; SrvError.Visibility = Visibility.Visible; return; }
        LoadEditor();
        LoadParamsEditor();
    }

    private string SelectedTarget => (ParamTarget.SelectedItem as ComboBoxItem)?.Content as string ?? "server";
    private string SelectedParamMode => (ParamMode.SelectedItem as ComboBoxItem)?.Content as string ?? "debug";

    private void OnParamSlotChanged(object sender, SelectionChangedEventArgs e) => LoadParamsEditor();

    private void LoadParamsEditor()
    {
        if (ParamsEditor is null) return;
        ParamsEditor.Text = string.Join("\n", _vm.CurrentParams(SelectedTarget, SelectedParamMode));
    }

    private void OnResetParams(object sender, RoutedEventArgs e) =>
        ParamsEditor.Text = string.Join("\n", MainViewModel.DefaultParams(SelectedTarget, SelectedParamMode));

    private void OnSaveParams(object sender, RoutedEventArgs e)
    {
        var lines = ParamsEditor.Text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _vm.ApplyParams(SelectedTarget, SelectedParamMode, lines);
    }

    // --- graphical serverDZ.cfg editor ----------------------------------

    private string ActiveServerConfigPath()
    {
        var configured = _vm.Cfg.ConfigName;
        return Path.IsPathRooted(configured) ? Path.GetFullPath(configured) : Path.Combine(_vm.ActiveServerDir, configured);
    }

    private void LoadDayzServerConfig()
    {
        if (DzHostname is null) return;
        try
        {
            var c = DayzServerConfig.Load(ActiveServerConfigPath());
            DzHostname.Text = c.Hostname;
            DzPassword.Password = c.Password;
            DzAdminPassword.Password = c.PasswordAdmin;
            DzMotd.Text = c.Motd;
            DzMotdInterval.Text = c.MotdInterval.ToString();
            DzMaxPlayers.Text = c.MaxPlayers.ToString();
            DzWhitelist.IsChecked = c.EnableWhitelist;
            DzVerifySignatures.Text = c.VerifySignatures.ToString();
            DzForceSameBuild.IsChecked = c.ForceSameBuild;
            DzDisableVon.IsChecked = c.DisableVoN;
            DzVonQuality.Text = c.VonCodecQuality.ToString();
            DzThirdPerson.IsChecked = c.DisableThirdPerson;
            DzCrosshair.IsChecked = c.DisableCrosshair;
            DzServerTime.Text = c.ServerTime;
            DzTimeAcceleration.Text = c.ServerTimeAcceleration.ToString(CultureInfo.InvariantCulture);
            DzNightAcceleration.Text = c.ServerNightTimeAcceleration.ToString(CultureInfo.InvariantCulture);
            DzTimePersistent.IsChecked = c.ServerTimePersistent;
            DzLoginConcurrent.Text = c.LoginQueueConcurrentPlayers.ToString();
            DzLoginMax.Text = c.LoginQueueMaxPlayers.ToString();
            DzInstanceId.Text = c.InstanceId.ToString();
            DzStorageAutoFix.IsChecked = c.StorageAutoFix;
            DzVisibility.Text = c.DefaultVisibility.ToString();
            DzObjectDistance.Text = c.DefaultObjectViewDistance.ToString();
            DzGameplayFile.IsChecked = c.EnableCfgGameplayFile;
            DzLightingConfig.Text = c.LightingConfig.ToString();
            DzPersonalLight.IsChecked = c.DisablePersonalLight;
            DzPingWarning.Text = c.PingWarning.ToString();
            DzPingCritical.Text = c.PingCritical.ToString();
            DzMaxPing.Text = c.MaxPing.ToString();
            DzFpsWarning.Text = c.ServerFpsWarning.ToString();
            DzAllowFilePatching.IsChecked = c.AllowFilePatching;
            DayzConfigStatus.Text = "";
        }
        catch (Exception ex) { DayzConfigStatus.Text = "✗ " + ex.Message; }
    }

    private static int Number(TextBox box, string label, int min, int max)
    {
        if (!int.TryParse(box.Text.Trim(), out var value) || value < min || value > max)
            throw new ArgumentException($"{label} must be from {min} to {max}.");
        return value;
    }

    private static double DecimalNumber(TextBox box, string label, double min, double max)
    {
        if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value < min || value > max)
            throw new ArgumentException($"{label} must be from {min} to {max}.");
        return value;
    }

    private void OnSaveDayzServerConfig(object sender, RoutedEventArgs e)
    {
        try
        {
            var edited = new DayzServerSettings
            {
                Hostname = DzHostname.Text.Trim(),
                Password = DzPassword.Password,
                PasswordAdmin = DzAdminPassword.Password,
                Motd = DzMotd.Text,
                MotdInterval = Number(DzMotdInterval, "MOTD interval", 0, 86400),
                MaxPlayers = Number(DzMaxPlayers, "Player limit", 1, 200),
                EnableWhitelist = DzWhitelist.IsChecked == true,
                VerifySignatures = Number(DzVerifySignatures, "Signature verification", 0, 2),
                ForceSameBuild = DzForceSameBuild.IsChecked == true,
                DisableVoN = DzDisableVon.IsChecked == true,
                VonCodecQuality = Number(DzVonQuality, "Voice quality", 0, 20),
                DisableThirdPerson = DzThirdPerson.IsChecked == true,
                DisableCrosshair = DzCrosshair.IsChecked == true,
                ServerTime = DzServerTime.Text.Trim(),
                ServerTimeAcceleration = DecimalNumber(DzTimeAcceleration, "Time acceleration", 0.1, 64),
                ServerNightTimeAcceleration = DecimalNumber(DzNightAcceleration, "Night acceleration", 0.1, 64),
                ServerTimePersistent = DzTimePersistent.IsChecked == true,
                LoginQueueConcurrentPlayers = Number(DzLoginConcurrent, "Login queue workers", 1, 100),
                LoginQueueMaxPlayers = Number(DzLoginMax, "Login queue limit", 0, 10000),
                InstanceId = Number(DzInstanceId, "Instance ID", 1, int.MaxValue),
                StorageAutoFix = DzStorageAutoFix.IsChecked == true,
                DefaultVisibility = Number(DzVisibility, "Terrain render distance", 100, 10000),
                DefaultObjectViewDistance = Number(DzObjectDistance, "Object render distance", 100, 10000),
                EnableCfgGameplayFile = DzGameplayFile.IsChecked == true,
                LightingConfig = Number(DzLightingConfig, "Lighting config", 0, 2),
                DisablePersonalLight = DzPersonalLight.IsChecked == true,
                PingWarning = Number(DzPingWarning, "Ping warning", 0, 10000),
                PingCritical = Number(DzPingCritical, "Ping critical", 0, 10000),
                MaxPing = Number(DzMaxPing, "Max ping", 0, 10000),
                ServerFpsWarning = Number(DzFpsWarning, "FPS warning", 0, 1000),
                AllowFilePatching = DzAllowFilePatching.IsChecked == true
            };
            if (edited.Hostname.Length == 0) throw new ArgumentException("Server name cannot be empty.");
            if (edited.VerifySignatures is not (0 or 2)) throw new ArgumentException("Signature verification must be 0 or 2.");
            DayzServerConfig.Save(ActiveServerConfigPath(), edited);
            DayzConfigStatus.Text = "✓ Saved this instance's serverDZ.cfg (backup kept).";
        }
        catch (Exception ex) { DayzConfigStatus.Text = "✗ " + ex.Message; }
    }

    private void OnReloadDayzServerConfig(object sender, RoutedEventArgs e) => LoadDayzServerConfig();

    private void OnOpenRawServerConfig(object sender, RoutedEventArgs e)
    {
        if (!ShellOpen.Editor(ActiveServerConfigPath())) DayzConfigStatus.Text = "✗ serverDZ.cfg could not be opened";
    }

    // --- per-instance Workshop policy -----------------------------------

    private void LoadWorkshopSettings()
    {
        if (EditorAutoUpdateMods is null) return;
        var c = _vm.Cfg;
        WorkshopDeployPath.Text = Path.Combine(_vm.ActiveServerDir, "@Workshop_<item id>");
        EditorAutoUpdateMods.IsChecked = c.AutoUpdateWorkshopMods;
        EditorAutoCopyKeys.IsChecked = c.AutoCopyWorkshopKeys;
        EditorUpdateInterval.Text = Math.Clamp(c.WorkshopUpdateIntervalMinutes, 5, 1440).ToString();
        foreach (var item in EditorUpdatePolicy.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Tag as string, c.WorkshopUpdatePolicy, StringComparison.OrdinalIgnoreCase))
                EditorUpdatePolicy.SelectedItem = item;
        WorkshopSettingsStatus.Text = "";
    }

    private void OnSaveWorkshopSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            var interval = Number(EditorUpdateInterval, "Workshop update interval", 5, 1440);
            _vm.SaveActiveInstance(_vm.Cfg with
            {
                AutoUpdateWorkshopMods = EditorAutoUpdateMods.IsChecked == true,
                AutoCopyWorkshopKeys = EditorAutoCopyKeys.IsChecked == true,
                WorkshopUpdateIntervalMinutes = interval,
                WorkshopUpdatePolicy = (EditorUpdatePolicy.SelectedItem as ComboBoxItem)?.Tag as string ?? "when-empty"
            });
            WorkshopSettingsStatus.Text = $"✓ Saved Workshop policy for {_vm.ActivePreset}.";
        }
        catch (Exception ex) { WorkshopSettingsStatus.Text = "✗ " + ex.Message; }
    }

    // --- instance file shortcuts ----------------------------------------

    private string ResolveInstancePath(string configured, string fallback)
    {
        if (string.IsNullOrWhiteSpace(configured)) return fallback;
        if (Path.IsPathRooted(configured)) return Path.GetFullPath(configured);
        return Path.GetFullPath(Path.Combine(_vm.ActiveServerDir, configured.TrimStart('.', '/', '\\')));
    }

    private void LoadFileLocations()
    {
        if (FilesServerConfig is null) return;
        FilesServerConfig.Text = ActiveServerConfigPath();
        FilesMission.Text = ResolveInstancePath(_vm.Cfg.Mission, Path.Combine(_vm.ActiveServerDir, "mpmissions"));
        FilesProfiles.Text = ResolveInstancePath(_vm.Cfg.ProfilesPath, Path.Combine(_vm.ActiveServerDir, "profiles"));
        FilesWorkshop.Text = _vm.ActiveServerDir;
        FilesKeys.Text = Path.Combine(_vm.ActiveServerDir, "keys");
        FilesRunnable.Text = string.IsNullOrWhiteSpace(_vm.Cfg.ServerInstallPathOverride)
            ? _vm.ActiveServerDir : _vm.Cfg.ServerInstallPathOverride;
    }

    private static void OpenOrCreate(string path)
    {
        try { if (!Directory.Exists(path)) Directory.CreateDirectory(path); } catch { /* ShellOpen reports failure */ }
        ShellOpen.Folder(path);
    }

    private void OnOpenMissionFiles(object sender, RoutedEventArgs e) => OpenOrCreate(FilesMission.Text);
    private void OnOpenProfilesFiles(object sender, RoutedEventArgs e) => OpenOrCreate(FilesProfiles.Text);
    private void OnOpenKeysFiles(object sender, RoutedEventArgs e) => OpenOrCreate(FilesKeys.Text);
    private void OnOpenRunnableFiles(object sender, RoutedEventArgs e) => OpenOrCreate(FilesRunnable.Text);

    private void OnBrowseInto(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var current = FindName(name) is TextBox cur ? cur.Text : "";
        var start = BrowseStartDir.Resolve(current, isFile: false,
            new[] { _vm.ActiveServerDir, CurrentDayzPath() }, Directory.Exists);
        var dlg = new OpenFolderDialog();
        if (!string.IsNullOrEmpty(start)) dlg.InitialDirectory = start;
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        if (FindName(name) is TextBox tb) tb.Text = dlg.FolderName;
    }

    private void OnBrowseMission(object sender, RoutedEventArgs e)
    {
        var dayz = CurrentDayzPath();
        var start = BrowseStartDir.Resolve(CfgMission.Text, isFile: false,
            new[] { Path.Combine(_vm.ActiveServerDir, "mpmissions"), Path.Combine(dayz, "mpmissions"), dayz },
            Directory.Exists);
        var dlg = new OpenFolderDialog { InitialDirectory = start };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) CfgMission.Text = RelOrAbs(dlg.FolderName, dayz);
    }

    private void OnBrowseConfigName(object sender, RoutedEventArgs e)
    {
        var dayz = CurrentDayzPath();
        var start = BrowseStartDir.Resolve(CfgConfigName.Text, isFile: true,
            new[] { _vm.ActiveServerDir, dayz }, Directory.Exists);
        var dlg = new OpenFileDialog
        {
            Filter = "Server config (*.cfg)|*.cfg|All files (*.*)|*.*",
            InitialDirectory = start,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) CfgConfigName.Text = RelOrAbs(dlg.FileName, dayz);
    }

    private string CurrentDayzPath() => _vm.Cfg.DayzPath;

    private static string RelOrAbs(string fullPath, string dayzPath)
    {
        var full = Path.GetFullPath(fullPath);
        var root = Path.GetFullPath(dayzPath);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, full)
            : full;
    }

    private void OnOpenInstanceFolder(object sender, RoutedEventArgs e)
    {
        var dir = _vm.ActiveServerDir;
        if (!ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show($"Couldn't open the folder:\n{dir}", "Open server folder",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OnWipePersistence(object sender, RoutedEventArgs e)
    {
        var ok = System.Windows.MessageBox.Show(
            "Wipe this server's persistence (Central Economy storage)?\n\nThe world / loot / player state " +
            "resets; DayZ regenerates fresh storage on the next start. The mission files are kept.",
            "Wipe persistence", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
            == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        WipeStatus.Text = _vm.WipeActivePersistence();
    }

    private void OnClose(object sender, RoutedEventArgs e) => _close();
}
