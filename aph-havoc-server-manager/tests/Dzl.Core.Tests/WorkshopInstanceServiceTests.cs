using Dzl.Core.App;
using Dzl.Core.Config;
using FluentAssertions;

public class WorkshopInstanceServiceTests
{
    [Theory]
    [InlineData(@"C:\Steam\steamapps\workshop\content\221100\123456789", "123456789")]
    [InlineData(@"D:\workshop\987654321\addons", "987654321")]
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
        File.WriteAllText(Path.Combine(mod, "keys", "demo.bikey"), "public");
        File.WriteAllText(Path.Combine(mod, "keys", "demo.biprivatekey"), "private");

        var result = WorkshopInstanceService.EnableForInstance(configPath, "default", "123456789", mod);

        result.Ok.Should().BeTrue();
        Profiles.Load("default", configPath).Mods.Should().ContainSingle(m => m.Enabled && m.Path == Path.GetFullPath(mod));
        File.Exists(Path.Combine(Profiles.InstanceDir("default", configPath), "keys", "demo.bikey")).Should().BeTrue();
        File.Exists(Path.Combine(Profiles.InstanceDir("default", configPath), "keys", "demo.biprivatekey")).Should().BeFalse();
    }
}
