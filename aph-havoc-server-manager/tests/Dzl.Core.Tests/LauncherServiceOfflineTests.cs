using Dzl.Core.App;
using Dzl.Core.Config;
using FluentAssertions;

public class LauncherServiceOfflineTests
{
    // Temp config with a fake DayzPath (never touch the real install) and a seeded default.
    private static string Seed()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "config.json");
        GlobalStore.Save(new GlobalConfig
        {
            ProjectsRoot = Path.Combine(dir, "projects"),
            DayzPath = Path.Combine(dir, "no-dayz"),
            AutoLaunchTray = false,
        }, path);
        Profiles.EnsureDefault(path);
        return path;
    }

    [Fact]
    public void Create_with_offline_flags_the_instance()
    {
        var path = Seed();

        var r = new ServerService(path).Create("sandbox", "chernarus", port: 2500,
            activate: true, offline: true);

        r.Ok.Should().BeTrue();
        Profiles.ResolveActive(path).cfg.OfflineMode.Should().BeTrue();
    }

    [Fact]
    public void Server_start_and_restart_fail_on_an_offline_instance()
    {
        var path = Seed();
        new ServerService(path).Create("sandbox", "chernarus", port: 2500,
            activate: true, offline: true);
        var svc = new LauncherService(path);

        svc.StartTarget("server", "debug").Ok.Should().BeFalse();
        svc.StartTarget("server", "debug").Message.Should().Contain("offline");
        svc.Start("debug", client: false).Ok.Should().BeFalse();
        svc.RestartTarget("server", "debug").Ok.Should().BeFalse();
        svc.Restart("debug").Ok.Should().BeFalse();
    }

    [Fact]
    public void Server_ops_still_work_on_a_normal_instance_guard_is_scoped()
    {
        var path = Seed();
        new ServerService(path).Create("normal1", "chernarus", port: 2501, activate: true);
        var svc = new LauncherService(path);

        // Spawn fails on the fake DayzPath, but it must fail on the SPAWN, not the guard.
        svc.StartTarget("server", "debug").Message.Should().NotContain("offline");
    }
}
