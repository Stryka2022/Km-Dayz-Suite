using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Dzl.Core.Config;
using Dzl.Core.Servers;
using Dzl.Tray;
using Dzl.Tray.Views;
using Dzl.Tray.ViewModels;
using FluentAssertions;
using Wpf.Ui.Appearance;

/// <summary>
/// Tier-2 smoke: instantiate and realize every WPF UserControl / Page in the Dzl.Tray assembly on one STA
/// thread, so the runtime-only XAML failures that compile clean — invalid WPF-UI <c>SymbolIcon</c> names,
/// <c>ui:*</c> styles missing <c>BasedOn</c>, and StaticResource keys a UserControl can't see (UserControls
/// don't inherit host-window resources) — surface as a red test instead of a crash in the running app.
///
/// This is the in-process xunit form of the existing <c>DZL_SMOKE_WINDOW</c> harness in App.xaml.cs. It loads
/// the REAL App.xaml resource dictionaries (Themes + Controls + Colors/Converters/Styles) so app-scope keys
/// resolve exactly as they do at runtime. Reflection-driven, so every control added later is covered for free.
/// </summary>
public class WpfRealizationSmokeTests
{
    /// <summary>Create the single Application instance and load App.xaml's merged dictionaries + dark theme.
    /// Must run on the STA thread that will realize the controls (WPF objects have thread affinity).</summary>
    private static void EnsureApp()
    {
        if (Application.Current is not null) return;
        var app = new App();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.InitializeComponent();   // loads App.xaml MergedDictionaries into Application.Current.Resources
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
    }

    /// <summary>Realizable = a concrete FrameworkElement (UserControl/Page/…) in the Tray assembly with a
    /// public parameterless ctor. Windows are excluded (they need a VM + Show, not Measure/Arrange).</summary>
    private static List<Type> RealizableControls() =>
        typeof(App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                        && typeof(FrameworkElement).IsAssignableFrom(t)
                        && !typeof(Window).IsAssignableFrom(t)
                        && !typeof(Application).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.Name)
            .ToList();

    private static void RealizeMainWindow()
    {
        var previousConfig = Environment.GetEnvironmentVariable("DZL_CONFIG");
        var previousEmbedded = Environment.GetEnvironmentVariable("KM_SUITE_EMBEDDED");
        var tempRoot = Path.Combine(Path.GetTempPath(), "aph-havoc-main-window-smoke", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(tempRoot, "config.json");
        Directory.CreateDirectory(tempRoot);

        try
        {
            Environment.SetEnvironmentVariable("DZL_CONFIG", configPath);
            Environment.SetEnvironmentVariable("KM_SUITE_EMBEDDED", null);
            Profiles.EnsureDefault(configPath);

            var window = new MainWindow();
            window.Measure(new Size(1240, 800));
            window.Arrange(new Rect(0, 0, 1240, 800));
            window.UpdateLayout();
            // The per-instance manager has a VM/action constructor, so reflection cannot create it.
            // Navigate through the real host to realize its graphical server config, Workshop,
            // files, mods and launch tabs under the same resources as production.
            using (var editorVm = new MainViewModel(configPath))
            {
                var editor = new ServerEditorWindow(editorVm, 0, () => { });
                editor.Measure(new Size(1500, 900));
                editor.Arrange(new Rect(0, 0, 1500, 900));
                editor.UpdateLayout();
            }
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DZL_CONFIG", previousConfig);
            Environment.SetEnvironmentVariable("KM_SUITE_EMBEDDED", previousEmbedded);
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [WpfFact]
    public void Every_usercontrol_realizes_without_throwing()
    {
        EnsureApp();
        RealizeMainWindow();

        var controls = RealizableControls();
        controls.Should().NotBeEmpty("reflection must find the Tray UserControls/Views to smoke-test");

        var failures = new List<string>();
        foreach (var type in controls)
        {
            try
            {
                var el = (FrameworkElement)Activator.CreateInstance(type)!;
                if (el is ServersView)
                {
                    // Realize an actual server card so display-only metadata bindings are exercised.
                    // WPF defaults some Run.Text bindings to TwoWay, which must not target read-only
                    // properties such as ServerInstance.FolderName.
                    el.DataContext = new
                    {
                        Servers = new[]
                        {
                            new ServerInstance("default", @"C:\DayZProjects\servers\default",
                                @"C:\DayZProjects\servers\default\serverDZ.cfg",
                                DisplayName: "My Server", Port: 2302)
                        },
                        Maps = Array.Empty<string>(),
                        BaseChoices = Array.Empty<string>(),
                        ModPresetChoices = Array.Empty<string>(),
                        ActivePreset = "default"
                    };
                }
                // Force template application + style/resource resolution without showing a window. This is
                // the point where a bad SymbolIcon / missing StaticResource / BasedOn-less style throws.
                el.Measure(new Size(1200, 900));
                el.Arrange(new Rect(0, 0, 1200, 900));
                el.UpdateLayout();
            }
            catch (Exception ex)
            {
                var b = ex.GetBaseException();
                failures.Add($"{type.FullName}: {b.Message}\n{b.StackTrace}");
            }
        }

        failures.Should().BeEmpty(
            "every UserControl/View must instantiate + realize cleanly; failures are runtime-only XAML defects:\n"
            + string.Join("\n", failures));
    }
}
