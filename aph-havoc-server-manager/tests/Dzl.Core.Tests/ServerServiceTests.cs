using Dzl.Core.App;
using Dzl.Core.Config;
using FluentAssertions;

namespace Dzl.Core.Tests;

public class ServerServiceTests
{
    [Fact]
    public void Creates_five_distinct_server_presets()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        var root = Path.Combine(tmp, "projects");

        Profiles.EnsureDefault(configPath);

        // point projects_root via base config; DayzPath=tmp means no real mission to copy — fine
        var (baseCfg, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(baseCfg with { ProjectsRoot = root, DayzPath = tmp }, configPath);

        var svc = new ServerService(configPath);
        foreach (var n in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
            svc.Create(n, "chernarus").Ok.Should().BeTrue();

        var presets = Profiles.List(configPath);
        foreach (var n in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
            presets.Should().Contain(n);

        var ports = new[] { "alpha", "bravo", "charlie", "delta", "echo" }
            .Select(n => Profiles.Load(n, configPath).Port)
            .ToList();
        ports.Should().OnlyHaveUniqueItems();

        foreach (var n in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
            File.Exists(Path.Combine(root, "servers", n, "serverDZ.cfg")).Should().BeTrue();
    }

    [Fact]
    public void Create_points_serverDZcfg_template_at_the_instances_own_mission()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        var root = Path.Combine(tmp, "projects");
        var install = Path.Combine(tmp, "DayZ");
        // A real install mission to copy into the new instance.
        Directory.CreateDirectory(Path.Combine(install, "mpmissions", "dayzOffline.chernarusplus"));

        Profiles.EnsureDefault(configPath);
        var (baseCfg, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(baseCfg with { ProjectsRoot = root, DayzPath = install }, configPath);

        new ServerService(configPath).Create("alpha", "chernarus").Ok.Should().BeTrue();

        var instanceMission = Path.Combine(root, "servers", "alpha", "mpmissions", "dayzOffline.chernarusplus");
        File.ReadAllText(Path.Combine(root, "servers", "alpha", "serverDZ.cfg"))
            .Should().Contain($"template = \"{instanceMission}\"");
    }

    [Fact]
    public void Create_repoints_Mission_at_the_new_instance_not_the_active_presets()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        var root = Path.Combine(tmp, "projects");
        var install = Path.Combine(tmp, "DayZ");
        Directory.CreateDirectory(Path.Combine(install, "mpmissions", "dayzOffline.chernarusplus"));

        Profiles.EnsureDefault(configPath);
        var (g, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(g with { ProjectsRoot = root, DayzPath = install }, configPath);

        // The active preset carries an ABSOLUTE Mission pointing at a DIFFERENT instance (as the editor's
        // "Browse" produces). A new instance must not inherit it.
        var (active, _, _) = Profiles.ResolveActive(configPath);
        var foreign = @"D:\DayzProjects\servers\test6\mpmissions\dayzOffline.chernarusplus";
        Profiles.Save(active with { Mission = foreign }, "default", configPath);

        new ServerService(configPath).Create("alpha", "chernarus").Ok.Should().BeTrue();

        var mission = Profiles.Load("alpha", configPath).Mission;
        mission.Should().NotContain("test6");
        mission.Should().Contain(Path.Combine("servers", "alpha", "mpmissions"));
    }

    [Fact]
    public void Create_keeps_a_friendly_name_but_uses_a_safe_unique_folder_and_install_path()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        var root = Path.Combine(tmp, "projects");
        Profiles.EnsureDefault(configPath);
        var (baseCfg, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(baseCfg with { ProjectsRoot = root, DayzPath = tmp }, configPath);

        var install = Path.Combine(tmp, "dedicated server");
        var result = new ServerService(configPath).Create(
            "KM PvE #1", "chernarus", displayName: "KM PvE #1",
            instanceFolderName: "KM PvE #1", serverInstallPathOverride: install,
            connectIp: "192.168.50.10");

        result.Ok.Should().BeTrue();
        result.Name.Should().Be("KM_PvE_1");
        var cfg = Profiles.Load("KM_PvE_1", configPath);
        cfg.DisplayName.Should().Be("KM PvE #1");
        cfg.InstanceFolderName.Should().Be("KM_PvE_1");
        cfg.ServerInstallPathOverride.Should().Be(install);
        cfg.ConnectIp.Should().Be("192.168.50.10");
        cfg.Mode.Should().Be("normal");
        Directory.Exists(Path.Combine(root, "servers", "KM_PvE_1")).Should().BeTrue();
        File.ReadAllText(Path.Combine(root, "servers", "KM_PvE_1", "serverDZ.cfg"))
            .Should().Contain("hostname = \"KM PvE #1\";");

        new ServerService(configPath).Create(
            "KM PvE #1", "chernarus", displayName: "Another label", instanceFolderName: "KM PvE #1")
            .Ok.Should().BeFalse();
    }

    [Fact]
    public void Create_rejects_duplicate_or_out_of_range_ports()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        Profiles.EnsureDefault(configPath);
        var (baseCfg, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(baseCfg with { ProjectsRoot = Path.Combine(tmp, "projects"), DayzPath = tmp }, configPath);

        var service = new ServerService(configPath);
        service.Create("alpha", "chernarus", 2502).Ok.Should().BeTrue();
        service.Create("bravo", "chernarus", 2502).Ok.Should().BeFalse();
        service.Create("charlie", "chernarus", 80).Ok.Should().BeFalse();
    }
}
