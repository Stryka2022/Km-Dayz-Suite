using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Dzl.Tray.ViewModels;
using QRCoder;
using SteamKit2.Authentication;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>Steam sign-in dialog: QR (scan with the mobile app) or username/password (+ Steam Guard via a
/// dialog-driven <see cref="IAuthenticator"/>). On success the VM stores the refresh token; the dialog closes.</summary>
public partial class SteamLoginWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private CancellationTokenSource? _loginCts;
    private TaskCompletionSource<string>? _guard;
    private int _attemptId;

    public bool SignedIn { get; private set; }

    public SteamLoginWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Loaded += async (_, _) => await StartQrAsync();
        Closed += (_, _) => CancelActiveAttempt();
    }

    private async Task StartQrAsync()
    {
        var (attemptId, token) = BeginAttempt();
        SignInBtn.IsEnabled = true;
        QrRetryButton.IsEnabled = false;
        QrImage.Source = null;
        QrStatus.Text = "Connecting to Steam…";
        try
        {
            var r = await _vm.SteamLoginQrAsync(
                url => Dispatcher.Invoke(() =>
                {
                    if (!IsCurrent(attemptId)) return;
                    QrImage.Source = MakeQr(url);
                    QrStatus.Text = "Scan with Steam Guard, then approve the sign-in on your phone.";
                }), token);
            if (IsCurrent(attemptId)) OnResult(r);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(attemptId)) QrStatus.Text = "QR sign-in cancelled.";
        }
        finally
        {
            if (IsCurrent(attemptId)) QrRetryButton.IsEnabled = true;
        }
    }

    private async void OnRetryQr(object sender, RoutedEventArgs e) => await StartQrAsync();

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        var user = UserBox.Text.Trim();
        if (user.Length == 0 || PassBox.Password.Length == 0) { PassStatus.Text = "Enter username + password."; return; }
        var password = PassBox.Password;
        var (attemptId, token) = BeginAttempt();
        QrRetryButton.IsEnabled = true;
        SignInBtn.IsEnabled = false;
        PassStatus.Text = "Signing in…";
        try
        {
            var task = _vm.SteamLoginCredentialsAsync(user, password, new DialogAuthenticator(this), token);
            PassBox.Clear();
            var r = await task;
            if (IsCurrent(attemptId)) OnResult(r, password: true);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(attemptId)) PassStatus.Text = "Sign-in cancelled.";
        }
        finally
        {
            if (IsCurrent(attemptId)) SignInBtn.IsEnabled = true;
        }
    }

    private void OnResult(Dzl.Core.Workshop.SteamLoginResult r, bool password = false)
    {
        if (r.Ok)
        {
            SignedIn = true;
            DialogResult = true;
            Close();
            return;
        }
        var msg = "✗ " + (string.IsNullOrWhiteSpace(r.Error) ? "sign-in failed" : r.Error);
        if (password) PassStatus.Text = msg; else QrStatus.Text = msg;
    }

    // Steam Guard prompt — driven by the IAuthenticator below.
    private Task<string> PromptGuardAsync(string prompt)
    {
        _guard?.TrySetCanceled();
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

    private void OnGuardOk(object sender, RoutedEventArgs e)
    {
        var code = GuardBox.Text.Trim();
        GuardPanel.Visibility = Visibility.Collapsed;
        _guard?.TrySetResult(code);
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

    private static BitmapImage MakeQr(string url)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.L);
        var png = new PngByteQRCode(data).GetGraphic(12);
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(png);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // Prefer phone approval; fall back to prompting for an email/device code.
    private sealed class DialogAuthenticator : IAuthenticator
    {
        private readonly SteamLoginWindow _w;
        public DialogAuthenticator(SteamLoginWindow w) => _w = w;
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
            => _w.PromptGuardAsync($"Enter your Steam Guard (mobile authenticator) code{(previousCodeWasIncorrect ? " — last one was wrong" : "")}:");
        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
            => _w.PromptGuardAsync($"Enter the Steam Guard code emailed to {email}{(previousCodeWasIncorrect ? " — last one was wrong" : "")}:");
        public Task<bool> AcceptDeviceConfirmationAsync() => _w.WaitForMobileApprovalAsync();
    }
}
