using Dzl.Core.Workshop;
using FluentAssertions;

namespace Dzl.Core.Tests;

public class DedicatedServerInstallerTests
{
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
}
