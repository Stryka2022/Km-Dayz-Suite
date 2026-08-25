using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Remote;
using FluentAssertions;

public class NotificationAutomationTests
{
    [Theory]
    [InlineData("Players on server:\n(0 players in total)", 0)]
    [InlineData("Players on server:\r\n1 127.0.0.1:2304 42 abc Name\r\n(1 player in total)", 1)]
    [InlineData("header\n( 27 players in total )\n", 27)]
    public void BattlEye_player_total_is_parsed(string response, int expected) =>
        BattlEyePlayerParser.ParseCount(response).Should().Be(expected);

    [Fact]
    public void BattlEye_player_total_returns_null_for_unrecognised_output() =>
        BattlEyePlayerParser.ParseCount("Players on server: unavailable").Should().BeNull();

    [Fact]
    public void Named_webhooks_are_encrypted_enabled_and_independently_removable()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "dzl-webhook-targets-" + Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(root);
        try
        {
            GlobalStore.Save(new GlobalConfig { ProjectsRoot = Path.Combine(root, "projects") }, configPath);
            Profiles.EnsureDefault(configPath);
            var updates = new DiscordWebhookTarget { Id = "updates", Name = "Mod updates", Enabled = true };
            var admins = new DiscordWebhookTarget
            {
                Id = "admins", Name = "Admin audit", Enabled = false, WorkshopUpdates = false
            };
            DiscordWebhookStore.Upsert(configPath, "default", updates,
                "https://discord.com/api/webhooks/123/secret-one");
            DiscordWebhookStore.Upsert(configPath, "default", admins,
                "https://discord.com/api/webhooks/456/secret-two");

            var raw = File.ReadAllText(DiscordWebhookStore.StorePath(configPath, "default"));
            raw.Should().NotContain("secret-one").And.NotContain("secret-two");
            DiscordWebhookStore.LoadTargets(configPath, "default").Should().HaveCount(2);
            DiscordWebhookStore.ResolveEnabled(configPath, "default").Should().ContainSingle()
                .Which.Name.Should().Be("Mod updates");

            // Blank URL retains the encrypted secret while allowing name/enabled edits.
            DiscordWebhookStore.Upsert(configPath, "default", admins with { Enabled = true }, "");
            DiscordWebhookStore.ResolveEnabled(configPath, "default").Should().HaveCount(2);
            DiscordWebhookStore.ResolveEnabled(configPath, "default", DiscordNotificationCategory.WorkshopUpdates)
                .Should().ContainSingle().Which.Name.Should().Be("Mod updates");
            DiscordWebhookStore.ResolveEnabled(configPath, "default", DiscordNotificationCategory.AdminActivity)
                .Should().HaveCount(2);
            DiscordWebhookStore.Delete(configPath, "default", "updates");
            DiscordWebhookStore.LoadTargets(configPath, "default").Should().ContainSingle()
                .Which.Name.Should().Be("Admin audit");
        }
        finally
        {
            var full = Path.GetFullPath(root);
            var temp = Path.GetFullPath(Path.GetTempPath());
            if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
                Directory.Delete(full, true);
        }
    }
}
