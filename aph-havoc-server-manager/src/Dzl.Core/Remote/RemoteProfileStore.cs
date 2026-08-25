using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dzl.Core.Config;

namespace Dzl.Core.Remote;

/// <summary>
/// Persists remote-server profiles beside the dzl config. Passwords are encrypted with
/// Windows DPAPI in CurrentUser scope and are never written as plaintext or returned in
/// the public profile model.
/// </summary>
public static class RemoteProfileStore
{
    // Keep the original FTP entropy byte-for-byte so existing saved credentials still decrypt.
    private static readonly byte[] FtpEntropy = Encoding.UTF8.GetBytes("KM Suite Server Manager FTP profiles v1");
    private static readonly byte[] RconEntropy = Encoding.UTF8.GetBytes("KM Suite Server Manager BattlEye RCon profiles v1");

    // Property defaults make older FTP-only JSON profiles forward-compatible with RCon fields.
    private sealed record StoredProfile
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string InstanceName { get; init; } = "";
        public string Host { get; init; } = "";
        public int Port { get; init; } = 21;
        public string UserName { get; init; } = "";
        public string RootPath { get; init; } = "/";
        public bool UseTls { get; init; } = true;
        public bool Passive { get; init; } = true;
        public string ProtectedPassword { get; init; } = "";
        public string RconHost { get; init; } = "";
        public int RconPort { get; init; } = 2301;
        public string ProtectedRconPassword { get; init; } = "";
    }

    public static string StorePath(string configPath) =>
        Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "remote-servers.json");

    public static IReadOnlyList<RemoteServerProfile> Load(string configPath)
    {
        try
        {
            var path = StorePath(configPath);
            if (!File.Exists(path)) return Array.Empty<RemoteServerProfile>();
            var rows = JsonSerializer.Deserialize<List<StoredProfile>>(File.ReadAllText(path), ConfigStore.Json) ?? new();
            return rows.Select(ToPublic).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return Array.Empty<RemoteServerProfile>();
        }
    }

    public static void Upsert(string configPath, RemoteServerProfile profile, string? password, string? rconPassword = null)
    {
        Validate(profile);
        var rows = LoadStored(configPath);
        var index = rows.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.Ordinal));
        var previousSecret = index >= 0 ? rows[index].ProtectedPassword : "";
        var previousRconSecret = index >= 0 ? rows[index].ProtectedRconPassword : "";
        var protectedPassword = string.IsNullOrEmpty(password) ? previousSecret : Protect(password, FtpEntropy);
        var protectedRconPassword = string.IsNullOrEmpty(rconPassword)
            ? previousRconSecret : Protect(rconPassword, RconEntropy);
        var stored = new StoredProfile
        {
            Id = profile.Id,
            Name = profile.Name.Trim(),
            InstanceName = profile.InstanceName.Trim(),
            Host = NormalizeHost(profile.Host),
            Port = profile.Port,
            UserName = profile.UserName.Trim(),
            RootPath = FtpRemoteClient.NormalizePath(profile.RootPath),
            UseTls = profile.UseTls,
            Passive = profile.Passive,
            ProtectedPassword = protectedPassword,
            RconHost = NormalizeHost(profile.RconHost),
            RconPort = profile.RconPort,
            ProtectedRconPassword = protectedRconPassword
        };
        if (index >= 0) rows[index] = stored; else rows.Add(stored);
        SaveStored(configPath, rows);
    }

    public static void Delete(string configPath, string id)
    {
        var rows = LoadStored(configPath);
        rows.RemoveAll(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        SaveStored(configPath, rows);
    }

    public static string GetPassword(string configPath, string id)
        => GetFtpPassword(configPath, id);

    public static string GetFtpPassword(string configPath, string id)
    {
        var secret = LoadStored(configPath)
            .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))?.ProtectedPassword;
        return string.IsNullOrWhiteSpace(secret) ? "" : Unprotect(secret, FtpEntropy);
    }

    public static string GetRconPassword(string configPath, string id)
    {
        var secret = LoadStored(configPath)
            .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))?.ProtectedRconPassword;
        return string.IsNullOrWhiteSpace(secret) ? "" : Unprotect(secret, RconEntropy);
    }

    public static string NormalizeHost(string host)
    {
        host = host.Trim();
        if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.Host;
        // A common paste is "ftp.example.com/some/root" without a scheme.  Treat it as an FTP
        // authority so the connection host never accidentally contains a remote path.
        if (Uri.TryCreate("ftp://" + host.TrimStart('/'), UriKind.Absolute, out uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
            return uri.Host;
        return host.TrimEnd('/').TrimEnd('\\');
    }

    private static List<StoredProfile> LoadStored(string configPath)
    {
        try
        {
            var path = StorePath(configPath);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<StoredProfile>>(File.ReadAllText(path), ConfigStore.Json) ?? new()
                : new();
        }
        catch { return new(); }
    }

    private static void SaveStored(string configPath, IReadOnlyCollection<StoredProfile> rows)
    {
        var path = StorePath(configPath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(rows, ConfigStore.Json));
        File.Move(temp, path, true);
    }

    private static RemoteServerProfile ToPublic(StoredProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        InstanceName = p.InstanceName,
        Host = p.Host,
        Port = p.Port,
        UserName = p.UserName,
        RootPath = p.RootPath,
        UseTls = p.UseTls,
        Passive = p.Passive,
        RconHost = p.RconHost,
        RconPort = p.RconPort is < 1 or > 65535 ? 2301 : p.RconPort
    };

    private static string Protect(string value, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Remote profile credentials require Windows DPAPI.");
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string value, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows()) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return ""; }
    }

    private static void Validate(RemoteServerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id)) throw new ArgumentException("Profile id is required.");
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profile name is required.");
        if (string.IsNullOrWhiteSpace(profile.Host) && string.IsNullOrWhiteSpace(profile.RconHost))
            throw new ArgumentException("An FTP or RCon host is required.");
        if (profile.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(profile.Port));
        if (profile.RconPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(profile.RconPort));
    }
}
