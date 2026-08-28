using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Dzl.Tray.ViewModels;
using Wpf.Ui.Controls;

namespace Dzl.Tray;

/// <summary>
/// The launcher main window: a Wpf.Ui <see cref="FluentWindow"/> with a title bar, a
/// persistent top action bar (mode toggle, profile switcher, server/client status pills)
/// and a left ListBox-based nav rail that swaps between five content panels
/// (Dashboard, Mods, Logs, Tools, Settings). All five panels are fully built; the Logs,
/// Tools and Settings pages own their interaction logic here (auto-scroll, file/folder
/// pickers, background tool runs and inline config/params editing — no modal dialogs).
/// </summary>
public partial class MainWindow : FluentWindow
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private static readonly uint WmNavigate = RegisterWindowMessage("KM_SUITE_APH_SERVER_MANAGER_NAV_V1");

    private readonly MainViewModel _vm;
    private DispatcherTimer? _kmEmbeddingWatch;
    private DispatcherTimer? _kmNavigationWatch;
    private nint _kmHostHandle;
    private HwndSource? _windowSource;
    private string? _kmNavigationFile;
    private string? _lastKmNavigationRequest;
    private FrameworkElement? _visiblePage;
    private string _serverEditorReturnPage = "servers";
    private string _setupReturnPage = "dashboard";
    private SetupWizardWindow? _embeddedSetupWizard;

    public MainWindow()
    {
        InitializeComponent();
        if (App.IsKmSuiteEmbedded)
        {
            TitleBarRow.Height = new GridLength(0);
            MainTitleBar.Visibility = Visibility.Collapsed;
            // KM owns the only visible Server Manager navigation in embedded mode. Keep the
            // companion rail for standalone GPL use, but remove the duplicate from KM's window.
            NavigationRailColumn.Width = new GridLength(0);
            CompanionNavigationRail.Visibility = Visibility.Collapsed;
            CompanionContentHost.Margin = new Thickness(10);
            ShowInTaskbar = false;
            MinWidth = 0;
            MinHeight = 0;
        }
        _vm = new MainViewModel(App.ConfigPath());
        DataContext = _vm;
        Closed += (_, _) =>
        {
            _kmEmbeddingWatch?.Stop();
            _kmNavigationWatch?.Stop();
            LogWindow.CloseAll();
            _vm.Dispose();
            if (App.IsKmSuiteEmbedded) Application.Current.Shutdown();
        };

        // Select Dashboard on load so a panel is always visible; selecting the first
        // NavTop item raises OnNavChanged, which calls ShowPage("dashboard").
        Loaded += (_, _) =>
        {
            NavGeneral.SelectedIndex = 0;   // Dashboard
            // Wpf.Ui finalises FluentWindow's DWM chrome while Window.Show() unwinds.
            // Re-parenting during Loaded turns the HWND into a child too early and makes
            // that top-level chrome update fail. ApplicationIdle runs after Show returns.
            if (App.IsKmSuiteEmbedded)
            {
                StartKmNavigationChannel();
                _ = Dispatcher.BeginInvoke(new Action(StartKmEmbedding), DispatcherPriority.ApplicationIdle);
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(OnKmHostMessage);
    }

    private nint OnKmHostMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message != WmNavigate) return nint.Zero;
        handled = true;
        var code = wParam.ToInt32();
        Dispatcher.BeginInvoke(new Action(() => NavigateFromKmHost(code)), DispatcherPriority.Normal);
        return nint.Zero;
    }

    private void NavigateFromKmHost(int code)
    {
        var tag = code switch
        {
            1 => "dashboard", 2 => "servers", 3 => "workshop", 4 => "mods",
            5 => "economy", 6 => "remote", 7 => "logs", 8 => "bases",
            9 => "mymods", 10 => "tools", 11 => "setup", 12 => "settings",
            13 => "about", 14 => "mcp", 15 => "notifications", _ => ""
        };
        NavigateFromKmHost(tag);
    }

    private void NavigateFromKmHost(string tag)
    {
        tag = tag.Trim().ToLowerInvariant();
        if (tag is not ("dashboard" or "servers" or "workshop" or "mods" or "economy" or
            "remote" or "logs" or "bases" or "mymods" or "tools" or "setup" or
            "settings" or "about" or "mcp" or "notifications")) return;
        if (tag == "setup")
        {
            OpenSetupWizard();
            return;
        }

        // KM owns the visible navigation rail in embedded mode. Bypassing the hidden ListBoxes
        // avoids two SelectionChanged passes per click and makes duplicate fallback requests a
        // no-op instead of re-measuring the same heavy page.
        if (App.IsKmSuiteEmbedded)
        {
            if (_currentPageTag == tag && _visiblePage is not null) return;
            _currentPageTag = tag;
            ShowPage(tag);
            return;
        }

        foreach (var rail in NavRails) rail.SelectedItem = null;
        foreach (var rail in NavRails)
            if (TrySelect(rail, tag)) return;
    }

    private void StartKmNavigationChannel()
    {
        _kmNavigationFile = Environment.GetEnvironmentVariable("KM_SUITE_NAV_FILE");
        if (string.IsNullOrWhiteSpace(_kmNavigationFile)) return;

        ReadKmNavigationRequest();
        _kmNavigationWatch = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _kmNavigationWatch.Tick += (_, _) => ReadKmNavigationRequest();
        _kmNavigationWatch.Start();
    }

    private void ReadKmNavigationRequest()
    {
        var path = _kmNavigationFile;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var request = File.ReadAllText(path).Trim();
            if (request.Length == 0 || request.Equals(_lastKmNavigationRequest, StringComparison.Ordinal)) return;
            var separator = request.IndexOf('|');
            if (separator < 0 || separator == request.Length - 1) return;
            _lastKmNavigationRequest = request;
            NavigateFromKmHost(request[(separator + 1)..]);
        }
        catch (IOException) { /* the next timer tick retries a partial/concurrent write */ }
        catch (UnauthorizedAccessException) { /* registered messages remain available */ }
    }

    /// <summary>
    /// Cooperates with the proprietary host through HWND parenting only. Keeping this code in the
    /// GPL process makes WPF apply the child style after FluentWindow has completed its top-level
    /// initialization; the KM process still neither links nor loads this assembly.
    /// </summary>
    private void StartKmEmbedding()
    {
        var rawHost = Environment.GetEnvironmentVariable("KM_SUITE_HOST_HWND");
        if (!long.TryParse(rawHost, out var hostValue) || hostValue == 0) return;
        _kmHostHandle = new nint(hostValue);

        AttachToKmHost();
        _kmEmbeddingWatch = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _kmEmbeddingWatch.Tick += (_, _) => AttachToKmHost();
        _kmEmbeddingWatch.Start();
    }

    private void AttachToKmHost()
    {
        if (_kmHostHandle == nint.Zero) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero) return;

        var changedFrame = false;
        var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
        if ((style & WsChild) == 0 || (style & (WsPopup | WsCaption | WsThickFrame | WsSysMenu)) != 0)
        {
            style &= ~(WsPopup | WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
            style |= WsChild | WsVisible;
            SetWindowLongPtr(handle, GwlStyle, new nint(style));

            var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            exStyle &= ~WsExAppWindow;
            exStyle |= WsExToolWindow;
            SetWindowLongPtr(handle, GwlExStyle, new nint(exStyle));
            changedFrame = true;
        }

        if (GetParent(handle) != _kmHostHandle)
        {
            Marshal.SetLastPInvokeError(0);
            if (SetParent(handle, _kmHostHandle) == nint.Zero && Marshal.GetLastWin32Error() != 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            changedFrame = true;
        }

        if (!GetClientRect(_kmHostHandle, out var bounds)) return;
        var width = Math.Max(1, bounds.Right);
        var height = Math.Max(1, bounds.Bottom);
        if (!changedFrame && GetClientRect(handle, out var current) &&
            current.Right == width && current.Bottom == height)
            return;

        SetWindowPos(handle, nint.Zero, 0, 0, width, height,
            SwpNoZOrder | SwpShowWindow | (changedFrame ? SwpFrameChanged : 0));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint newParent);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint child);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    /// <summary>Every nav rail (grouped sections + pinned system group). Selection is single across all of
    /// them — selecting in one clears the others.</summary>
    private ListBox[] NavRails => new[] { NavGeneral, NavServer, NavBottom };

    // --- Navigation: swap the visible content panel based on the selected rail item ---

    /// <summary>True while we are programmatically restoring the rail selection (after the
    /// "Setup" pseudo-item opened the wizard) so the resulting SelectionChanged is ignored.</summary>
    private bool _restoringNav;

    /// <summary>Tag of the last real content page shown, so "Setup" (a button-like item) can
    /// restore the rail to it instead of becoming a sticky selection.</summary>
    private string _currentPageTag = "dashboard";

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringNav) return;
        if (sender is ListBox lb && lb.SelectedItem is ListBoxItem { Tag: string tag })
        {
            // "Setup" is not a page: open the wizard, then bounce the selection back.
            if (tag == "setup")
            {
                OpenSetupWizard();
                RestoreNavToCurrentPage();
                return;
            }

            // Clear every other rail's selection so only one item looks active.
            foreach (var rail in NavRails)
                if (!ReferenceEquals(rail, lb)) rail.SelectedItem = null;
            _currentPageTag = tag;
            ShowPage(tag);
        }
    }

    /// <summary>Reselect the rail item for <see cref="_currentPageTag"/> without re-running page
    /// logic (guarded so the programmatic SelectionChanged is a no-op).</summary>
    private void RestoreNavToCurrentPage()
    {
        _restoringNav = true;
        try
        {
            foreach (var rail in NavRails) rail.SelectedItem = null;
            foreach (var rail in NavRails)
                if (TrySelect(rail, _currentPageTag)) break;
        }
        finally { _restoringNav = false; }
    }

    private static bool TrySelect(ListBox rail, string tag)
    {
        foreach (var obj in rail.Items)
            if (obj is ListBoxItem { Tag: string t } item && t == tag)
            {
                rail.SelectedItem = item;
                return true;
            }
        return false;
    }

    private void ShowPage(string tag)
    {
        if (PageDashboard is null) return; // not yet templated
        FrameworkElement? target = tag switch
        {
            "dashboard" => PageDashboard,
            "mods" => PageMods,
            "workshop" => PageWorkshop,
            "notifications" => PageNotifications,
            "remote" => PageRemoteFiles,
            "mymods" => PageMyMods,
            "servers" => PageServers,
            "servereditor" => PageServerEditor,
            "setupinline" => PageSetup,
            "bases" => PageBases,
            "economy" => PageEconomy,
            "logs" => PageLogs,
            "tools" => PageTools,
            "mcp" => PageMcp,
            "settings" => PageSettings,
            "about" => PageAbout,
            _ => null
        };
        if (target is null || ReferenceEquals(_visiblePage, target)) return;

        if (_visiblePage is not null)
            _visiblePage.Visibility = Visibility.Collapsed;
        else
        {
            // First navigation only: collapse the pages that XAML initially keeps alive.
            foreach (var page in new FrameworkElement[]
                     {
                         PageDashboard, PageMods, PageWorkshop, PageNotifications, PageRemoteFiles, PageMyMods,
                         PageServers, PageServerEditor, PageSetup, PageBases, PageEconomy, PageLogs, PageTools, PageMcp,
                         PageSettings, PageAbout
                     })
                page.Visibility = Visibility.Collapsed;
        }
        target.Visibility = Visibility.Visible;
        _visiblePage = target;

        // Refresh page-local state on show.
        if (tag == "tools") PageTools.RefreshToolsPage();
        if (tag == "mymods") PageMyMods.RefreshOnShow();
        if (tag == "servers") { _vm.RefreshServers(); _vm.RefreshBases(); }   // base dropdown needs bases
        if (tag == "bases") _vm.RefreshBases();
        if (tag == "remote") PageRemoteFiles.RefreshOnShow();
        if (tag == "notifications") PageNotifications.Reload();
        if (tag == "settings") { PageSettings.Reload(); _ = _vm.RefreshGitHubAuthAsync(); _vm.RefreshSteamAccount(); }
    }

    /// <summary>Open the active instance editor inside the current Server Manager window.</summary>
    internal void OpenServerEditor(int tab, string returnPage = "servers")
    {
        _serverEditorReturnPage = returnPage;
        PageServerEditor.Content = new ServerEditorWindow(_vm, tab, CloseServerEditor);
        _currentPageTag = "servereditor";
        foreach (var rail in NavRails) rail.SelectedItem = null;
        ShowPage("servereditor");
    }

    private void CloseServerEditor()
    {
        PageServerEditor.Content = null;
        _vm.RefreshServers();
        _currentPageTag = _serverEditorReturnPage;
        ShowPage(_serverEditorReturnPage);
        RestoreNavToCurrentPage();
    }

    // --- Economy window (modeless, single instance) ------------------------

    private EconomyWindow? _economyWin;

    /// <summary>Open (or focus) the Central Economy editor window. Modeless and ownerless on
    /// purpose: an owned Mica FluentWindow hides its owner when closed, and the editor must not
    /// block the main window. The shared MainViewModel keeps all editor state across closes.</summary>
    private void OpenEconomyWindow()
    {
        if (_economyWin is { } w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            BringToFront(w);
            return;
        }
        _economyWin = new EconomyWindow(_vm);
        _economyWin.Closed += (_, _) => _economyWin = null;
        _economyWin.Show();
        BringToFront(_economyWin);
    }

    /// <summary>Force an ownerless modeless window above the main window on open/focus. A fresh
    /// FluentWindow can otherwise come up behind the (larger) main window, so the click looks like
    /// nothing happened. The brief Topmost flip pulls it to the front, then releases it so it does
    /// not stay pinned over everything.</summary>
    private static void BringToFront(Window w)
    {
        w.Topmost = true;
        w.Activate();
        w.Topmost = false;
    }

    // --- Top action bar handlers ------------------------------------------

    private void OnModeToggleClick(object sender, RoutedEventArgs e)
    {
        if (_vm.ToggleModeCommand.CanExecute(null))
            _vm.ToggleModeCommand.Execute(null);
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm.SwitchPresetCommand.CanExecute(null))
            _vm.SwitchPresetCommand.Execute(null);
    }

    // === Work drive (bottom status bar) ===================================

    // The Tools + Settings pages own their own copies (ToolsView / SettingsView); this stays for
    // the app-wide bottom status bar's "Mount P:" quick button.
    private void OnMountWorkDrive(object sender, RoutedEventArgs e) => _vm.MountWorkDrive();

    // === Setup wizard =====================================================

    /// <summary>Open the Setup Wizard modally; on Finish, reload the VM so the new config/profile
    /// takes effect immediately, and re-read the Settings page. Shared by the Settings page's
    /// "Run setup wizard…" button (SettingsView) and the "Setup" nav-rail item.</summary>
    internal void OpenSetupWizard()
    {
        if (App.IsKmSuiteEmbedded)
        {
            _setupReturnPage = _currentPageTag is "setupinline" or "servereditor" ? "dashboard" : _currentPageTag;
            _embeddedSetupWizard = new SetupWizardWindow(App.ConfigPath());
            PageSetup.Content = _embeddedSetupWizard.CreateEmbedded(CloseEmbeddedSetup);
            _currentPageTag = "setupinline";
            ShowPage("setupinline");
            return;
        }
        var wizard = new SetupWizardWindow(App.ConfigPath());
        wizard.Owner = this;
        if (wizard.ShowDialog() == true)
        {
            _vm.Reload();
            PageSettings.Reload();
        }
    }

    private void CloseEmbeddedSetup(bool saved)
    {
        PageSetup.Content = null;
        _embeddedSetupWizard = null;
        if (saved)
        {
            _vm.Reload();
            PageSettings.Reload();
        }
        _currentPageTag = _setupReturnPage;
        ShowPage(_setupReturnPage);
        RestoreNavToCurrentPage();
    }

    /// <summary>Re-read the global Settings page from the live config. Called by the Mods / My Mods
    /// views after the per-module settings modal closes, so the Settings page mirrors any config
    /// the module edited (the pages are never visible at once, but this keeps state consistent).</summary>
    /// <summary>Programmatically navigate to a nav tag (used by the screenshot smoke) — selects the
    /// rail item so the normal OnNavChanged flow runs (shows the page, or opens Economy/Setup).</summary>
    public void NavigateTo(string tag)
    {
        foreach (var rail in NavRails)
            foreach (var obj in rail.Items)
                if (obj is ListBoxItem { Tag: string t } item && t == tag)
                {
                    rail.SelectedItem = item;
                    return;
                }
    }

    internal void SyncSettingsPage() => PageSettings.Reload();

    // (The Servers / My Mods / Settings pages now live in Views/ServersView, MyModsView and
    //  SettingsView; per-server settings + the launch-params editor live in ServerEditorWindow.)
}
