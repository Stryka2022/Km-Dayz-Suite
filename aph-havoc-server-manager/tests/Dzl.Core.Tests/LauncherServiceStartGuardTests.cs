using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Launch;
using FluentAssertions;

public class LauncherServiceStartGuardTests
{
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

    // Record the TEST RUNNER's own pid + image so StateFile.ReadLive sees a genuinely live entry.
    private static void MarkLive(string configPath, string target)
    {
        var pid = Environment.ProcessId;
        var image = ProcessManager.ImageOf(pid);
        image.Should().NotBeNull("the test host must be visible to tasklist");
        StateFile.Write(configPath, target, pid, "debug", "test", image!);
    }

    [Fact]
    public void Second_start_of_a_live_target_is_refused()
    {
        var path = Seed();
        MarkLive(path, "server");
        var svc = new LauncherService(path);

        var r = svc.StartTarget("server", "debug");

        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("already up").And.Contain(Environment.ProcessId.ToString());
        svc.Start("debug", client: false).Ok.Should().BeFalse();
    }

    [Fact]
    public void Live_client_blocks_client_start_but_not_the_server()
    {
        var path = Seed();
        MarkLive(path, "client");
        var svc = new LauncherService(path);

        svc.StartTarget("client", "debug").Ok.Should().BeFalse();
        // Server start proceeds past the guard and fails only on the fake DayzPath spawn.
        svc.StartTarget("server", "debug").Message.Should().NotContain("already up");
    }

    [Fact]
    public void Dead_recorded_pid_does_not_block_a_start()
    {
        var path = Seed();
        // A pid that exists but whose image can't match the recorded one -> pruned as dead.
        StateFile.Write(path, "server", Environment.ProcessId, "debug", "test", "DefinitelyNotThisImage.exe");
        var svc = new LauncherService(path);

        svc.StartTarget("server", "debug").Message.Should().NotContain("already up");
    }

    [Fact]
    public void Live_server_for_another_instance_does_not_block_the_active_server()
    {
        var path = Seed();
        var (cfg, _, _) = Profiles.ResolveActive(path);
        Profiles.Save(cfg, "alpha", path);
        Profiles.Save(cfg, "bravo", path);
        Profiles.SetActive("bravo", path);
        MarkLive(path, ProcessManager.ServerStateKey("alpha"));
        var svc = new LauncherService(path);

        svc.StartTarget("server", "debug").Message.Should().NotContain("already up");

        MarkLive(path, ProcessManager.ServerStateKey("bravo"));
        svc.StartTarget("server", "debug").Message.Should().Contain("already up");
    }
}
