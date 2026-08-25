using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dzl.Core.App;
using Dzl.Core.Remote;
using Dzl.Tray.ViewModels;
using Microsoft.Win32;

namespace Dzl.Tray.Views;

/// <summary>
/// Server-owner FTP/FTPS and BattlEye RCon workspace. Network operations stay asynchronous,
/// both passwords are resolved through separate DPAPI-backed fields, and each remote overwrite
/// first creates a local recovery copy under the dzl config directory.
/// </summary>
public partial class RemoteFilesView : UserControl
{
    private readonly ObservableCollection<RemoteFileEntry> _entries = new();
    private IReadOnlyList<RemoteServerProfile> _profiles = Array.Empty<RemoteServerProfile>();
    private RemoteServerProfile? _selectedProfile;
    private FtpRemoteClient? _client;
    private BattlEyeRconClient? _rcon;
    private string _currentPath = "/";
    private string _rootPath = "/";
    private RemoteFileEntry? _editorEntry;
    private Encoding _editorEncoding = new UTF8Encoding(false);
    private bool _editorBom;
    private bool _settingEditor;
    private bool _dirty;
    private bool _busy;
    private bool _rconBusy;
    private CancellationTokenSource? _operation;

    private string ConfigPath => App.ConfigPath();
    private MainViewModel? Vm => DataContext as MainViewModel;

    private void Notify(DiscordNotificationCategory category, string title, string message)
    {
        if (Vm is not { } vm) return;
        _ = vm.NotifyDiscordAsync(category, title, message);
    }

    public RemoteFilesView()
    {
        InitializeComponent();
        RemoteGrid.ItemsSource = _entries;
        Loaded += (_, _) => LoadProfiles();
        Unloaded += async (_, _) => await DisconnectRconAsync(silent: true);
    }

    public void RefreshOnShow()
    {
        if (!IsLoaded) return;
        LoadProfiles(_selectedProfile?.Id);
    }

