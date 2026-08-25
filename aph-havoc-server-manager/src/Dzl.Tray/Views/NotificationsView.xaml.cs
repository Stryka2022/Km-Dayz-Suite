using System.Windows;
using System.Windows.Controls;
using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray.Views;

public partial class NotificationsView : UserControl
{
    private DiscordWebhookTarget? _selectedWebhook;
    private MainViewModel? Vm => DataContext as MainViewModel;
    private string InstanceName => string.IsNullOrWhiteSpace(Vm?.ActivePreset) ? "default" : Vm!.ActivePreset;

    public NotificationsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public void Reload()
    {
        if (Vm is null) return;
        var c = Vm.Cfg;
        InstanceLabel.Text = $"SERVER  ·  {InstanceName}";
        AutoUpdateMods.IsChecked = c.AutoUpdateWorkshopMods;
        foreach (var item in UpdatePolicy.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Tag as string, c.WorkshopUpdatePolicy, StringComparison.OrdinalIgnoreCase))
                UpdatePolicy.SelectedItem = item;
        AutoCopyKeys.IsChecked = c.AutoCopyWorkshopKeys;
        UpdateInterval.Text = Math.Clamp(c.WorkshopUpdateIntervalMinutes, 5, 1440).ToString();
        DiscordEnabled.IsChecked = c.DiscordNotificationsEnabled;
        NotifyWorkshop.IsChecked = c.NotifyWorkshopUpdates;
        NotifyLifecycle.IsChecked = c.NotifyServerLifecycle;
        NotifyAdmin.IsChecked = c.NotifyAdminActivity;
        NotifyLogs.IsChecked = c.NotifyLogAlerts;
        NotifyRemote.IsChecked = c.NotifyRemoteActivity;
        ReloadWebhooks();
        StatusText.Text = "";
    }

    private DzlConfig EditedConfig()
    {
        if (Vm is null) throw new InvalidOperationException("Server Manager is not ready.");
        if (!int.TryParse(UpdateInterval.Text.Trim(), out var interval) || interval is < 5 or > 1440)
            throw new ArgumentException("Workshop check interval must be between 5 and 1440 minutes.");
        return Vm.Cfg with
        {
            AutoUpdateWorkshopMods = AutoUpdateMods.IsChecked == true,
            WorkshopUpdatePolicy = (UpdatePolicy.SelectedItem as ComboBoxItem)?.Tag as string ?? "when-empty",
            AutoCopyWorkshopKeys = AutoCopyKeys.IsChecked == true,
            WorkshopUpdateIntervalMinutes = interval,
            DiscordNotificationsEnabled = DiscordEnabled.IsChecked == true,
            NotifyWorkshopUpdates = NotifyWorkshop.IsChecked == true,
            NotifyServerLifecycle = NotifyLifecycle.IsChecked == true,
            NotifyAdminActivity = NotifyAdmin.IsChecked == true,
            NotifyLogAlerts = NotifyLogs.IsChecked == true,
            NotifyRemoteActivity = NotifyRemote.IsChecked == true
        };
    }

    private bool SaveSettings()
    {
        if (Vm is null) return false;
        try
        {
            var edited = EditedConfig();
            if (_selectedWebhook is not null || !string.IsNullOrWhiteSpace(WebhookName.Text) ||
                !string.IsNullOrWhiteSpace(WebhookUrl.Password))
                SaveWebhookDestination();
            Vm.SaveActiveInstance(edited);
            WebhookUrl.Password = "";
            ReloadWebhooks(_selectedWebhook?.Id);
            StatusText.Text = $"✓ Saved automation and notification settings for {InstanceName}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "✗ " + ex.Message;
            return false;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => SaveSettings();

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        if (!SaveSettings() || Vm is null) return;
        var r = await Vm.NotifyDiscordAsync(DiscordNotificationCategory.AdminActivity,
            "KM Suite webhook test", $"Notifications are connected for **{InstanceName}**.");
        StatusText.Text = (r.Ok ? "✓ " : "✗ ") + r.Message;
    }

    private async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        if (!SaveSettings() || Vm is null) return;
        StatusText.Text = "Checking Workshop metadata for every configured instance…";
        var result = await Vm.CheckWorkshopUpdatesAcrossInstancesAsync(manual: true);
        StatusText.Text = result.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? "✗ " + result
            : "✓ " + result;
    }

    private void OnNewWebhook(object sender, RoutedEventArgs e)
    {
        WebhookList.SelectedItem = null;
        _selectedWebhook = null;
        WebhookName.Text = "";
        WebhookEnabled.IsChecked = true;
        TargetWorkshop.IsChecked = true;
        TargetLifecycle.IsChecked = true;
        TargetAdmin.IsChecked = true;
        TargetLogs.IsChecked = true;
        TargetRemote.IsChecked = true;
        WebhookUrl.Password = "";
        WebhookStoredText.Text = "New destination — enter a name and Discord webhook URL.";
    }

    private void OnWebhookSelected(object sender, SelectionChangedEventArgs e)
    {
        if (WebhookList.SelectedItem is not DiscordWebhookTarget target) return;
        _selectedWebhook = target;
        WebhookName.Text = target.Name;
        WebhookEnabled.IsChecked = target.Enabled;
        TargetWorkshop.IsChecked = target.WorkshopUpdates;
        TargetLifecycle.IsChecked = target.ServerLifecycle;
        TargetAdmin.IsChecked = target.AdminActivity;
        TargetLogs.IsChecked = target.LogAlerts;
        TargetRemote.IsChecked = target.RemoteActivity;
        WebhookUrl.Password = "";
        WebhookStoredText.Text = $"✓ {target.Name} has an encrypted URL saved; leave the URL blank to retain it.";
    }

    private void OnSaveWebhook(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveWebhookDestination();
            ReloadWebhooks(_selectedWebhook?.Id);
            StatusText.Text = $"✓ Saved Discord destination for {InstanceName}.";
        }
        catch (Exception ex) { StatusText.Text = "✗ " + ex.Message; }
    }

    private void SaveWebhookDestination()
    {
        if (Vm is null) throw new InvalidOperationException("Server Manager is not ready.");
        var target = new DiscordWebhookTarget
        {
            Id = _selectedWebhook?.Id ?? Guid.NewGuid().ToString("N"),
            Name = WebhookName.Text.Trim(),
            Enabled = WebhookEnabled.IsChecked == true,
            WorkshopUpdates = TargetWorkshop.IsChecked == true,
            ServerLifecycle = TargetLifecycle.IsChecked == true,
            AdminActivity = TargetAdmin.IsChecked == true,
            LogAlerts = TargetLogs.IsChecked == true,
            RemoteActivity = TargetRemote.IsChecked == true
        };
        DiscordWebhookStore.Upsert(Vm.ConfigFilePath, InstanceName, target, WebhookUrl.Password);
        _selectedWebhook = target;
        WebhookUrl.Password = "";
    }

    private void OnClearWebhook(object sender, RoutedEventArgs e)
    {
        if (Vm is null || _selectedWebhook is null) return;
        DiscordWebhookStore.Delete(Vm.ConfigFilePath, InstanceName, _selectedWebhook.Id);
        _selectedWebhook = null;
        WebhookUrl.Password = "";
        ReloadWebhooks();
        StatusText.Text = $"Discord destination removed for {InstanceName}.";
    }

    private void ReloadWebhooks(string? selectedId = null)
    {
        if (Vm is null) return;
        var targets = DiscordWebhookStore.LoadTargets(Vm.ConfigFilePath, InstanceName);
        WebhookList.ItemsSource = targets;
        var selected = targets.FirstOrDefault(t => t.Id == selectedId) ?? targets.FirstOrDefault();
        WebhookList.SelectedItem = selected;
        if (selected is null) OnNewWebhook(this, new RoutedEventArgs());
        WebhookStoredText.Text = targets.Count == 0
            ? "No webhook destinations saved for this instance"
            : $"{targets.Count} named destination(s) · {targets.Count(t => t.Enabled)} enabled";
    }
}
