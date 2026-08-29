using Dzl.Core.Config;
using Dzl.Core.Workshop;
using FluentAssertions;

namespace Dzl.Core.Tests;

public class DedicatedServerInstallerTests
{
    private sealed class CaptureProgress(List<string> messages) : IProgress<string>
    {
        public void Report(string value) => messages.Add(value);
    }

    [Fact]
    public void ResolveInstanceInstallPath_appends_the_safe_server_name_to_a_parent_folder()
    {
        var parent = Path.Combine(Path.GetTempPath(), "Dayz Servers");

        DedicatedServerInstaller.ResolveInstanceInstallPath(parent, "APH Havoc PVE #1")
            .Should().Be(Path.Combine(Path.GetFullPath(parent), "APH_Havoc_PVE_1"));
    }

    [Fact]
    public void ResolveInstanceInstallPath_does_not_append_the_same_server_name_twice()
    {
        var resolved = Path.Combine(Path.GetTempPath(), "Dayz Servers", "APH_Havoc_PVE_1");

        DedicatedServerInstaller.ResolveInstanceInstallPath(resolved, "APH_Havoc_PVE_1")
            .Should().Be(Path.GetFullPath(resolved));
    }

    [Fact]
    public void BuildArguments_targets_dayz_server_with_the_selected_install_path_and_account()
    {
        var install = Path.Combine(Path.GetTempPath(), "Dayz Servers", "APH_Havoc_PVE_1");

        DedicatedServerInstaller.BuildArguments(install, "shaun-account").Should().Equal(
            "+force_install_dir", install,
            "+login", "shaun-account",
            "+app_update", "223350", "validate",
            "+quit");
    }

    [Fact]
    public void Install_is_successful_only_when_the_dayz_server_executable_exists()
    {
        var install = Directory.CreateTempSubdirectory().FullName;
        DedicatedServerInstaller.IsInstalled(install).Should().BeFalse();

        File.WriteAllText(Path.Combine(install, DedicatedServerInstaller.ServerExecutable), "test");

        DedicatedServerInstaller.IsInstalled(install).Should().BeTrue();
    }

    [Fact]
    public void FindReusableInstall_prefers_the_configured_local_server_install()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "Steam DayZServer")).FullName;
            var destination = Path.Combine(root.FullName, "instances", "PVE_1");
            File.WriteAllText(Path.Combine(source, DedicatedServerInstaller.ServerExecutable), "server");

            DedicatedServerInstaller.FindReusableInstall(
                    DzlConfig.Default() with { DayzServerPath = source }, destination)
                .Should().Be(Path.GetFullPath(source));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void FindReusableInstall_reuses_the_active_instances_complete_runtime_first()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var instance = Directory.CreateDirectory(Path.Combine(root.FullName, "existing-instance")).FullName;
            var global = Directory.CreateDirectory(Path.Combine(root.FullName, "global-server")).FullName;
            File.WriteAllText(Path.Combine(instance, DedicatedServerInstaller.ServerExecutable), "instance");
            File.WriteAllText(Path.Combine(global, DedicatedServerInstaller.ServerExecutable), "global");

            DedicatedServerInstaller.FindReusableInstall(
                    DzlConfig.Default() with
                    {
                        ServerInstallPathOverride = instance,
                        DayzServerPath = global,
                    },
                    Path.Combine(root.FullName, "new-instance"))
                .Should().Be(Path.GetFullPath(instance));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public async Task CopyExistingInstall_copies_and_verifies_a_local_server_with_progress()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "source")).FullName;
            var destination = Path.Combine(root.FullName, "destination");
            var addons = Directory.CreateDirectory(Path.Combine(source, "addons")).FullName;
            File.WriteAllText(Path.Combine(source, DedicatedServerInstaller.ServerExecutable), "server-exe");
            File.WriteAllText(Path.Combine(addons, "data.pbo"), "pbo-data");
            File.WriteAllText(Path.Combine(source, "serverDZ.cfg"), "source-config");
            Directory.CreateDirectory(Path.Combine(source, "mpmissions", "dayzOffline.chernarusplus"));
            File.WriteAllText(Path.Combine(source, "mpmissions", "dayzOffline.chernarusplus", "init.c"),
                "source-mission");
            Directory.CreateDirectory(Path.Combine(destination, "mpmissions", "dayzOffline.chernarusplus"));
            File.WriteAllText(Path.Combine(destination, "serverDZ.cfg"), "instance-config");
            File.WriteAllText(Path.Combine(destination, "mpmissions", "dayzOffline.chernarusplus", "init.c"),
                "instance-mission");
            var messages = new List<string>();

            var result = await DedicatedServerInstaller.CopyExistingInstallAsync(
                source, destination, new CaptureProgress(messages));

            result.ok.Should().BeTrue(result.message);
            File.ReadAllText(Path.Combine(destination, DedicatedServerInstaller.ServerExecutable))
                .Should().Be("server-exe");
            File.ReadAllText(Path.Combine(destination, "addons", "data.pbo")).Should().Be("pbo-data");
            File.ReadAllText(Path.Combine(destination, "serverDZ.cfg")).Should().Be("instance-config");
            File.ReadAllText(Path.Combine(destination, "mpmissions", "dayzOffline.chernarusplus", "init.c"))
                .Should().Be("instance-mission");
            messages.Should().Contain(message => message.Contains("100%"));
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("serverDZ.cfg", true)]
    [InlineData("mpmissions\\dayzOffline.chernarusplus\\init.c", true)]
    [InlineData("profiles\\server.RPT", true)]
    [InlineData("profiles_client\\client.RPT", true)]
    [InlineData(".dzl\\metadata.json", true)]
    [InlineData("DayZServer_x64.exe", false)]
    [InlineData("addons\\data.pbo", false)]
    public void Instance_owned_content_is_preserved_during_runtime_copy(string path, bool expected)
        => DedicatedServerInstaller.IsInstanceOwnedPath(path).Should().Be(expected);
}
