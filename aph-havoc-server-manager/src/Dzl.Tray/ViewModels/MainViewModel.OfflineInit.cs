using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.Economy;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // Dashboard "Offline mission" card (offline instances only): does the mission init.c carry the
    // dzl offline bootstrap? A vanilla MissionServer mission hangs on a lone diag client because no
    // character spawns; the Fix button injects a client mission that spawns one (backup kept).

    [ObservableProperty] private bool _offlineInitCardVisible;
    [ObservableProperty] private string _offlineInitKind = "unknown";   // patched | needspatch | nomission
    [ObservableProperty] private string _offlineInitStatusLabel = "—";
    [ObservableProperty] private string _offlineInitMessage = "";
    [ObservableProperty] private bool _offlineInitFixVisible;

    private void RefreshOfflineInit()
    {
        var r = _svc.CheckOfflineInit();
        // Relevant wherever a mission can boot offline (offline instance, or "Menu only" on a normal
        // one) — hide only when there's no mission init.c to reason about.
        OfflineInitCardVisible = r.Status != OfflineInitStatus.NoMission;
        (OfflineInitKind, OfflineInitStatusLabel) = r.Status switch
        {
            OfflineInitStatus.Patched    => ("patched", "Ready"),
            OfflineInitStatus.NeedsPatch => ("needspatch", "Needs patch"),
            _                            => ("nomission", "No mission"),
        };
        OfflineInitMessage = r.Message;
        OfflineInitFixVisible = r.Patchable;
    }

    [RelayCommand]
    private void PatchOfflineInit() => RunOp(() =>
    {
        var res = _svc.PatchOfflineInit();
        _dispatcher.Invoke(() =>
        {
            RefreshOfflineInit();
            StatusLine = (res.Ok ? "✓ " : "✗ ") + res.Message;
        });
    });

    /// <summary>Copy the offline-bootstrap snippet to the clipboard (hand-edit fallback).</summary>
    [RelayCommand]
    private void CopyOfflineInitSnippet()
    {
        try { System.Windows.Clipboard.SetText(OfflineInit.Snippet); }
        catch { /* clipboard busy — best-effort */ }
    }
}
