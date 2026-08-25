using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dzl.Core.Config;

namespace Dzl.Core.App;

/// <summary>A named Discord destination. The URL remains private inside the encrypted store.</summary>
public sealed record DiscordWebhookTarget
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Primary";
    public bool Enabled { get; init; } = true;
    public bool WorkshopUpdates { get; init; } = true;
    public bool ServerLifecycle { get; init; } = true;
    public bool AdminActivity { get; init; } = true;
    public bool LogAlerts { get; init; } = true;
    public bool RemoteActivity { get; init; } = true;
    public string DisplayName => Enabled ? Name : Name + " (disabled)";

    public bool Accepts(DiscordNotificationCategory category) => category switch
    {
        DiscordNotificationCategory.WorkshopUpdates => WorkshopUpdates,
        DiscordNotificationCategory.ServerLifecycle => ServerLifecycle,
        DiscordNotificationCategory.AdminActivity => AdminActivity,
        DiscordNotificationCategory.LogAlerts => LogAlerts,
        DiscordNotificationCategory.RemoteActivity => RemoteActivity,
        _ => false
    };
}

public sealed record ResolvedDiscordWebhookTarget(string Id, string Name, string Url);

/// <summary>Stores multiple named per-instance Discord webhooks encrypted for the current Windows user.</summary>
public static class DiscordWebhookStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KM Suite Server Manager Discord webhooks v2");
    private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("KM Suite Server Manager Discord webhook v1");

    private sealed record StoredTarget
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public bool Enabled { get; init; } = true;
        public bool WorkshopUpdates { get; init; } = true;
        public bool ServerLifecycle { get; init; } = true;
        public bool AdminActivity { get; init; } = true;
        public bool LogAlerts { get; init; } = true;
        public bool RemoteActivity { get; init; } = true;
        public string ProtectedUrl { get; init; } = "";
    }

    public static string StorePath(string configPath, string instanceName) =>
        Path.Combine(Profiles.InstanceDir(instanceName, configPath), ".dzl", "discord-webhooks.json");

    public static string LegacyStorePath(string configPath, string instanceName) =>
        Path.Combine(Profiles.InstanceDir(instanceName, configPath), ".dzl", "discord-webhook.bin");

    public static bool Exists(string configPath, string instanceName) => LoadTargets(configPath, instanceName).Count > 0;

    public static IReadOnlyList<DiscordWebhookTarget> LoadTargets(string configPath, string instanceName)
    {
        var stored = LoadStored(configPath, instanceName);
        if (stored.Count > 0)
            return stored.Select(ToPublic)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return File.Exists(LegacyStorePath(configPath, instanceName))
            ? new[] { new DiscordWebhookTarget { Id = "legacy-primary", Name = "Primary", Enabled = true } }
            : Array.Empty<DiscordWebhookTarget>();
    }

    public static void Upsert(
        string configPath, string instanceName, DiscordWebhookTarget target, string? webhookUrl = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Encrypted Discord webhook storage currently requires Windows DPAPI.");
        if (string.IsNullOrWhiteSpace(target.Id)) throw new ArgumentException("Webhook id is required.");
        if (string.IsNullOrWhiteSpace(target.Name)) throw new ArgumentException("Enter a name for this webhook.");

        var rows = LoadStored(configPath, instanceName);
        var index = rows.FindIndex(p => string.Equals(p.Id, target.Id, StringComparison.Ordinal));
        var protectedUrl = index >= 0 ? rows[index].ProtectedUrl : "";
        if (!string.IsNullOrWhiteSpace(webhookUrl)) protectedUrl = Protect(ValidateUrl(webhookUrl));
        if (string.IsNullOrWhiteSpace(protectedUrl) && target.Id == "legacy-primary")
        {
            var legacy = LoadLegacy(configPath, instanceName);
            if (legacy.Length > 0) protectedUrl = Protect(legacy);
        }
        if (string.IsNullOrWhiteSpace(protectedUrl))
            throw new ArgumentException("Enter a Discord webhook URL for this destination.");

        var stored = new StoredTarget
        {
            Id = target.Id == "legacy-primary" ? Guid.NewGuid().ToString("N") : target.Id,
            Name = target.Name.Trim(),
            Enabled = target.Enabled,
            WorkshopUpdates = target.WorkshopUpdates,
            ServerLifecycle = target.ServerLifecycle,
            AdminActivity = target.AdminActivity,
            LogAlerts = target.LogAlerts,
            RemoteActivity = target.RemoteActivity,
            ProtectedUrl = protectedUrl
        };
        if (index >= 0) rows[index] = stored; else rows.Add(stored);
        SaveStored(configPath, instanceName, rows);
        var legacyPath = LegacyStorePath(configPath, instanceName);
        if (File.Exists(legacyPath)) File.Delete(legacyPath);
    }

    public static void Delete(string configPath, string instanceName, string id)
    {
        if (id == "legacy-primary")
        {
            var legacyPath = LegacyStorePath(configPath, instanceName);
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
            return;
        }
        var rows = LoadStored(configPath, instanceName);
        rows.RemoveAll(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        SaveStored(configPath, instanceName, rows);
    }

    public static IReadOnlyList<ResolvedDiscordWebhookTarget> ResolveEnabled(
        string configPath, string instanceName, DiscordNotificationCategory? category = null)
    {
        var rows = LoadStored(configPath, instanceName);
        if (rows.Count == 0)
        {
            var legacy = LoadLegacy(configPath, instanceName);
            return legacy.Length == 0
                ? Array.Empty<ResolvedDiscordWebhookTarget>()
                : new[] { new ResolvedDiscordWebhookTarget("legacy-primary", "Primary", legacy) };
        }
        return rows.Where(p => p.Enabled && (category is null || ToPublic(p).Accepts(category.Value)))
            .Select(p => new ResolvedDiscordWebhookTarget(p.Id, p.Name, Unprotect(p.ProtectedUrl, Entropy)))
            .Where(p => p.Url.Length > 0).ToList();
    }

    // Compatibility surface for the earlier single-webhook UI and encrypted store.
    public static void Save(string configPath, string instanceName, string webhookUrl)
    {
        var target = LoadTargets(configPath, instanceName).FirstOrDefault() ??
                     new DiscordWebhookTarget { Id = "primary", Name = "Primary", Enabled = true };
        Upsert(configPath, instanceName, target, webhookUrl);
    }

    public static string Load(string configPath, string instanceName) =>
        ResolveEnabled(configPath, instanceName).FirstOrDefault()?.Url ?? "";

    public static void Clear(string configPath, string instanceName)
    {
        foreach (var path in new[] { StorePath(configPath, instanceName), LegacyStorePath(configPath, instanceName) })
            if (File.Exists(path)) File.Delete(path);
    }

    private static List<StoredTarget> LoadStored(string configPath, string instanceName)
    {
        try
        {
            var path = StorePath(configPath, instanceName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<StoredTarget>>(File.ReadAllText(path), ConfigStore.Json) ?? new()
                : new();
        }
        catch { return new(); }
    }

    private static DiscordWebhookTarget ToPublic(StoredTarget p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Enabled = p.Enabled,
        WorkshopUpdates = p.WorkshopUpdates,
        ServerLifecycle = p.ServerLifecycle,
        AdminActivity = p.AdminActivity,
        LogAlerts = p.LogAlerts,
        RemoteActivity = p.RemoteActivity
    };

    private static void SaveStored(string configPath, string instanceName, IReadOnlyCollection<StoredTarget> rows)
    {
        var path = StorePath(configPath, instanceName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(rows, ConfigStore.Json));
        File.Move(temp, path, true);
    }

    private static string ValidateUrl(string webhookUrl)
    {
        var value = webhookUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.EndsWith("discord.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith("discordapp.com", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Enter a valid HTTPS Discord webhook URL.");
        return value;
    }

    private static string Protect(string value)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Encrypted Discord webhook storage currently requires Windows DPAPI.");
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string value, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(value)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return ""; }
    }

    private static string LoadLegacy(string configPath, string instanceName)
    {
        if (!OperatingSystem.IsWindows()) return "";
        try
        {
            var path = LegacyStorePath(configPath, instanceName);
            if (!File.Exists(path)) return "";
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), LegacyEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return ""; }
    }
}
