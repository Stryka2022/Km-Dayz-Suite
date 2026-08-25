using System;
using System.Reflection;
using System.Windows;
using Dzl.Tray;
using Dzl.Tray.Controls;
using Dzl.Tray.ViewModels;
using FluentAssertions;
using Wpf.Ui.Appearance;
using Xunit;

namespace Dzl.Tray.Tests;

/// <summary>Guards the detached-log-pane leak fix: a LogPaneControl must drop its PropertyChanged subscription
/// on the (app-lifetime) pane VM when it is unloaded — e.g. when a detached LogWindow closes — and re-subscribe
/// when reloaded (tab switch). Otherwise every detach/close cycle leaks the control + its AvalonEdit editor.</summary>
public class LogPaneControlTests
{
    // Count of live subscribers on a CommunityToolkit ObservableObject's PropertyChanged event.
    private static int HandlerCount(object vm)
    {
        for (var t = vm.GetType(); t is not null; t = t.BaseType)
        {
            var f = t.GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f is null) continue;
            return f.GetValue(vm) is Delegate d ? d.GetInvocationList().Length : 0;
        }
        return 0;
    }

    [WpfFact]
    public void Unloads_drop_the_vm_subscription_and_reloads_restore_it()
    {
        // This control uses only DynamicResource references and does not need to own the process-wide WPF
        // Application.  Leaving Application creation to WpfRealizationSmokeTests avoids the WPF rule that
        // permits only one Application instance per test AppDomain.
        var vm = new LogPaneVm("rpt", "RPT");
        var ctl = new LogPaneControl { DataContext = vm };

        HandlerCount(vm).Should().Be(1, "setting the DataContext subscribes the control to the VM");

        ctl.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        HandlerCount(vm).Should().Be(0, "unloading (e.g. a detached window closing) must unsubscribe so it can be GC'd");

        ctl.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        HandlerCount(vm).Should().Be(1, "reloading (e.g. switching back to the tab) must re-subscribe");

        ctl.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        HandlerCount(vm).Should().Be(0, "and unloading again drops it — no accumulation across cycles");
    }
}
