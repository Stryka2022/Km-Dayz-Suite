using System.Net.Http.Json;
using Dzl.Core.Config;

namespace Dzl.Core.App;

public enum DiscordNotificationCategory
{
    WorkshopUpdates,
    ServerLifecycle,
    AdminActivity,
    LogAlerts,
    RemoteActivity
}

public static class DiscordWebhookService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static bool IsEnabled(DzlConfig cfg, DiscordNotificationCategory category) =>
        cfg.DiscordNotificationsEnabled && category switch
        {
            DiscordNotificationCategory.WorkshopUpdates => cfg.NotifyWorkshopUpdates,
            DiscordNotificationCategory.ServerLifecycle => cfg.NotifyServerLifecycle,
            DiscordNotificationCategory.AdminActivity => cfg.NotifyAdminActivity,
            DiscordNotificationCategory.LogAlerts => cfg.NotifyLogAlerts,
            DiscordNotificationCategory.RemoteActivity => cfg.NotifyRemoteActivity,
            _ => false
        };

    public static async Task<OpResult> SendAsync(
        string configPath, string instanceName, DzlConfig cfg, DiscordNotificationCategory category,
        string title, string message, string? webhookOverride = null)
    {
        if (!IsEnabled(cfg, category)) return new(false, "this notification category is disabled");
        var targets = string.IsNullOrWhiteSpace(webhookOverride)
            ? DiscordWebhookStore.ResolveEnabled(configPath, instanceName, category)
            : new[] { new ResolvedDiscordWebhookTarget("test", "Test", webhookOverride.Trim()) };
        if (targets.Count == 0) return new(false, "no enabled Discord webhook is saved for this instance");

        var sent = 0;
        var errors = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                var safeTitle = Limit(title.Replace("@", "＠"), 160);
                var safeMessage = Limit(message.Replace("@", "＠"), 1600);
                using var response = await Client.PostAsJsonAsync(target.Url, new
                {
                    username = "APH Havoc Server Manager",
                    allowed_mentions = new { parse = Array.Empty<string>() },
                    embeds = new[] { new { title = safeTitle, description = safeMessage, color = 65280 } }
                });
                if (response.IsSuccessStatusCode) sent++;
                else errors.Add($"{target.Name}: HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex) { errors.Add($"{target.Name}: {ex.Message}"); }
        }
        return sent > 0
            ? new(true, $"Discord notification sent to {sent} destination(s)" +
                        (errors.Count > 0 ? $"; {errors.Count} failed" : ""))
            : new(false, string.Join("; ", errors));
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
}
