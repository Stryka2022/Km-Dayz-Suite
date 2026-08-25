using System.Drawing;          // Bitmap / Graphics.CopyFromScreen (System.Drawing.Common, already used by the tray icon)
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dzl.Tray;

/// <summary>
/// Captures the app as it looks on screen — the main window plus every open child window (Workshop, modals) —
/// by grabbing the screen region that bounds all of them. Uses the same System.Drawing path the tray icon
/// already relies on, so no new dependency. Saves a PNG under &lt;configDir&gt;\screenshots and returns its path.
/// <para>Window bounds come from Win32 <c>GetWindowRect</c> (true physical pixels, correct in any window state)
/// rather than WPF <c>Left</c>/<c>Top</c> — those report the RESTORE position for a MAXIMIZED window, which made
/// a full-screen capture grab the wrong region. Each window is then clamped to its monitor's WORK AREA so a
/// maximized window's border overhang can't pull the Windows taskbar into the shot.</para>
/// </summary>
internal static class AppScreenshot
{
    /// <summary>
    /// Render one WPF window directly to a PNG.  The smoke harness uses this path so visual-regression
    /// captures remain reliable even when another desktop window is temporarily above Server Manager.
    /// The user-facing screenshot button keeps using <see cref="Capture"/> so it can include dialogs.
    /// </summary>
    public static string CaptureRenderedWindow(string configPath, Window window, string? label = null)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var dir = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "screenshots");
        Directory.CreateDirectory(dir);
        var safeLabel = string.IsNullOrWhiteSpace(label) ? "render" : label.Replace(':', '-');
        var path = Path.Combine(dir, $"aph-havoc-{safeLabel}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }

    public static string Capture(string configPath)
    {
        // Only our own real windows (MainWindow / Workshop / modals). Excludes the hidden H.NotifyIcon tray-host
        // window — it lives in another assembly and would otherwise blow the union out to empty desktop.
        var ours = typeof(MainWindow).Assembly;
        var rects = Application.Current.Windows.OfType<Window>()
            .Where(w => w.GetType().Assembly == ours && w.IsVisible
                        && w.WindowState != WindowState.Minimized && w.ActualWidth > 0 && w.ActualHeight > 0)
            .Select(w => new WindowInteropHelper(w).Handle)
            .Where(h => h != IntPtr.Zero)
            .Select(OnScreenRect)
            .OfType<RECT>()                                   // drop windows whose rect couldn't be read
            .Where(r => r.Right > r.Left && r.Bottom > r.Top)
            .ToList();
        if (rects.Count == 0) throw new InvalidOperationException("no visible windows to capture");

        // Union of the on-screen (work-area-clamped) window rectangles — already physical pixels.
        int left = rects.Min(r => r.Left), top = rects.Min(r => r.Top);
        int right = rects.Max(r => r.Right), bottom = rects.Max(r => r.Bottom);
        int pw = Math.Max(1, right - left), ph = Math.Max(1, bottom - top);

        using var bmp = new Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(pw, ph), CopyPixelOperation.SourceCopy);

        var dir = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"aph-havoc-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>The window's true on-screen rectangle, clamped to its monitor's work area (so a maximized
    /// window's overhang doesn't bleed onto the taskbar/adjacent monitor). Null if the handle has no rect.</summary>
    private static RECT? OnScreenRect(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return null;
        var mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
            r = new RECT
            {
                Left = Math.Max(r.Left, mi.rcWork.Left),
                Top = Math.Max(r.Top, mi.rcWork.Top),
                Right = Math.Min(r.Right, mi.rcWork.Right),
                Bottom = Math.Min(r.Bottom, mi.rcWork.Bottom),
            };
        return r;
    }

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
