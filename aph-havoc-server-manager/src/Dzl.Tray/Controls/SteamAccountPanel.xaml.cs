using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Dzl.Core.Workshop;
using Dzl.Tray.ViewModels;
using QRCoder;
using SteamKit2.Authentication;

namespace Dzl.Tray.Controls;

/// <summary>Inline Steam login used by the in-tab Workshop page.</summary>
public partial class SteamAccountPanel : UserControl
{
    private CancellationTokenSource? _loginCts;
    private TaskCompletionSource<string>? _guard;
    private int _attemptId;
    private MainViewModel? Vm => DataContext as MainViewModel;

    public SteamAccountPanel()
    {
        InitializeComponent();
        Unloaded += (_, _) => CancelActiveAttempt();
    }

    public void ExpandAndFocus()
    {
        LoginExpander.IsExpanded = true;
        UserBox.Focus();
    }

    private async void OnStartQr(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var (attemptId, token) = BeginAttempt();
        SignInButton.IsEnabled = true;
        QrButton.IsEnabled = false;
        QrStatus.Text = "Connecting…";
        QrImage.Source = null;
        QrPlaceholder.Visibility = Visibility.Visible;
        try
        {
            var result = await Vm.SteamLoginQrAsync(url => Dispatcher.Invoke(() =>
            {
                if (!IsCurrent(attemptId)) return;
                QrImage.Source = MakeQr(url);
                QrPlaceholder.Visibility = Visibility.Collapsed;
                QrStatus.Text = "Scan in Steam Guard and approve";
            }), token);
            if (IsCurrent(attemptId)) Finish(result, QrStatus);
        }
        catch (OperationCanceledException) { if (IsCurrent(attemptId)) QrStatus.Text = "Cancelled"; }
        finally { if (IsCurrent(attemptId)) QrButton.IsEnabled = true; }
    }

    private async void OnCredentialSignIn(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var user = UserBox.Text.Trim();
        if (user.Length == 0 || PassBox.Password.Length == 0)
        { PassStatus.Text = "Enter username and password."; return; }

        var password = PassBox.Password;
        var (attemptId, token) = BeginAttempt();
        QrButton.IsEnabled = true;
        SignInButton.IsEnabled = false;
        PassStatus.Text = "Signing in…";
        try
        {
            var task = Vm.SteamLoginCredentialsAsync(user, password,
                new InlineAuthenticator(this), token);
            PassBox.Clear();
            var result = await task;
            if (IsCurrent(attemptId)) Finish(result, PassStatus);
        }
        catch (OperationCanceledException) { if (IsCurrent(attemptId)) PassStatus.Text = "Cancelled"; }
        finally { if (IsCurrent(attemptId)) SignInButton.IsEnabled = true; }
    }

    private void Finish(SteamLoginResult result, TextBlock target)
    {
        if (!result.Ok)
        {
            target.Text = "Sign-in failed: " + (string.IsNullOrWhiteSpace(result.Error) ? "Steam rejected the request." : result.Error);
            return;
        }
        target.Text = "Signed in";
        if (Vm is not null)
        {
            Vm.NotifyWorkshopGate();
            Vm.RefreshSteamAccount();
            Vm.RefreshSubscribed();
        }
        LoginExpander.IsExpanded = false;
    }

    private void OnGuardOk(object sender, RoutedEventArgs e)
    {
        var code = GuardBox.Text.Trim();
        GuardPanel.Visibility = Visibility.Collapsed;
        _guard?.TrySetResult(code);
    }

    private Task<string> PromptGuardAsync(string prompt)
    {
        _guard = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.Invoke(() =>
        {
            GuardPrompt.Text = prompt;
            GuardPanel.Visibility = Visibility.Visible;
            GuardBox.Text = "";
            GuardBox.Focus();
        });
        return _guard.Task;
    }

    private Task<bool> WaitForMobileApprovalAsync()
    {
        Dispatcher.Invoke(() => PassStatus.Text = "Approve this sign-in in the Steam mobile app…");
        return Task.FromResult(true);
    }

    private (int id, CancellationToken token) BeginAttempt()
    {
        _guard?.TrySetCanceled();
        _guard = null;
        GuardPanel.Visibility = Visibility.Collapsed;
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = new CancellationTokenSource();
        return (++_attemptId, _loginCts.Token);
    }

    private bool IsCurrent(int attemptId) => attemptId == _attemptId && !(_loginCts?.IsCancellationRequested ?? true);

    private void CancelActiveAttempt()
    {
        _attemptId++;
        _guard?.TrySetCanceled();
        _guard = null;
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = null;
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (MessageBox.Show(Window.GetWindow(this), "Sign out of Steam in Server Manager?",
                "Steam account", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Vm.SteamSignOut();
        Vm.NotifyWorkshopGate();
        Vm.RefreshSteamAccount();
        Vm.RefreshSubscribed();
        LoginExpander.IsExpanded = true;
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (Vm is null || Window.GetWindow(this) is not Window owner) return;
        new ModuleSettingsWindow(Vm, "workshop") { Owner = owner }.ShowDialog();
        Vm.NotifyWorkshopGate();
        Vm.RefreshSteamAccount();
        Vm.RefreshSubscribed();
    }

    private static BitmapImage MakeQr(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.L);
        var png = new PngByteQRCode(data).GetGraphic(12);
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(png);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private sealed class InlineAuthenticator : IAuthenticator
    {
        private readonly SteamAccountPanel _panel;
        public InlineAuthenticator(SteamAccountPanel panel) => _panel = panel;
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) =>
            _panel.PromptGuardAsync("Steam Guard mobile code" + (previousCodeWasIncorrect ? " (try again)" : ""));
        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) =>
            _panel.PromptGuardAsync($"Code emailed to {email}" + (previousCodeWasIncorrect ? " (try again)" : ""));
        public Task<bool> AcceptDeviceConfirmationAsync() => _panel.WaitForMobileApprovalAsync();
    }
}
