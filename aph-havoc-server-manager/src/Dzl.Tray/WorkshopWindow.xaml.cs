using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.App;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray;

/// <summary>
/// Standalone Steam Workshop browser: search the Web API, then per result Subscribe (opens the item in the
/// Steam client) or Download (steamcmd). Lists items already subscribed in the Steam client with open/update
/// actions. Shares <see cref="MainViewModel"/> so results/subscribed/status bind directly.
/// </summary>
public partial class WorkshopWindow : UserControl
{
    private MainViewModel Vm => (MainViewModel)DataContext;
    private bool _loaded;
    private bool _syncingTargetServer;

    public WorkshopWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            RefreshTargetServers();
            Vm.InitWorkshop();
            Vm.RefreshSteamAccount();
            Vm.RefreshSubscribed();
            if (!_loaded)
            {
                _loaded = true;
                await Vm.WorkshopBrowseAsync();
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible || DataContext is not MainViewModel) return;
            RefreshTargetServers();
            Vm.RefreshSubscribed();
        };
    }

    /// <summary>Reload only real server instances and keep the Workshop target aligned with the
    /// active Server Manager instance. Profiles that are not backed by a created server never
    /// appear in this selector.</summary>
    private void RefreshTargetServers()
    {
        if (DataContext is not MainViewModel) return;
        _syncingTargetServer = true;
        try
        {
            Vm.RefreshServers();
            var active = string.IsNullOrWhiteSpace(Vm.ActivePreset) ? "default" : Vm.ActivePreset;
            TargetServerCombo.SelectedValue = active;
            UpdateTargetServerStatus();
        }
        finally { _syncingTargetServer = false; }
    }

    private void UpdateTargetServerStatus(string? switchStatus = null)
    {
        if (TargetServerCombo.SelectedItem is Dzl.Core.Servers.ServerInstance server)
            TargetServerStatus.Text = string.IsNullOrWhiteSpace(switchStatus)
                ? $"Active target  ·  {server.Dir}"
                : $"{switchStatus}  ·  {server.Dir}";
        else
            TargetServerStatus.Text = "";
    }

    private void OnRefreshTargetServers(object sender, RoutedEventArgs e) => RefreshTargetServers();

    private void OnTargetServerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTargetServer || DataContext is not MainViewModel ||
            TargetServerCombo.SelectedValue is not string name || string.IsNullOrWhiteSpace(name)) return;

        var active = string.IsNullOrWhiteSpace(Vm.ActivePreset) ? "default" : Vm.ActivePreset;
        if (string.Equals(name, active, StringComparison.OrdinalIgnoreCase))
        {
            UpdateTargetServerStatus();
            return;
        }

        _syncingTargetServer = true;
        try
        {
            var status = Vm.UseServer(name);
            UpdateTargetServerStatus(status);
        }
        finally { _syncingTargetServer = false; }
    }

    // ⚙ — open the Workshop settings modal (Steam sign-in + steamcmd); refresh gating after.
    private void OnWorkshopSettings(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not Window owner) return;
        new ModuleSettingsWindow(Vm, "workshop") { Owner = owner }.ShowDialog();
        Vm.NotifyWorkshopGate();
        Vm.RefreshSteamAccount();
        Vm.RefreshSubscribed();
    }

    // Sign-in banner button — sign in directly, then re-evaluate the gate.
    private void OnSignInBanner(object sender, RoutedEventArgs e)
    {
        SteamPanel.ExpandAndFocus();
    }

    private async void OnSearch(object sender, RoutedEventArgs e) => await Vm.WorkshopBrowseAsync();

    // Infinite scroll: auto-load the next page when the results list is scrolled near the bottom. With a
    // virtualizing ListBox the ScrollViewer offsets are in item units, so "within 3 of the end" works.
    private bool _loadingMore;
    private async void OnResultsScroll(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (_loadingMore || e.VerticalChange <= 0 || e.ExtentHeight <= e.ViewportHeight) return;
        if (e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 3) return;   // not near the bottom yet
        _loadingMore = true;
        try { await Vm.WorkshopLoadMoreAsync(); }
        finally { _loadingMore = false; }
    }

    private void OnRefreshSubscribed(object sender, RoutedEventArgs e) => Vm.RefreshSubscribed();

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await Vm.WorkshopDownloadAsync(id);
    }

    private async void OnInstallForTarget(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await Vm.InstallWorkshopOnActiveServerAsync(id);
    }

    private async void OnCheckAllUpdates(object sender, RoutedEventArgs e)
        => await Vm.CheckWorkshopUpdatesAcrossInstancesAsync(manual: true);

    // Subscribe in-app via the Steam web token when set; otherwise open the item page in the Steam client.
    private async void OnSubscribe(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        if (await Vm.SubscribeWorkshopAsync(id)) return;   // handled in-app
        try { Process.Start(new ProcessStartInfo(WorkshopService.SteamPageUrl(id)) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    // Open the item's page in the Steam client (steam:// protocol).
    private void OnOpenInSteam(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        try { Process.Start(new ProcessStartInfo(WorkshopService.SteamPageUrl(id)) { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    // Open the item's Steam Community page in the default browser.
    private void OnOpenSteamWeb(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        try { Process.Start(new ProcessStartInfo($"https://steamcommunity.com/sharedfiles/filedetails/?id={id}") { UseShellExecute = true }); }
        catch { /* best-effort */ }
    }

    // Show the item's details in the right pane (useful from the Subscribed/Downloaded lists).
    private async void OnShowInDzl(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await Vm.ShowDetailAsync(id);
    }

    private async void OnUnsubscribe(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await Vm.UnsubscribeWorkshopAsync(id);
    }

    // Resolve the item's real folder by id (Steam client OR the steamcmd download under ProjectsRoot) — the
    // SubscribedItem.Dir reflects only the Steam client folder and is empty for optimistic / steamcmd-only rows.
    // Open a specific folder path directly (used by the Downloaded list, so the same id existing as a Steam
    // subscription doesn't redirect us to the Steam folder via the id resolver).
    private void OnOpenFolderDirect(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string dir }) return;
        if (string.IsNullOrWhiteSpace(dir) || !ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show("Folder not found — it may have been deleted.",
                "Open folder", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void OnOpenSubscribedFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        var dir = Vm.ResolveModFolder(id);
        if (string.IsNullOrWhiteSpace(dir) || !ShellOpen.Folder(dir))
            System.Windows.MessageBox.Show("Not downloaded yet — subscribe (the Steam client downloads in the background) or use Download (steamcmd).",
                "Open folder", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    // Delete a steamcmd-downloaded item (destructive → confirm first).
    private async void OnDeleteDownloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        var name = Vm.WorkshopDownloaded.FirstOrDefault(d => d.Id == id)?.Name ?? id;
        var r = System.Windows.MessageBox.Show(
            $"Delete the downloaded files for \"{name}\" ({id})?\n\nThis removes them from your workshop folder. You can re-download later.",
            "Delete download", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (r == System.Windows.MessageBoxResult.Yes) await Vm.DeleteDownloadedAsync(id);
    }

    private async void OnAddById(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not Window owner) return;
        var input = PromptDialog.Show(owner, "Add Workshop item", "Workshop id or URL:");
        if (string.IsNullOrWhiteSpace(input)) return;
        var m = System.Text.RegularExpressions.Regex.Match(input, @"id=(\d+)");
        var id = m.Success ? m.Groups[1].Value : new string(input.Where(char.IsDigit).ToArray());
        if (id.Length == 0)
        {
            System.Windows.MessageBox.Show("Couldn't find a Workshop id in that input.", "Add by ID",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        await Vm.WorkshopDownloadAsync(id);
    }
}
