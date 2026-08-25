using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Workshop;
using FluentAssertions;

public class WorkshopServiceTests
{
    [Fact]
    public void Steam_login_is_read_from_global_config()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(dir, "config.json");
        GlobalStore.Save(new GlobalConfig
        {
            ProjectsRoot = Path.Combine(dir, "projects"),
            SteamLogin = "TestPlayer",
        }, configPath);
        Profiles.EnsureDefault(configPath);

        Profiles.ResolveActive(configPath).cfg.SteamLogin.Should().Be("TestPlayer");
        File.ReadAllText(configPath).Should().Contain("\"steam_login\": \"TestPlayer\"");
        File.ReadAllText(Profiles.PresetFile("default", configPath)).Should().NotContain("steam_login");
    }

    [Fact]
    public void Download_when_signed_in_but_no_steam_login_mentions_username_field()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(dir, "config.json");
        var steamcmd = Path.Combine(dir, "steamcmd.exe");
        File.WriteAllBytes(steamcmd, []);
        GlobalStore.Save(new GlobalConfig
        {
            ProjectsRoot = Path.Combine(dir, "projects"),
            SteamCmdPath = steamcmd,
            SteamLogin = "",
        }, configPath);
        Profiles.EnsureDefault(configPath);
        SteamTokenStore.Save(configPath, "fake-refresh");

        var msg = new WorkshopService(configPath).Download("123").Message;
        msg.Should().Contain("Steam username");
    }
}
