namespace Dzl.Core.Remote;

/// <summary>
/// A saved server-owner endpoint used by the Server Manager FTP/FTPS and BattlEye RCon page.
/// Passwords are deliberately not part of this model; <see cref="RemoteProfileStore"/>
/// keeps both secrets in separate Windows-DPAPI-protected fields on disk.
/// </summary>
public sealed record RemoteServerProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "My DayZ server";
    /// <summary>KM Suite server instance this endpoint administers. Empty keeps legacy profiles unlinked.</summary>
    public string InstanceName { get; init; } = "";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 21;
    public string UserName { get; init; } = "";
    public string RootPath { get; init; } = "/";
    public bool UseTls { get; init; } = true;
    public bool Passive { get; init; } = true;
    public string RconHost { get; init; } = "";
    public int RconPort { get; init; } = 2301;

    public string ProtocolLabel => UseTls ? "FTPS (explicit TLS)" : "FTP";
    public string EndpointLabel => string.IsNullOrWhiteSpace(Host)
        ? "Not configured"
        : $"{Host}:{Port}";
    public string EffectiveRconHost => string.IsNullOrWhiteSpace(RconHost) ? Host : RconHost;
    public string RconEndpointLabel => string.IsNullOrWhiteSpace(EffectiveRconHost)
        ? "Not configured"
        : $"{EffectiveRconHost}:{RconPort}";
}

/// <summary>A file or directory returned by an FTP directory listing.</summary>
public sealed record RemoteFileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long? Size = null,
    DateTimeOffset? Modified = null)
{
    public string Kind => IsDirectory ? "Folder" : "File";
    public string SizeLabel => IsDirectory ? "" : FormatSize(Size);
    public string ModifiedLabel => Modified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "";

    private static string FormatSize(long? value)
    {
        if (value is null) return "";
        double size = value.Value;
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }
}
