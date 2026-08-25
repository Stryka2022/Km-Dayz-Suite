using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Dzl.Core.Remote;

public enum RemoteServerOperatingSystem
{
    Unknown,
    LinuxOrUnix,
    Windows
}

/// <summary>
/// Small FTP/explicit-FTPS client used by the Server Manager. It intentionally uses the
/// Windows/.NET FTP stack already shipped with the application, so the GPL companion does
/// not need to download or dynamically load a third-party networking component.
/// </summary>
public sealed partial class FtpRemoteClient
{
    private readonly RemoteServerProfile _profile;
    private readonly NetworkCredential _credential;

    public RemoteServerOperatingSystem DetectedOperatingSystem { get; private set; }

    public FtpRemoteClient(RemoteServerProfile profile, string password)
    {
        _profile = profile;
        _credential = new NetworkCredential(
            string.IsNullOrWhiteSpace(profile.UserName) ? "anonymous" : profile.UserName,
            string.IsNullOrWhiteSpace(profile.UserName) ? "km-suite@localhost" : password);
    }

    public async Task<string> TestAsync(CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(_profile.RootPath, WebRequestMethods.Ftp.PrintWorkingDirectory);
        using var response = await GetResponseAsync(request, cancellationToken);
        return response.StatusDescription?.Trim() ?? "Connected";
    }

