using Dzl.Core.Config;
using Dzl.Core.Projects;
using FluentAssertions;

public class DevToolsAssetsTests
{
    // A fake bundle (source/ + build/DzlDevTools.pbo) + a temp projects root.
    private static (string bundle, DzlConfig cfg, string root) Fixture()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var bundle = Path.Combine(dir, "bundle");
        Directory.CreateDirectory(Path.Combine(bundle, "source", "scripts"));
        File.WriteAllText(Path.Combine(bundle, "source", "config.cpp"), "class CfgPatches {};");
        File.WriteAllText(Path.Combine(bundle, "source", "scripts", "x.c"), "// v1");
        Directory.CreateDirectory(Path.Combine(bundle, "build"));
        File.WriteAllText(Path.Combine(bundle, "build", "DzlDevTools.pbo"), "PBOv1");

        var root = Path.Combine(dir, "projects");
        var cfg = DzlConfig.Default() with { ProjectsRoot = root };
        return (bundle, cfg, root);
    }

    [Fact]
    public void Deploy_installs_source_and_pbo()
    {
        var (bundle, cfg, root) = Fixture();

        var r = DevToolsAssets.Deploy(cfg, bundle);

        r.Ok.Should().BeTrue();
        File.ReadAllText(Path.Combine(root, "mods", "DzlDevTools", "config.cpp")).Should().Contain("CfgPatches");
        File.ReadAllText(Path.Combine(root, "build", "@DzlDevTools", "Addons", "DzlDevTools.pbo")).Should().Be("PBOv1");
    }

    [Fact]
    public void Deploy_keeps_user_source_edits_but_refreshes_the_pbo()
    {
        var (bundle, cfg, root) = Fixture();
        DevToolsAssets.Deploy(cfg, bundle);

        // User edits a source file; a newer bundle ships a new PBO.
        var userFile = Path.Combine(root, "mods", "DzlDevTools", "scripts", "x.c");
        File.WriteAllText(userFile, "// my edit");
        File.WriteAllText(Path.Combine(bundle, "build", "DzlDevTools.pbo"), "PBOv2");

        var r = DevToolsAssets.Deploy(cfg, bundle);

        r.Ok.Should().BeTrue();
        File.ReadAllText(userFile).Should().Be("// my edit");   // source untouched
        File.ReadAllText(Path.Combine(root, "build", "@DzlDevTools", "Addons", "DzlDevTools.pbo"))
            .Should().Be("PBOv2");                              // PBO refreshed
    }

    [Fact]
    public void Deploy_reports_a_missing_bundle()
        => DevToolsAssets.Deploy(DzlConfig.Default(), Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid()))
            .Ok.Should().BeFalse();
}
