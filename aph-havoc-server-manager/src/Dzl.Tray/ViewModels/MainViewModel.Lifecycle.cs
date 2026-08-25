using CommunityToolkit.Mvvm.Input;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // --- Server / client ops (background; call the tray's LauncherService) -

    private void RunOp(Action op) => Task.Run(() =>
    {
        try { op(); } catch { /* surfaced via status poll */ }
        finally { _dispatcher.BeginInvoke(() => _ = RefreshStatusAsync()); }
    });

    private void RunLifecycleOp(Func<Dzl.Core.App.OpResult> op, string action) => Task.Run(async () =>
    {
        Dzl.Core.App.OpResult result;
        try { result = op(); }
        catch (Exception ex) { result = new(false, ex.Message); }
        await NotifyDiscordAsync(
            result.Ok ? Dzl.Core.App.DiscordNotificationCategory.ServerLifecycle : Dzl.Core.App.DiscordNotificationCategory.LogAlerts,
            $"Server {action}", result.Ok ? $"**{ActivePreset}**: {result.Message}" : $"**{ActivePreset}** failed: {result.Message}");
        _ = _dispatcher.BeginInvoke(() => _ = RefreshStatusAsync());
    });

    // Buttons follow the live state (Start disabled while up, Stop/Restart while down). The
    // status poll lags ~1.5s behind reality, so LauncherService's AlreadyUpGuard still backstops
    // a double-click in that window.

    [RelayCommand(CanExecute = nameof(CanStartServer))]
    private void StartServer() => RunLifecycleOp(() => _svc.StartTarget("server", Mode), "started");
    private bool CanStartServer() => !ServerUp;

    [RelayCommand(CanExecute = nameof(CanStopServer))]
    private void StopServer() => RunLifecycleOp(() => _svc.StopTarget("server"), "stopped");
    private bool CanStopServer() => ServerUp;

    [RelayCommand(CanExecute = nameof(CanStopServer))]
    private void RestartServer() => RunLifecycleOp(() => _svc.RestartTarget("server", Mode), "restarted");

    [RelayCommand(CanExecute = nameof(CanStartClient))]
    private void StartClient() => RunOp(() => _svc.StartTarget("client", Mode));
    private bool CanStartClient() => !ClientUp;

    /// <summary>Start the client without <c>-connect</c>: mods + mission load, the game
    /// stays in the main menu (diag boots the mission offline) instead of auto-joining.</summary>
    [RelayCommand(CanExecute = nameof(CanStartClient))]
    private void StartClientNoConnect() => RunOp(() => _svc.StartTarget("client", Mode, connect: false));

    [RelayCommand(CanExecute = nameof(CanStopClient))]
    private void StopClient() => RunOp(() => _svc.StopTarget("client"));
    private bool CanStopClient() => ClientUp;

    [RelayCommand(CanExecute = nameof(CanStopClient))]
    private void RestartClient() => RunOp(() => _svc.RestartTarget("client", Mode));

    partial void OnServerUpChanged(bool value)
    {
        StartServerCommand.NotifyCanExecuteChanged();
        StopServerCommand.NotifyCanExecuteChanged();
        RestartServerCommand.NotifyCanExecuteChanged();
    }

    partial void OnClientUpChanged(bool value)
    {
        StartClientCommand.NotifyCanExecuteChanged();
        StopClientCommand.NotifyCanExecuteChanged();
        RestartClientCommand.NotifyCanExecuteChanged();
        StartClientNoConnectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleMode()
    {
        Mode = Mode == "debug" ? "normal" : "debug";
        Persist();
    }
}