    public async Task<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        path = NormalizePath(path);
        var request = CreateRequest(path, WebRequestMethods.Ftp.ListDirectoryDetails);
        using var response = await GetResponseAsync(request, cancellationToken);
        await using var stream = response.GetResponseStream();
        using var reader = new StreamReader(stream);
        var result = new List<RemoteFileEntry>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var detected = DetectOperatingSystemFromListLine(line);
            if (detected != RemoteServerOperatingSystem.Unknown)
            {
                DetectedOperatingSystem = detected;
            }
            var entry = ParseListLine(path, line);
            if (entry is not null && entry.Name is not "." and not "..") result.Add(entry);
        }
        return result
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<byte[]> DownloadBytesAsync(string path, int maxBytes = 8 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(path, WebRequestMethods.Ftp.DownloadFile);
        using var response = await GetResponseAsync(request, cancellationToken);
        if (response.ContentLength > maxBytes)
            throw new IOException($"Remote file is larger than the {maxBytes / 1024 / 1024} MB editor limit.");
        await using var input = response.GetResponseStream();
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes)
                throw new IOException($"Remote file is larger than the {maxBytes / 1024 / 1024} MB editor limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(remotePath, WebRequestMethods.Ftp.DownloadFile);
        using var response = await GetResponseAsync(request, cancellationToken);
        await using var input = response.GetResponseStream();
        await using var output = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);
        await input.CopyToAsync(output, cancellationToken);
    }

    public async Task UploadBytesAsync(string remotePath, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(remotePath, WebRequestMethods.Ftp.UploadFile);
        request.ContentLength = bytes.Length;
        using var registration = cancellationToken.Register(request.Abort);
        await using (var output = await request.GetRequestStreamAsync())
            await output.WriteAsync(bytes, cancellationToken);
        using var response = (FtpWebResponse)await request.GetResponseAsync();
    }

    public async Task UploadFileAsync(string localPath, string remotePath, CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(remotePath, WebRequestMethods.Ftp.UploadFile);
        var info = new FileInfo(localPath);
        request.ContentLength = info.Length;
        using var registration = cancellationToken.Register(request.Abort);
        await using (var output = await request.GetRequestStreamAsync())
        await using (var input = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true))
            await input.CopyToAsync(output, cancellationToken);
        using var response = (FtpWebResponse)await request.GetResponseAsync();
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        ExecuteAsync(path, WebRequestMethods.Ftp.MakeDirectory, cancellationToken: cancellationToken);

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default) =>
        ExecuteAsync(path, WebRequestMethods.Ftp.DeleteFile, cancellationToken: cancellationToken);

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        ExecuteAsync(path, WebRequestMethods.Ftp.RemoveDirectory, cancellationToken: cancellationToken);

    public Task RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        ValidateName(newName);
        return ExecuteAsync(path, WebRequestMethods.Ftp.Rename, newName, cancellationToken);
    }

    private async Task ExecuteAsync(string path, string method, string? renameTo = null,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(path, method);
        if (renameTo is not null) request.RenameTo = renameTo;
        using var response = await GetResponseAsync(request, cancellationToken);
    }

    #pragma warning disable SYSLIB0014 // FtpWebRequest remains the built-in .NET FTP/FTPS implementation.
    private FtpWebRequest CreateRequest(string path, string method)
    {
        var request = (FtpWebRequest)WebRequest.Create(BuildUri(_profile, path));
        request.Method = method;
        request.Credentials = _credential;
        request.EnableSsl = _profile.UseTls;
        request.UsePassive = _profile.Passive;
        request.UseBinary = true;
        request.KeepAlive = false;
        request.Timeout = 30_000;
        request.ReadWriteTimeout = 30_000;
        return request;
    }
    #pragma warning restore SYSLIB0014

    private static async Task<FtpWebResponse> GetResponseAsync(FtpWebRequest request, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(request.Abort);
        try { return (FtpWebResponse)await request.GetResponseAsync(); }
        catch (WebException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("FTP operation cancelled.", ex, cancellationToken);
        }
    }

    public static Uri BuildUri(RemoteServerProfile profile, string path)
    {
        var host = RemoteProfileStore.NormalizeHost(profile.Host);
        var encodedPath = string.Join('/', NormalizePath(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        var builder = new UriBuilder(Uri.UriSchemeFtp, host, profile.Port)
        {
            Path = encodedPath.Length == 0 ? "/" : "/" + encodedPath
        };
        return builder.Uri;
    }

    public static string NormalizePath(string? path)
    {
        var parts = new List<string>();
        foreach (var part in (path ?? "/").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return parts.Count == 0 ? "/" : "/" + string.Join('/', parts);
    }

    public static string CombinePath(string path, string name)
    {
        ValidateName(name);
        return NormalizePath(NormalizePath(path).TrimEnd('/') + "/" + name);
    }

    public static string ParentPath(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized == "/") return "/";
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? "/" : normalized[..index];
    }

    public static RemoteFileEntry? ParseListLine(string parentPath, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var unix = UnixListRegex().Match(line);
        if (unix.Success)
        {
            var isDir = unix.Groups[1].Value == "d";
            var name = unix.Groups[5].Value;
            if (!isDir && name.Contains(" -> ", StringComparison.Ordinal)) name = name.Split(" -> ", 2)[0];
            _ = long.TryParse(unix.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
            DateTimeOffset? modified = ParseUnixDate(unix.Groups[3].Value, unix.Groups[4].Value);
            return new RemoteFileEntry(name, CombinePath(parentPath, name), isDir, isDir ? null : size, modified);
        }

        var windows = WindowsListRegex().Match(line);
        if (windows.Success)
        {
            var isDir = string.Equals(windows.Groups[3].Value, "<DIR>", StringComparison.OrdinalIgnoreCase);
            var name = windows.Groups[4].Value.Trim();
            long? size = isDir ? null : long.Parse(windows.Groups[3].Value, CultureInfo.InvariantCulture);
            DateTimeOffset? modified = DateTime.TryParseExact(
                windows.Groups[1].Value + " " + windows.Groups[2].Value,
                "MM-dd-yy hh:mmtt", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
                ? new DateTimeOffset(date) : null;
            return new RemoteFileEntry(name, CombinePath(parentPath, name), isDir, size, modified);
        }

        // Some servers return names only even for LIST. Keep them usable as files rather than
        // hiding data; directory navigation is still available on standards-compliant listings.
        var fallback = line.Trim();
        return fallback.Length == 0 ? null : new RemoteFileEntry(fallback, CombinePath(parentPath, fallback), false);
    }

    public static RemoteServerOperatingSystem DetectOperatingSystemFromListLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return RemoteServerOperatingSystem.Unknown;
        if (UnixListRegex().IsMatch(line)) return RemoteServerOperatingSystem.LinuxOrUnix;
        if (WindowsListRegex().IsMatch(line)) return RemoteServerOperatingSystem.Windows;
        return RemoteServerOperatingSystem.Unknown;
    }

    private static DateTimeOffset? ParseUnixDate(string monthDay, string yearOrTime)
    {
        var text = $"{monthDay} {yearOrTime}";
        var formats = yearOrTime.Contains(':') ? new[] { "MMM d HH:mm", "MMM dd HH:mm" } : new[] { "MMM d yyyy", "MMM dd yyyy" };
        if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)) return null;
        if (yearOrTime.Contains(':')) date = date.AddYears(DateTime.Now.Year - date.Year);
        return new DateTimeOffset(date);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
            throw new ArgumentException("Remote name must be a single file or folder name.", nameof(name));
    }

    [GeneratedRegex(@"^([dl-])[rwxStTs-]{9}\s+\d+\s+\S+\s+\S+\s+(\d+)\s+([A-Za-z]{3}\s+\d{1,2})\s+(\d{2}:\d{2}|\d{4})\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex UnixListRegex();

    [GeneratedRegex(@"^(\d{2}-\d{2}-\d{2})\s+(\d{2}:\d{2}[AP]M)\s+(<DIR>|\d+)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsListRegex();
}