    private void LoadProfiles(string? selectedId = null)
    {
        selectedId ??= _selectedProfile?.Id;
        var instances = Dzl.Core.Config.Profiles.List(ConfigPath);
        InstanceBox.ItemsSource = instances;
        _profiles = RemoteProfileStore.Load(ConfigPath);
        ProfileBox.ItemsSource = _profiles;
        if (selectedId is not null)
            ProfileBox.SelectedItem = _profiles.FirstOrDefault(p => p.Id == selectedId);
        else if (_profiles.Count > 0 && ProfileBox.SelectedItem is null)
            ProfileBox.SelectedIndex = 0;
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not RemoteServerProfile profile) return;
        _selectedProfile = profile;
        NameBox.Text = profile.Name;
        InstanceBox.SelectedItem = InstanceBox.Items.Cast<string>()
            .FirstOrDefault(name => string.Equals(name, profile.InstanceName, StringComparison.OrdinalIgnoreCase));
        HostBox.Text = profile.Host;
        PortBox.Text = profile.Port.ToString();
        UserBox.Text = profile.UserName;
        RootBox.Text = profile.RootPath;
        ProtocolBox.SelectedIndex = profile.UseTls ? 0 : 1;
        PassiveBox.IsChecked = profile.Passive;
        PasswordBox.Clear();
        PasswordBox.ToolTip = "Saved password is encrypted. Leave blank to keep and use it.";
        RconHostBox.Text = profile.RconHost;
        RconPortBox.Text = profile.RconPort.ToString();
        RconPasswordBox.Clear();
        RconPasswordBox.ToolTip = "Saved RCon password is encrypted. Leave blank to keep and use it.";
        SetStatus($"Loaded {profile.Name}. FTP and RCon secrets remain encrypted until used.");
    }

    private void OnNewProfile(object sender, RoutedEventArgs e)
    {
        ProfileBox.SelectedItem = null;
        _selectedProfile = null;
        NameBox.Text = "";
        InstanceBox.SelectedItem = InstanceBox.Items.Cast<string>()
            .FirstOrDefault(name => string.Equals(name, Vm?.ActivePreset, StringComparison.OrdinalIgnoreCase));
        HostBox.Text = "";
        PortBox.Text = "21";
        UserBox.Text = "";
        RootBox.Text = "/";
        ProtocolBox.SelectedIndex = 0;
        PassiveBox.IsChecked = true;
        PasswordBox.Clear();
        RconHostBox.Text = "";
        RconPortBox.Text = "2301";
        RconPasswordBox.Clear();
        RemoteOsBadge.Visibility = Visibility.Collapsed;
        _ = DisconnectRconAsync(silent: true);
        SetStatus("New profile — FTPS is selected by default; RCon uses its own host, port and password.");
    }

    private void OnSaveProfile(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = ReadProfile();
            RemoteProfileStore.Upsert(ConfigPath, profile, PasswordBox.Password, RconPasswordBox.Password);
            _selectedProfile = profile;
            LoadProfiles(profile.Id);
            PasswordBox.Clear();
            RconPasswordBox.Clear();
            SetStatus($"Saved {profile.Name}; FTP and RCon passwords are encrypted for this Windows user.", success: true);
            Notify(DiscordNotificationCategory.AdminActivity, "Remote profile saved",
                $"Connection profile **{profile.Name}** was saved. Secrets remain encrypted and are never sent to Discord.");
        }
        catch (Exception ex) { SetStatus(ex.Message, error: true); }
    }

    private void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null) return;
        if (MessageBox.Show(Window.GetWindow(this),
                $"Delete the saved connection profile '{_selectedProfile.Name}'?\n\nNo remote files will be changed.",
                "Delete remote profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        RemoteProfileStore.Delete(ConfigPath, _selectedProfile.Id);
        _selectedProfile = null;
        _client = null;
        _ = DisconnectRconAsync(silent: true);
        OnNewProfile(sender, e);
        LoadProfiles();
    }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        if (_busy) { _operation?.Cancel(); return; }
        try
        {
            var profile = ReadProfile(requireFtp: true);
            var password = PasswordBox.Password;
            if (password.Length == 0 && _selectedProfile?.Id == profile.Id)
                password = RemoteProfileStore.GetFtpPassword(ConfigPath, profile.Id);
            _client = new FtpRemoteClient(profile, password);
            _rootPath = FtpRemoteClient.NormalizePath(profile.RootPath);
            await RunAsync(async ct =>
            {
                SetStatus($"Connecting securely to {profile.Host}:{profile.Port}…");
                await _client.TestAsync(ct);
                await LoadDirectoryAsync(_rootPath, ct);
                SetStatus($"Connected to {profile.Name} via {profile.ProtocolLabel}.", success: true);
                Notify(DiscordNotificationCategory.RemoteActivity, "FTP connected",
                    $"Connected to **{profile.Name}** at `{profile.Host}:{profile.Port}` using {profile.ProtocolLabel}.");
            });
        }
        catch (Exception ex) { SetStatus(FriendlyError(ex), error: true); }
    }

    private async void OnConnectRcon(object sender, RoutedEventArgs e)
    {
        if (_rconBusy) return;
        _rconBusy = true;
        RconConnectButton.IsEnabled = false;
        try
        {
            var profile = ReadProfile();
            var password = RconPasswordBox.Password;
            if (password.Length == 0 && _selectedProfile?.Id == profile.Id)
                password = RemoteProfileStore.GetRconPassword(ConfigPath, profile.Id);
            if (password.Length == 0) throw new ArgumentException("Enter the BattlEye RCon password.");

            await DisconnectRconAsync(silent: true);
            var client = new BattlEyeRconClient();
            client.ServerMessageReceived += OnRconServerMessage;
            _rcon = client;
            AppendRcon($"Connecting to {profile.RconEndpointLabel}…");
            await client.ConnectAsync(profile.EffectiveRconHost, profile.RconPort, password);
            RconDisconnectButton.IsEnabled = true;
            AppendRcon("Connected and authenticated. Try 'players' or 'commands'.");
            SetStatus($"BattlEye RCon connected to {profile.RconEndpointLabel}.", success: true);
            Notify(DiscordNotificationCategory.RemoteActivity, "RCon connected",
                $"BattlEye RCon authenticated at `{profile.RconEndpointLabel}`.");
        }
        catch (Exception ex)
        {
            AppendRcon("Connection failed: " + ex.Message);
            SetStatus("RCon connection failed: " + ex.Message, error: true);
            await DisconnectRconAsync(silent: true);
        }
        finally
        {
            _rconBusy = false;
            RconConnectButton.IsEnabled = true;
        }
    }

    private async void OnDisconnectRcon(object sender, RoutedEventArgs e) =>
        await DisconnectRconAsync(silent: false);

    private async Task DisconnectRconAsync(bool silent)
    {
        var client = _rcon;
        _rcon = null;
        RconDisconnectButton.IsEnabled = false;
        if (client is not null)
        {
            client.ServerMessageReceived -= OnRconServerMessage;
            await client.DisposeAsync();
            if (!silent) AppendRcon("Disconnected.");
        }
    }

    private async void OnSendRcon(object sender, RoutedEventArgs e) =>
        await SendRconAsync(RconCommandBox.Text);

    private async void OnRconQuickCommand(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string command }) await SendRconAsync(command);
    }

    private async void OnRconCommandKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SendRconAsync(RconCommandBox.Text);
    }

    private async Task SendRconAsync(string command)
    {
        command = command.Trim();
        if (_rconBusy) return;
        if (_rcon is not { IsConnected: true } client)
        {
            SetStatus("Connect BattlEye RCon first.", error: true);
            return;
        }
        if (command.Length == 0) return;
        _rconBusy = true;
        RconConnectButton.IsEnabled = false;
        try
        {
            AppendRcon("> " + command);
            var response = await client.ExecuteAsync(command);
            AppendRcon(response.Length == 0 ? "(command accepted; no text response)" : response);
            RconCommandBox.Clear();
            SetStatus("RCon command completed.", success: true);
            Notify(DiscordNotificationCategory.AdminActivity, "RCon command executed",
                $"Command `{command.Replace("`", "'")}` was sent to `{_selectedProfile?.RconEndpointLabel ?? "the selected server"}`.");
        }
        catch (Exception ex)
        {
            AppendRcon("Command failed: " + ex.Message);
            SetStatus("RCon command failed: " + ex.Message, error: true);
        }
        finally
        {
            _rconBusy = false;
            RconConnectButton.IsEnabled = true;
        }
    }

    private void OnRconServerMessage(object? sender, string message) => AppendRcon("[SERVER] " + message);

    private void AppendRcon(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => AppendRcon(message)));
            return;
        }
        RconConsoleBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        RconConsoleBox.ScrollToEnd();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_client is null) { SetStatus("Connect to a server first.", error: true); return; }
        await RunAsync(async ct => await LoadDirectoryAsync(_currentPath, ct));
    }

    private async void OnGoPath(object sender, RoutedEventArgs e) => await GoPathAsync();
    private async void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await GoPathAsync(); }
    }

    private async Task GoPathAsync()
    {
        if (_client is null) { SetStatus("Connect to a server first.", error: true); return; }
        var requested = ClampToRoot(PathBox.Text);
        await RunAsync(async ct => await LoadDirectoryAsync(requested, ct));
    }

    private async void OnUp(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        var parent = ClampToRoot(FtpRemoteClient.ParentPath(_currentPath));
        await RunAsync(async ct => await LoadDirectoryAsync(parent, ct));
    }

    private async void OnRemoteDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RemoteGrid.SelectedItem is not RemoteFileEntry entry || _client is null) return;
        if (entry.IsDirectory)
            await RunAsync(async ct => await LoadDirectoryAsync(entry.FullPath, ct));
        else
            await OpenEditorAsync(entry);
    }

    private async Task LoadDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        if (_client is null) return;
        path = ClampToRoot(path);
        var rows = await _client.ListAsync(path, cancellationToken);
        _entries.Clear();
        foreach (var row in rows) _entries.Add(row);
        _currentPath = path;
        PathBox.Text = path;
        EntryCountText.Text = $"{rows.Count} item{(rows.Count == 1 ? "" : "s")} · {_selectedProfile?.EndpointLabel ?? "connected"}";
        UpdateRemoteOperatingSystem(_client.DetectedOperatingSystem);
    }

    private void UpdateRemoteOperatingSystem(RemoteServerOperatingSystem operatingSystem)
    {
        RemoteOsText.Text = operatingSystem switch
        {
            RemoteServerOperatingSystem.LinuxOrUnix => "REMOTE OS: LINUX / UNIX",
            RemoteServerOperatingSystem.Windows => "REMOTE OS: WINDOWS",
            _ => "REMOTE OS: UNKNOWN"
        };
        RemoteOsBadge.Visibility = Visibility.Visible;
        RemoteOsBadge.ToolTip = operatingSystem == RemoteServerOperatingSystem.Unknown
            ? "The FTP server did not return a platform-specific directory listing. File operations remain available."
            : "Detected from the FTP server's directory-listing format.";
    }

    private async Task OpenEditorAsync(RemoteFileEntry entry)
    {
        if (_client is null) return;
        await RunAsync(async ct =>
        {
            SetStatus($"Opening {entry.Name}…");
            var bytes = await _client.DownloadBytesAsync(entry.FullPath, cancellationToken: ct);
            if (!TryDecodeText(bytes, out var text, out var encoding, out var bom))
            {
                _editorEntry = null;
                SetEditor("", false);
                EditorTitle.Text = $"{entry.Name} · binary (download only)";
                SetStatus("This appears to be a binary file. Use Download to save a local copy.", error: true);
                return;
            }
            _editorEntry = entry;
            _editorEncoding = encoding;
            _editorBom = bom;
            EditorTitle.Text = entry.FullPath;
            SetEditor(text, true);
            SetStatus($"Loaded {entry.Name} into the inline editor.", success: true);
        });
    }

    private void SetEditor(string text, bool enabled)
    {
        _settingEditor = true;
        EditorBox.Text = text;
        EditorBox.IsEnabled = enabled;
        _dirty = false;
        DirtyBadge.Visibility = Visibility.Collapsed;
        SaveRemoteButton.IsEnabled = enabled;
        _settingEditor = false;
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingEditor || _editorEntry is null) return;
        _dirty = true;
        DirtyBadge.Visibility = Visibility.Visible;
    }

    private async void OnSaveRemote(object sender, RoutedEventArgs e)
    {
        if (_client is null || _editorEntry is null || !_dirty) return;
        var entry = _editorEntry;
        if (MessageBox.Show(Window.GetWindow(this),
                $"Upload your edits and replace this remote file?\n\n{entry.FullPath}\n\nA local recovery copy will be made first.",
                "Save to server", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        await RunAsync(async ct =>
        {
            var original = await _client.DownloadBytesAsync(entry.FullPath, cancellationToken: ct);
            var backup = WriteRecoveryCopy(entry, original);
            var payload = EncodeEditorText(EditorBox.Text, _editorEncoding, _editorBom);
            await _client.UploadBytesAsync(entry.FullPath, payload, ct);
            _dirty = false;
            DirtyBadge.Visibility = Visibility.Collapsed;
            SetStatus($"Saved {entry.Name}. Recovery copy: {backup}", success: true);
            Notify(DiscordNotificationCategory.RemoteActivity, "Remote file edited",
                $"Saved `{entry.FullPath}` after creating a local recovery copy.");
            await LoadDirectoryAsync(_currentPath, ct);
        });
    }

    private async void OnUpload(object sender, RoutedEventArgs e)
    {
        if (_client is null) { SetStatus("Connect to a server first.", error: true); return; }
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Upload files to the current server folder" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var conflicts = dialog.FileNames.Where(f => _entries.Any(e => !e.IsDirectory &&
            string.Equals(e.Name, Path.GetFileName(f), StringComparison.OrdinalIgnoreCase))).ToList();
        if (conflicts.Count > 0 && MessageBox.Show(Window.GetWindow(this),
                $"{conflicts.Count} selected file(s) already exist and will be replaced. Continue?",
                "Upload files", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        await RunAsync(async ct =>
        {
            foreach (var file in dialog.FileNames)
            {
                SetStatus($"Uploading {Path.GetFileName(file)}…");
                await _client.UploadFileAsync(file, FtpRemoteClient.CombinePath(_currentPath, Path.GetFileName(file)), ct);
            }
            await LoadDirectoryAsync(_currentPath, ct);
            SetStatus($"Uploaded {dialog.FileNames.Length} file(s).", success: true);
            Notify(DiscordNotificationCategory.RemoteActivity, "Files uploaded",
                $"Uploaded **{dialog.FileNames.Length}** file(s) to `{_currentPath}`.");
        });
    }

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        if (_client is null || RemoteGrid.SelectedItem is not RemoteFileEntry { IsDirectory: false } entry) return;
        var dialog = new SaveFileDialog { FileName = entry.Name, Title = "Download remote file" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await RunAsync(async ct =>
        {
            await _client.DownloadFileAsync(entry.FullPath, dialog.FileName, ct);
            SetStatus($"Downloaded {entry.Name} to {dialog.FileName}.", success: true);
            Notify(DiscordNotificationCategory.RemoteActivity, "Remote file downloaded",
                $"Downloaded `{entry.FullPath}`.");
        });
    }

    private async void OnNewFolder(object sender, RoutedEventArgs e)
    {
        if (_client is null || Window.GetWindow(this) is not Window owner) return;
        var name = PromptDialog.Show(owner, "New remote folder", "Folder name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunAsync(async ct =>
        {
            await _client.CreateDirectoryAsync(FtpRemoteClient.CombinePath(_currentPath, name.Trim()), ct);
            await LoadDirectoryAsync(_currentPath, ct);
            SetStatus($"Created folder {name.Trim()}.", success: true);
            Notify(DiscordNotificationCategory.RemoteActivity, "Remote folder created",
                $"Created `{FtpRemoteClient.CombinePath(_currentPath, name.Trim())}`.");
        });
    }

    private async void OnRename(object sender, RoutedEventArgs e)
    {
        if (_client is null || RemoteGrid.SelectedItem is not RemoteFileEntry entry || Window.GetWindow(this) is not Window owner) return;
        var name = PromptDialog.Show(owner, "Rename remote item", "New name:", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name == entry.Name) return;
        await RunAsync(async ct =>
        {
            await _client.RenameAsync(entry.FullPath, name.Trim(), ct);
            await LoadDirectoryAsync(_currentPath, ct);
            SetStatus($"Renamed {entry.Name} to {name.Trim()}.", success: true);
            Notify(DiscordNotificationCategory.RemoteActivity, "Remote item renamed",
                $"Renamed `{entry.FullPath}` to `{name.Trim()}`.");
        });
    }

    private async void OnDeleteRemote(object sender, RoutedEventArgs e)
    {
        if (_client is null || RemoteGrid.SelectedItem is not RemoteFileEntry entry) return;
        if (MessageBox.Show(Window.GetWindow(this),
                $"Permanently delete this remote {(entry.IsDirectory ? "folder" : "file")}?\n\n{entry.FullPath}\n\nFolders must be empty.",
                "Delete remote item", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync(async ct =>
        {
            if (entry.IsDirectory) await _client.DeleteDirectoryAsync(entry.FullPath, ct);
            else await _client.DeleteFileAsync(entry.FullPath, ct);
            await LoadDirectoryAsync(_currentPath, ct);
            SetStatus($"Deleted {entry.Name}.", success: true);
            Notify(DiscordNotificationCategory.RemoteActivity, "Remote item deleted",
                $"Deleted `{entry.FullPath}`.");
        });
    }

    private RemoteServerProfile ReadProfile(bool requireFtp = false)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
            throw new ArgumentException("Enter a valid FTP port from 1 to 65535.");
        if (!int.TryParse(RconPortBox.Text.Trim(), out var rconPort) || rconPort is < 1 or > 65535)
            throw new ArgumentException("Enter a valid RCon port from 1 to 65535.");
        var profile = new RemoteServerProfile
        {
            Id = _selectedProfile?.Id ?? Guid.NewGuid().ToString("N"),
            Name = NameBox.Text.Trim(),
            InstanceName = InstanceBox.SelectedItem as string ?? "",
            Host = RemoteProfileStore.NormalizeHost(HostBox.Text),
            Port = port,
            UserName = UserBox.Text.Trim(),
            RootPath = FtpRemoteClient.NormalizePath(RootBox.Text),
            UseTls = ProtocolBox.SelectedIndex == 0,
            Passive = PassiveBox.IsChecked != false,
            RconHost = RemoteProfileStore.NormalizeHost(RconHostBox.Text),
            RconPort = rconPort
        };
        if (profile.Name.Length == 0) throw new ArgumentException("Enter a profile name.");
        if (requireFtp && profile.Host.Length == 0) throw new ArgumentException("Enter the FTP host name or IP address.");
        if (profile.Host.Length == 0 && profile.RconHost.Length == 0)
            throw new ArgumentException("Enter an FTP or RCon host / IP address.");
        return profile;
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy) return;
        _busy = true;
        _operation = new CancellationTokenSource();
        BusyBar.Visibility = Visibility.Visible;
        ConnectButton.Content = "Cancel";
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { SetStatus("Operation cancelled."); }
        catch (Exception ex)
        {
            var error = FriendlyError(ex);
            SetStatus(error, error: true);
            Notify(DiscordNotificationCategory.LogAlerts, "Remote operation failed", error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _busy = false;
            BusyBar.Visibility = Visibility.Collapsed;
            ConnectButton.Content = "Connect FTP";
        }
    }

    private string ClampToRoot(string path)
    {
        var normalized = FtpRemoteClient.NormalizePath(path);
        if (_rootPath == "/") return normalized;
        return normalized.Equals(_rootPath, StringComparison.Ordinal) ||
               normalized.StartsWith(_rootPath.TrimEnd('/') + "/", StringComparison.Ordinal)
            ? normalized : _rootPath;
    }

    private string WriteRecoveryCopy(RemoteFileEntry entry, byte[] bytes)
    {
        var profile = _selectedProfile?.Id ?? "unsaved";
        var dir = Path.Combine(Path.GetDirectoryName(ConfigPath) ?? ".", "remote-backups", profile,
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(dir);
        var safe = string.Concat(entry.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.Combine(dir, safe + ".before-upload");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static bool TryDecodeText(byte[] bytes, out string text, out Encoding encoding, out bool bom)
    {
        bom = false;
        var offset = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        { encoding = new UTF8Encoding(true); bom = true; offset = 3; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        { encoding = Encoding.Unicode; bom = true; offset = 2; }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        { encoding = Encoding.BigEndianUnicode; bom = true; offset = 2; }
        else
        { encoding = new UTF8Encoding(false, true); }

        if (offset == 0 && bytes.Take(Math.Min(bytes.Length, 4096)).Any(b => b == 0))
        { text = ""; return false; }
        try { text = encoding.GetString(bytes, offset, bytes.Length - offset); return true; }
        catch (DecoderFallbackException) { text = ""; return false; }
    }

    private static byte[] EncodeEditorText(string text, Encoding encoding, bool bom)
    {
        var body = encoding.GetBytes(text);
        if (!bom) return body;
        var prefix = encoding.GetPreamble();
        var result = new byte[prefix.Length + body.Length];
        prefix.CopyTo(result, 0);
        body.CopyTo(result, prefix.Length);
        return result;
    }

    private void SetStatus(string text, bool success = false, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(
            error ? "MissionErrorBrush" : success ? "KmGreenBrush" : "TextFillColorSecondaryBrush");
    }

    private static string FriendlyError(Exception ex)
    {
        var message = ex.Message;
        if (ex is System.Net.WebException { Response: System.Net.FtpWebResponse response })
            message = response.StatusDescription?.Trim() ?? message;
        return "FTP operation failed: " + message;
    }
}
