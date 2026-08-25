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
    private MainViewModel? Vm => DataContext as MainViewModel;

    public SteamAccountPanel()
    {
        InitializeComponent();
        Unloaded += (_, _) => _loginCts?.Cancel();
    }

    public void ExpandAndFocus()
    {
        LoginExpander.IsExpanded = true;
        UserBox.Focus();
    }

    private async void OnStartQr(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = new CancellationTokenSource();
        QrButton.IsEnabled = false;
        QrStatus.Text = "Connecting…";
        QrPlaceholder.Visibility = Visibility.Visible;
        try
        {
            var result = await Vm.SteamLoginQrAsync(url => Dispatcher.Invoke(() =>
            {
                QrImage.Source = MakeQr(url);
                QrPlaceholder.Visibility = Visibility.Collapsed;
                QrStatus.Text = "Approve in Steam mobile";
            }), _loginCts.Token);
            Finish(result, QrStatus);
        }
        catch (OperationCanceledException) { QrStatus.Text = "Cancelled"; }
        finally { QrButton.IsEnabled = true; }
    }

    private async void OnCredentialSignIn(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var user = UserBox.Text.Trim();
        if (user.Length == 0 || PassBox.Password.Length == 0)
        { PassStatus.Text = "Enter username and password."; return; }

        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = new CancellationTokenSource();
        SignInButton.IsEnabled = false;
        PassStatus.Text = "Signing in…";
        try
        {
            var result = await Vm.SteamLoginCredentialsAsync(user, PassBox.Password,
                new InlineAuthenticator(this), _loginCts.Token);
            PassBox.Clear();
            Finish(result, PassStatus);
        }
        catch (OperationCanceledException) { PassStatus.Text = "Cancelled"; }
        finally { SignInButton.IsEnabled = true; }
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
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
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
        public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(true);
    }
}
