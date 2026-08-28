using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Dzl.Core.Env;

/// <summary>Portable snapshot of the installed dotnet host, SDKs and shared runtimes.</summary>
public sealed record DotNetEnvironment(
    string Platform,
    string Architecture,
    string HostVersion,
    IReadOnlyList<string> Sdks,
    IReadOnlyList<string> Runtimes)
{
    public bool CommandAvailable => HostVersion.Length > 0 || Sdks.Count > 0 || Runtimes.Count > 0;
    public bool HasMajor11 => HasMajor(Sdks, 11) || HasMajor(Runtimes, 11) || StartsWithMajor(HostVersion, 11);

    public string Summary
    {
        get
        {
            var host = HostVersion.Length > 0
                ? $"dotnet {HostVersion}"
                : CommandAvailable
                    ? "dotnet host version unavailable"
                    : "dotnet command not found";
            var eleven = HasMajor11 ? ".NET 11 detected" : ".NET 11 not installed";
            return $"{Platform} {Architecture}; {host}; {eleven}; {Sdks.Count} SDK(s), {Runtimes.Count} runtime(s)";
        }
    }

    private static bool HasMajor(IEnumerable<string> versions, int major) =>
        versions.Any(version => StartsWithMajor(version, major));

    private static bool StartsWithMajor(string version, int major)
    {
        var value = version.TrimStart();
        var end = value.IndexOfAny(['.', ' ', '[']);
        if (end < 0) end = value.Length;
        return int.TryParse(value[..end], out var found) && found == major;
    }
}

public static class DotNetEnvironmentDetector
{
    /// <summary>
    /// Detect the dotnet installation on Windows or Linux. The optional runner makes this probe
    /// deterministic in tests; it receives <c>--version</c>, <c>--list-sdks</c>, or
    /// <c>--list-runtimes</c> and returns command output.
    /// </summary>
    public static DotNetEnvironment Detect(Func<string, string?>? runner = null)
    {
        runner ??= RunDotNet;
        var host = runner("--version")?.Trim() ?? string.Empty;
        var sdks = Versions(runner("--list-sdks"));
        var runtimes = Versions(runner("--list-runtimes"));
        var platform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : RuntimeInformation.OSDescription;
        return new DotNetEnvironment(platform, RuntimeInformation.OSArchitecture.ToString(), host, sdks, runtimes);
    }

    private static IReadOnlyList<string> Versions(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return Array.Empty<string>();
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.FirstOrDefault(part => char.IsDigit(part[0])) ?? string.Empty;
            })
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? RunDotNet(string argument)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet", argument)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
