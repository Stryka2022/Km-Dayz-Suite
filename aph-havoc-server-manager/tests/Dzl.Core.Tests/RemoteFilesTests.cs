using Dzl.Core.Remote;
using FluentAssertions;

namespace Dzl.Core.Tests;

public class RemoteFilesTests
{
    [Theory]
    [InlineData("ftp.example.com", "ftp.example.com")]
    [InlineData("ftp://ftp.example.com/dayz/server", "ftp.example.com")]
    [InlineData("ftp.example.com/dayz/server", "ftp.example.com")]
    public void NormalizeHost_RemovesSchemeAndPastedPath(string input, string expected)
    {
        Assert.Equal(expected, RemoteProfileStore.NormalizeHost(input));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("\\mpmissions\\dayzOffline.chernarusplus\\", "/mpmissions/dayzOffline.chernarusplus")]
    [InlineData("/profiles/../mpmissions/./db", "/mpmissions/db")]
    [InlineData("../../serverDZ.cfg", "/serverDZ.cfg")]
    public void NormalizePath_canonicalizes_without_escaping_root(string? input, string expected) =>
        FtpRemoteClient.NormalizePath(input).Should().Be(expected);

    [Fact]
    public void BuildUri_normalizes_host_port_and_escapes_each_path_segment()
    {
        var profile = new RemoteServerProfile { Host = "ftps://example.invalid/", Port = 2121 };
        var uri = FtpRemoteClient.BuildUri(profile, "/DayZ Server/mp missions/serverDZ.cfg");

        uri.Scheme.Should().Be("ftp");
        uri.Host.Should().Be("example.invalid");
        uri.Port.Should().Be(2121);
        uri.AbsoluteUri.Should().Contain("DayZ%20Server/mp%20missions/serverDZ.cfg");
    }

    [Fact]
    public void Combine_and_parent_paths_validate_names()
    {
        FtpRemoteClient.CombinePath("/profiles", "serverDZ.cfg").Should().Be("/profiles/serverDZ.cfg");
        FtpRemoteClient.ParentPath("/profiles/logs/script.log").Should().Be("/profiles/logs");
        var action = () => FtpRemoteClient.CombinePath("/profiles", "../secret");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unix_list_line_is_parsed_as_directory()
    {
        var entry = FtpRemoteClient.ParseListLine("/server", "drwxr-xr-x 2 owner group 4096 Aug 24 20:10 profiles");

        entry.Should().NotBeNull();
        entry!.Name.Should().Be("profiles");
        entry.IsDirectory.Should().BeTrue();
        entry.FullPath.Should().Be("/server/profiles");
    }

    [Fact]
    public void Windows_list_line_is_parsed_as_file()
    {
        var entry = FtpRemoteClient.ParseListLine("/server", "08-24-26  09:15PM              12345 serverDZ.cfg");

        entry.Should().NotBeNull();
        entry!.Name.Should().Be("serverDZ.cfg");
        entry.IsDirectory.Should().BeFalse();
        entry.Size.Should().Be(12345);
    }

    [Theory]
    [InlineData("drwxr-xr-x 2 owner group 4096 Aug 24 20:10 profiles", RemoteServerOperatingSystem.LinuxOrUnix)]
    [InlineData("08-24-26  09:15PM              12345 serverDZ.cfg", RemoteServerOperatingSystem.Windows)]
    [InlineData("serverDZ.cfg", RemoteServerOperatingSystem.Unknown)]
    public void Remote_operating_system_is_detected_from_directory_listing(
        string listingLine,
        RemoteServerOperatingSystem expected)
    {
        FtpRemoteClient.DetectOperatingSystemFromListLine(listingLine).Should().Be(expected);
    }

    [Fact]
    public void Profile_store_encrypts_ftp_and_rcon_passwords_at_rest_and_preserves_them_on_blank_update()
    {
        if (!OperatingSystem.IsWindows()) return;
        var testDir = Path.Combine(Path.GetTempPath(), "dzl-remote-profile-test-" + Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(testDir, "config.json");
        Directory.CreateDirectory(testDir);
        try
        {
            var profile = new RemoteServerProfile
            {
                Name = "Production",
                InstanceName = "hardcore",
                Host = "ftp.example.invalid",
                UserName = "owner",
                RconHost = "rcon.example.invalid",
                RconPort = 2301
            };
            RemoteProfileStore.Upsert(configPath, profile, "correct horse battery staple", "another secret");

            var raw = File.ReadAllText(RemoteProfileStore.StorePath(configPath));
            raw.Should().NotContain("correct horse battery staple");
            raw.Should().NotContain("another secret");
            RemoteProfileStore.GetPassword(configPath, profile.Id).Should().Be("correct horse battery staple");
            RemoteProfileStore.GetRconPassword(configPath, profile.Id).Should().Be("another secret");

            RemoteProfileStore.Upsert(configPath, profile with { RootPath = "/dayz" }, "");
            RemoteProfileStore.GetPassword(configPath, profile.Id).Should().Be("correct horse battery staple");
            RemoteProfileStore.GetRconPassword(configPath, profile.Id).Should().Be("another secret");
            RemoteProfileStore.Load(configPath).Single().RootPath.Should().Be("/dayz");
            RemoteProfileStore.Load(configPath).Single().InstanceName.Should().Be("hardcore");
            RemoteProfileStore.Load(configPath).Single().RconEndpointLabel.Should().Be("rcon.example.invalid:2301");
        }
        finally
        {
            var full = Path.GetFullPath(testDir);
            var temp = Path.GetFullPath(Path.GetTempPath());
            if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
                Directory.Delete(full, true);
        }
    }

    [Fact]
    public void Profile_store_loads_legacy_ftp_only_json_with_safe_rcon_defaults()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "dzl-legacy-remote-profile-test-" + Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(testDir, "config.json");
        Directory.CreateDirectory(testDir);
        try
        {
            File.WriteAllText(RemoteProfileStore.StorePath(configPath), """
                [{
                  "id": "legacy",
                  "name": "Old FTP server",
                  "host": "ftp.example.invalid",
                  "port": 2121,
                  "user_name": "owner",
                  "root_path": "/dayz",
                  "use_tls": true,
                  "passive": true,
                  "protected_password": ""
                }]
                """);

            var profile = RemoteProfileStore.Load(configPath).Single();
            profile.Name.Should().Be("Old FTP server");
            profile.Host.Should().Be("ftp.example.invalid");
            profile.RconHost.Should().BeEmpty();
            profile.RconPort.Should().Be(2301);
            profile.EffectiveRconHost.Should().Be("ftp.example.invalid");
        }
        finally
        {
            var full = Path.GetFullPath(testDir);
            var temp = Path.GetFullPath(Path.GetTempPath());
            if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
                Directory.Delete(full, true);
        }
    }
}
