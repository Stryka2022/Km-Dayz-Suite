using Dzl.Core.App;
using Dzl.Core.Config;
using FluentAssertions;

public class WorkshopInstanceServiceTests
{
    [Theory]
    [InlineData(@"C:\Steam\steamapps\workshop\content\221100\123456789", "123456789")]
    [InlineData(@"D:\workshop\987654321\addons", "987654321")]
    [InlineData(@"C:\DayZProjects\servers\PVE_1\@Workshop_2545327648", "2545327648")]
    public void Extracts_workshop_id_from_content_path(string path, string id) =>
        WorkshopInstanceService.TryWorkshopId(path).Should().Be(id);

    [Fact]
    public void Enables_mod_and_copies_only_public_keys_for_selected_instance()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(root, "config.json");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = Path.Combine(root, "projects") }, configPath);
        Profiles.EnsureDefault(configPath);
        var mod = Path.Combine(root, "workshop", "123456789");
        Directory.CreateDirectory(Path.Combine(mod, "keys"));
        Directory.CreateDirectory(Path.Combine(mod, "addons"));
        File.WriteAllText(Path.Combine(mod, "addons", "demo.pbo"), "pbo");
        File.WriteAllText(Path.Combine(mod, "keys", "demo.bikey"), "public");
        File.WriteAllText(Path.Combine(mod, "keys", "demo.biprivatekey"), "private");

        var result = WorkshopInstanceService.EnableForInstance(configPath, "default", "123456789", mod);

        result.Ok.Should().BeTrue();
        var deployed = WorkshopInstanceService.DeploymentDir(configPath, "default", "123456789");
        Profiles.Load("default", configPath).Mods.Should().ContainSingle(m => m.Enabled && m.Path == deployed);
        File.ReadAllText(Path.Combine(deployed, "addons", "demo.pbo")).Should().Be("pbo");
        File.Exists(Path.Combine(Profiles.InstanceDir("default", configPath), "keys", "demo.bikey")).Should().BeTrue();
        File.Exists(Path.Combine(Profiles.InstanceDir("default", configPath), "keys", "demo.biprivatekey")).Should().BeFalse();
    }

    [Fact]
    public void Disables_mod_for_selected_instance_without_deleting_shared_download()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(root, "config.json");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = Path.Combine(root, "projects") }, configPath);
        Profiles.EnsureDefault(configPath);
        var mod = Path.Combine(root, "workshop", "123456789");
        Directory.CreateDirectory(mod);
        WorkshopInstanceService.EnableForInstance(configPath, "default", "123456789", mod, copyKeys: false)
            .Ok.Should().BeTrue();

        var result = WorkshopInstanceService.DisableForInstance(configPath, "default", "123456789");

        result.Ok.Should().BeTrue();
        Profiles.Load("default", configPath).Mods.Should().NotContain(m =>
            WorkshopInstanceService.TryWorkshopId(m.Path) == "123456789");
        Directory.Exists(mod).Should().BeTrue();
        Directory.Exists(WorkshopInstanceService.DeploymentDir(configPath, "default", "123456789")).Should().BeFalse();
    }

    [Fact]
    public void Updating_replaces_the_selected_instances_copy_without_touching_other_instances()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(root, "config.json");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = Path.Combine(root, "projects") }, configPath);
        Profiles.EnsureDefault(configPath);
        Profiles.Save(Profiles.Load("default", configPath) with { DisplayName = "Second" }, "second", configPath);
        var mod = Path.Combine(root, "workshop", "123456789");
        Directory.CreateDirectory(Path.Combine(mod, "addons"));
        File.WriteAllText(Path.Combine(mod, "addons", "demo.pbo"), "v1");

        WorkshopInstanceService.EnableForInstance(configPath, "default", "123456789", mod, copyKeys: false).Ok.Should().BeTrue();
        WorkshopInstanceService.EnableForInstance(configPath, "second", "123456789", mod, copyKeys: false).Ok.Should().BeTrue();
        File.WriteAllText(Path.Combine(mod, "addons", "demo.pbo"), "v2");
        File.WriteAllText(Path.Combine(mod, "addons", "new.pbo"), "new");

        WorkshopInstanceService.EnableForInstance(configPath, "default", "123456789", mod, copyKeys: false).Ok.Should().BeTrue();

        var first = WorkshopInstanceService.DeploymentDir(configPath, "default", "123456789");
        var second = WorkshopInstanceService.DeploymentDir(configPath, "second", "123456789");
        File.ReadAllText(Path.Combine(first, "addons", "demo.pbo")).Should().Be("v2");
        File.Exists(Path.Combine(first, "addons", "new.pbo")).Should().BeTrue();
        File.ReadAllText(Path.Combine(second, "addons", "demo.pbo")).Should().Be("v1");
        File.Exists(Path.Combine(second, "addons", "new.pbo")).Should().BeFalse();
    }

    [Fact]
    public void Disabling_an_item_that_is_not_enabled_is_idempotent()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(root, "config.json");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = Path.Combine(root, "projects") }, configPath);
        Profiles.EnsureDefault(configPath);

        var result = WorkshopInstanceService.DisableForInstance(configPath, "default", "123456789");

        result.Ok.Should().BeTrue();
        result.Message.Should().Contain("already uninstalled");
    }
}
