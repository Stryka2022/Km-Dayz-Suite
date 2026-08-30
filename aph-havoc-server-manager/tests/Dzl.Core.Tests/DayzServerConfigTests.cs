using Dzl.Core.Servers;
using FluentAssertions;

public class DayzServerConfigTests
{
    [Fact]
    public void Loads_and_saves_managed_fields_without_replacing_mission_or_comments()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "serverDZ.cfg");
        File.WriteAllText(path,
            """
            hostname = "Old server"; // keep this comment
            password = "";
            maxPlayers = 40;
            serverTimeAcceleration = 2;
            motd[] = { "Welcome", "Survivor" };
            class Missions
            {
                class DayZ { template = "dayzOffline.chernarusplus"; };
            };
            """);

        var settings = DayzServerConfig.Load(path);
        settings.Hostname.Should().Be("Old server");
        settings.MaxPlayers.Should().Be(40);
        settings.Motd.Should().Be("Welcome" + Environment.NewLine + "Survivor");

        DayzServerConfig.Save(path, settings with
        {
            Hostname = "New server",
            PasswordAdmin = "secret",
            MaxPlayers = 75,
            ServerTimeAcceleration = 4,
            EnableWhitelist = true,
            AllowFilePatching = true
        });

        var text = File.ReadAllText(path);
        text.Should().Contain("hostname = \"New server\"; // keep this comment");
        text.Should().Contain("passwordAdmin = \"secret\";");
        text.Should().Contain("maxPlayers = 75;");
        text.Should().Contain("enableWhitelist = 1;");
        text.Should().Contain("allowFilePatching = 1;");
        text.Should().Contain("template = \"dayzOffline.chernarusplus\"");
        File.Exists(path + ".km-backup").Should().BeTrue();
    }

    [Fact]
    public void Missing_settings_are_inserted_before_the_missions_class()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "serverDZ.cfg");
        File.WriteAllText(path, "class Missions { class DayZ { template = \"map\"; }; };");

        DayzServerConfig.Save(path, new DayzServerSettings { Hostname = "Inserted" });

        var text = File.ReadAllText(path);
        text.IndexOf("hostname = \"Inserted\";", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("class Missions", StringComparison.Ordinal));
    }
}
