using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Mods;
using FluentAssertions;

public class LauncherServiceModPresetTests
{
    // Temp config + seeded active "default" instance with a known mod list. DayzPath points
    // at a nonexistent dir on purpose: the default would hit the real install and ServerService
    // .Create (Task 5's test) would copy a real multi-hundred-MB mission into the temp dir.
    private static string Seed(out LauncherService svc)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "config.json");
        GlobalStore.Save(new GlobalConfig
        {
            ProjectsRoot = Path.Combine(dir, "projects"),
            DayzPath = Path.Combine(dir, "no-dayz"),
        }, path);
        Profiles.EnsureDefault(path);
        var (cfg, _, active) = Profiles.ResolveActive(path);
        Profiles.Save(cfg with
        {
            Mods = new()
            {
                new ModEntry { Path = @"P:\@CF", Enabled = true, Side = "both" },
                new ModEntry { Path = @"P:\@VPP", Enabled = true, Side = "server" },
                new ModEntry { Path = @"P:\@Extra", Enabled = false, Side = "both" },
            },
        }, string.IsNullOrEmpty(active) ? "default" : active, path);
        svc = new LauncherService(path);
        return path;
    }

    [Fact]
    public void SaveModPreset_snapshots_enabled_mods_and_marks_instance()
    {
        var path = Seed(out var svc);

        var r = svc.SaveModPreset("base-loadout");

        r.Ok.Should().BeTrue();
        var p = ModPresetStore.Load("base-loadout", path);
        p!.Mods.Select(m => m.Path).Should().ContainInOrder(@"P:\@CF", @"P:\@VPP");
        Profiles.ResolveActive(path).cfg.ModPreset.Should().Be("base-loadout");
        svc.ModPresets().Should().ContainSingle(v => v.Name == "base-loadout" && v.ModCount == 2 && v.Active);
    }

    [Fact]
    public void ApplyModPreset_rewrites_loadout_and_records_name()
    {
        var path = Seed(out var svc);
        ModPresetStore.Save(new ModPreset
        {
            Name = "extra-only",
            Mods = new() { new ModPresetEntry { Path = @"P:\@Extra", Side = "both" } },
        }, path);

        var r = svc.ApplyModPreset("extra-only");

        r.Ok.Should().BeTrue();
        var cfg = Profiles.ResolveActive(path).cfg;
        cfg.ModPreset.Should().Be("extra-only");
        cfg.Mods[0].Path.Should().Be(@"P:\@Extra");
        cfg.Mods[0].Enabled.Should().BeTrue();
        cfg.Mods.Where(m => m.Path != @"P:\@Extra").Should().OnlyContain(m => !m.Enabled);
    }

    [Fact]
    public void ApplyModPreset_unknown_name_fails()
    {
        Seed(out var svc);
        svc.ApplyModPreset("nope").Ok.Should().BeFalse();
    }

    [Fact]
    public void SaveModPreset_invalid_name_fails()
    {
        Seed(out var svc);
        svc.SaveModPreset("a<b").Ok.Should().BeFalse();
    }

    [Fact]
    public void DeleteModPreset_removes_file_keeps_dangling_instance_pointer()
    {
        var path = Seed(out var svc);
        svc.SaveModPreset("gone");

        svc.DeleteModPreset("gone").Ok.Should().BeTrue();

        svc.ModPresets().Should().BeEmpty();
        Profiles.ResolveActive(path).cfg.ModPreset.Should().Be("gone");   // dangling by design
        svc.DeleteModPreset("gone").Ok.Should().BeFalse();
    }

    [Fact]
    public void Ipc_table_dispatches_mod_preset_methods()
    {
        Seed(out var svc);
        Dzl.Core.Ipc.IpcMethods.Table[Dzl.Core.Ipc.IpcMethods.SaveModPreset](
            svc, new Dzl.Core.Ipc.IpcRequest(Dzl.Core.Ipc.IpcMethods.SaveModPreset,
                new() { ["name"] = "via-ipc" }));

        var list = (IReadOnlyList<ModPresetView>)Dzl.Core.Ipc.IpcMethods.Table[Dzl.Core.Ipc.IpcMethods.ModPresets](
            svc, new Dzl.Core.Ipc.IpcRequest(Dzl.Core.Ipc.IpcMethods.ModPresets, null));

        list.Should().ContainSingle(p => p.Name == "via-ipc" && p.ModCount == 2);
    }

    [Fact]
    public void Create_server_with_mod_preset_applies_loadout_to_the_new_instance()
    {
        var path = Seed(out var svc);
        svc.SaveModPreset("starter");

        // Seed's DayzPath doesn't exist, so ServerScaffold just notes "mission source not
        // found" (it never throws); the instance config must still land with the loadout applied.
        var r = new ServerService(path).Create("pvp", "chernarus", port: 2402,
            activate: false, baseName: null, modPreset: "starter");

        r.Ok.Should().BeTrue();
        var cfg = Profiles.Load("pvp", path);
        cfg.ModPreset.Should().Be("starter");
        cfg.Mods.Where(m => m.Enabled).Select(m => m.Path)
           .Should().ContainInOrder(@"P:\@CF", @"P:\@VPP");
    }

    [Fact]
    public void Create_server_with_unknown_mod_preset_reports_it_in_the_message()
    {
        var path = Seed(out _);

        var r = new ServerService(path).Create("pvp2", "chernarus", port: 2403,
            activate: false, baseName: null, modPreset: "no-such-preset");

        r.Ok.Should().BeTrue();
        r.Message.Should().Contain("no-such-preset").And.Contain("not applied");
        Profiles.Load("pvp2", path).ModPreset.Should().Be("");
    }
}
