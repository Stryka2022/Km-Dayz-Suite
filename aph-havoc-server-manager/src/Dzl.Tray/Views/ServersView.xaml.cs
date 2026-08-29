using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Servers;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Views;

/// <summary>Servers page (instances): create a server, activate one, and open its inline editor
/// (Settings / Mods / Params tabs). All state lives on <see cref="MainViewModel"/> (the inherited
/// DataContext); destructive actions confirm first.</summary>
public partial class ServersView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    private bool _updatingFolderName;
    private bool _folderNameWasEdited;

    public ServersView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (NewServerPortBox.Text.Length == 0)
            NewServerPortBox.Text = Vm.SuggestServerPort().ToString();
        if (NewServerIpBox.Text.Length == 0)
            NewServerIpBox.Text = MainViewModel.DetectServerIp();
    }

    private void OnDisplayNameChanged(object sender, TextChangedEventArgs e)
    {
        if (Vm is null || NewServerFolderBox is null || _folderNameWasEdited) return;
        _updatingFolderName = true;
        NewServerFolderBox.Text = MainViewModel.SuggestInstanceFolder(NewServerNameBox.Text);
        _updatingFolderName = false;
        UpdateDedicatedInstallSuggestion();
    }

    private void OnFolderNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingFolderName && NewServerFolderBox?.IsKeyboardFocusWithin == true)
            _folderNameWasEdited = true;
        UpdateDedicatedInstallSuggestion();
    }

    private void OnRandomPort(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) NewServerPortBox.Text = Vm.SuggestServerPort().ToString();
    }

    private void OnDetectIp(object sender, RoutedEventArgs e) =>
        NewServerIpBox.Text = MainViewModel.DetectServerIp();

    private void OnDedicatedInstallChanged(object sender, RoutedEventArgs e)
    {
        if (DedicatedInstallPanel is null) return;
        DedicatedInstallPanel.IsEnabled = NewServerDedicatedBox.IsChecked == true;
        UpdateDedicatedInstallSuggestion();
    }

    private void UpdateDedicatedInstallSuggestion()
    {
        if (Vm is null || NewServerDedicatedBox?.IsChecked != true || NewServerInstallPathBox is null) return;
        if (!NewServerInstallPathBox.IsKeyboardFocusWithin || string.IsNullOrWhiteSpace(NewServerInstallPathBox.Text))
            NewServerInstallPathBox.Text = Vm.SuggestDedicatedInstallPath(NewServerFolderBox.Text);
    }

    private void OnBrowseDedicatedInstall(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a parent folder — the safe server name will be added",
        };
        var current = NewServerInstallPathBox.Text.Trim();
        if (Directory.Exists(current)) dlg.InitialDirectory = current;
        else if (Directory.Exists(Path.GetDirectoryName(current))) dlg.InitialDirectory = Path.GetDirectoryName(current)!;
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            NewServerInstallPathBox.Text = MainViewModel.ResolveDedicatedInstallPath(
                dlg.FolderName, NewServerFolderBox.Text);
    }

    // Re-entrancy guard for the create-server flow (button disabled while it runs, but a fast
    // double-tap before the first frame renders could still re-enter).
    private bool _creatingServer;

    private async void OnCreateServer(object sender, RoutedEventArgs e)
    {
        if (Vm is null || _creatingServer) return;
        var displayName = NewServerNameBox.Text.Trim();
        if (displayName.Length == 0) { NewServerStatus.Text = "Enter a server display name."; return; }
        var folderName = MainViewModel.SuggestInstanceFolder(NewServerFolderBox.Text);
        var map = (NewServerMapBox.SelectedItem as string) ?? "chernarus";
        int? port = null;
        if (NewServerPortBox.Text.Trim().Length > 0)
        {
            if (!int.TryParse(NewServerPortBox.Text.Trim(), out var p) || p is < 1024 or > 65535)
            {
                NewServerStatus.Text = "Enter a port from 1024 to 65535, or use the random button.";
                return;
            }
            port = p;
        }
        var baseSel = NewServerBaseBox.SelectedItem as string;
        var baseName = (string.IsNullOrEmpty(baseSel) || baseSel == MainViewModel.VanillaChoice) ? null : baseSel;
        var modsSel = NewServerModsBox.SelectedItem as string;
        var modPreset = (string.IsNullOrEmpty(modsSel) || modsSel == MainViewModel.NoModPresetChoice) ? null : modsSel;
        var offline = NewServerOfflineBox.IsChecked == true;
        var installDedicated = NewServerDedicatedBox.IsChecked == true;
        var installPath = installDedicated ? NewServerInstallPathBox.Text.Trim() : null;
        var connectIp = NewServerIpBox.Text.Trim();
        if (connectIp.Length == 0)
        {
            connectIp = MainViewModel.DetectServerIp();
            NewServerIpBox.Text = connectIp;
        }
        _creatingServer = true;
        NewServerButton.IsEnabled = false;
        NewServerStatus.Text = installDedicated
            ? "creating instance and preparing its DayZ Dedicated Server files…"
            : "creating… (copying mission template — this can take a moment)";
        try
        {
            var progress = new Progress<string>(message => NewServerStatus.Text = message);
            NewServerStatus.Text = await Vm.CreateServerAsync(displayName, folderName, map, port,
                baseName, modPreset, offline, installPath, installDedicated, connectIp, progress);
        }
        catch (Exception ex) { NewServerStatus.Text = "✗ " + ex.Message; }
        finally { NewServerButton.IsEnabled = true; _creatingServer = false; }
        if (NewServerStatus.Text.StartsWith('✓') || NewServerStatus.Text.StartsWith('⚠'))
        {
            NewServerNameBox.Text = "";
            NewServerFolderBox.Text = "";
            _folderNameWasEdited = false;
            NewServerPortBox.Text = Vm.SuggestServerPort().ToString();
            NewServerIpBox.Text = MainViewModel.DetectServerIp();
            NewServerDedicatedBox.IsChecked = false;
            NewServerInstallPathBox.Text = "";
        }
    }

    private void OnUseServer(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && sender is FrameworkElement { Tag: string name })
            NewServerStatus.Text = Vm.UseServer(name);
    }

    // A base fixes its own map (baked into its serverDZ.cfg + mpmission). When one is
    // selected, lock the map dropdown and reflect the base's map; only vanilla is free to pick.
    private void OnNewServerBaseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is null || NewServerMapBox is null) return;   // fires once during InitializeComponent before peers exist
        var sel = NewServerBaseBox.SelectedItem as string;
        var vanilla = string.IsNullOrEmpty(sel) || sel == MainViewModel.VanillaChoice;
        NewServerMapBox.IsEnabled = vanilla;
        if (!vanilla)
        {
            var b = Vm.Bases.FirstOrDefault(x => x.Name == sel);
            if (b is not null) NewServerMapBox.SelectedItem = MapAliases.MapName(b.Mission);
        }
    }

    // --- per-server inline editor ----------------------------------------

    /// <summary>Open the inline editor for the active server on a given tab (0=Settings,1=Mods,2=Params).</summary>
    private void OpenServerEditor(int tab)
    {
        if (Vm is null) return;
        (Window.GetWindow(this) as MainWindow)?.OpenServerEditor(tab, "servers");
    }

    /// <summary>Servers row "Settings"/"Mods": activate the clicked server, then open its inline editor.</summary>
    private void OpenServerForRow(object sender, int tab)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string name }) return;
        Vm.UseServer(name);
        OpenServerEditor(tab);
    }

    private void OnOpenServerSettings(object sender, RoutedEventArgs e) => OpenServerForRow(sender, 0);
    private void OnOpenServerMods(object sender, RoutedEventArgs e) => OpenServerForRow(sender, 1);

    /// <summary>Open a server instance's folder in Explorer (Tag = the instance dir).</summary>
    private void OnOpenServerFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string dir } || string.IsNullOrWhiteSpace(dir)) return;
        if (!ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show($"Couldn't open the folder:\n{dir}", "Open server folder",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OnOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string folder }) return;
        var msg = Vm.OpenInEditor(folder);
        if (msg.StartsWith('✗'))
            System.Windows.MessageBox.Show(msg.TrimStart('✗', ' '), "Open in editor",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private bool _repairingDedicatedServer;

    private async void OnRepairDedicatedServer(object sender, RoutedEventArgs e)
    {
        if (Vm is null || _repairingDedicatedServer || sender is not FrameworkElement { Tag: string name }) return;
        _repairingDedicatedServer = true;
        NewServerStatus.Text = $"installing / repairing the DayZ Dedicated Server for {name}…";
        try
        {
            var progress = new Progress<string>(message => NewServerStatus.Text = message);
            NewServerStatus.Text = await Vm.RepairDedicatedServerAsync(name, progress);
        }
        catch (Exception ex) { NewServerStatus.Text = "✗ " + ex.Message; }
        finally { _repairingDedicatedServer = false; }
    }

    private void OnWipeServerPersistence(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string dir } || string.IsNullOrWhiteSpace(dir)) return;
        var ok = System.Windows.MessageBox.Show(
            $"Wipe persistence for this server?\n\n{dir}\n\nThe world / loot / player state resets; DayZ " +
            "regenerates fresh Central Economy storage on the next start. The mission files are kept.",
            "Wipe persistence", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        NewServerStatus.Text = Vm.WipePersistenceDir(dir);
    }

    private void OnDeleteServer(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement { Tag: string name }) return;
        var r = System.Windows.MessageBox.Show(
            $"Delete server \"{name}\"?\n\n" +
            "YES — delete the server AND all its files (serverDZ.cfg, mpmissions, profiles / logs). Cannot be undone.\n\n" +
            "NO — remove it from APH Havoc only; keep the folder + files on disk.\n\n" +
            "CANCEL — don't delete.",
            "Delete server", System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Cancel) return;
        NewServerStatus.Text = Vm.DeleteServer(name, removeFiles: r == System.Windows.MessageBoxResult.Yes);
    }
}
