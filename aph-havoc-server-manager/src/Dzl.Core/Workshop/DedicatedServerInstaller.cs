using System.Diagnostics;
using Dzl.Core.Config;
using Dzl.Core.Procs;
using Dzl.Core.Projects;

namespace Dzl.Core.Workshop;

/// <summary>Installs or validates Steam app 223350 for one server instance.</summary>
public static class DedicatedServerInstaller
{
    public const string AppId = "223350";
    public const string ServerExecutable = "DayZServer_x64.exe";

    /// <summary>Turn the folder selected by the user into one isolated install folder. Selecting
    /// <c>D:\DayZ Servers</c> for <c>My_PvE_Server</c> installs into
    /// <c>D:\DayZ Servers\My_PvE_Server</c>. An already-resolved path is left unchanged.</summary>
    public static string ResolveInstanceInstallPath(string selectedPath, string instanceFolderName)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) return "";
        var safeName = ProjectPaths.SafeInstanceName(instanceFolderName);
        var full = Path.GetFullPath(selectedPath.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(Path.GetFileName(full), safeName, StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(full, ServerExecutable)))
            return full;
        return Path.Combine(full, safeName);
    }

    public static IReadOnlyList<string> BuildArguments(string installPath, string login) =>
        new[]
        {
            "+force_install_dir", installPath,
            "+login", login.Trim(),
            "+app_update", AppId, "validate",
            "+quit",
        };

    public static bool IsInstalled(string installPath) =>
        File.Exists(Path.Combine(installPath, ServerExecutable));

    public static async Task<(bool ok, string message)> InstallAsync(
        string configPath, string installPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var (cfg, _, _) = Profiles.ResolveActive(configPath);
            if (string.IsNullOrWhiteSpace(cfg.SteamLogin))
                return (false, "Steam account name is required because DayZ Dedicated Server app 223350 " +
                               "does not install anonymously. Sign in under Settings → Accounts, then retry.");
            var steamCmd = ResolveSteamCmd(cfg, configPath);
            if (!File.Exists(steamCmd))
            {
                if (!OperatingSystem.IsWindows())
                    return (false, "set the steamcmd path in Settings before installing on Linux");

                var dest = Path.Combine(Path.GetDirectoryName(configPath) ?? Environment.CurrentDirectory, "steamcmd");
                var installed = await SteamCmdInstaller.InstallAsync(dest).ConfigureAwait(false);
                if (!installed.ok) return (false, installed.message);
                steamCmd = installed.exePath;
            }

            Directory.CreateDirectory(installPath);
            var args = BuildArguments(installPath, cfg.SteamLogin);
            var consoleLog = Path.Combine(Path.GetDirectoryName(steamCmd) ?? "", "logs", "console_log.txt");
            var previousLogLength = File.Exists(consoleLog) ? new FileInfo(consoleLog).Length : 0;

            int exitCode;
            string capturedOutput = "";
            if (OperatingSystem.IsWindows())
            {
                // App 223350 requires an owned Steam account. A normal console is intentional:
                // steamcmd can request the password and Steam Guard code without the suite ever
                // reading, storing or logging those secrets.
                var info = new ProcessStartInfo(steamCmd)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(steamCmd) ?? Environment.CurrentDirectory,
                    WindowStyle = ProcessWindowStyle.Normal,
                };
                foreach (var arg in args) info.ArgumentList.Add(arg);
                using var process = Process.Start(info);
                if (process is null) return (false, "could not start steamcmd");
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                exitCode = process.ExitCode;
            }
            else
            {
                var result = await Task.Run(() => ProcRunner.Run(steamCmd, args,
                    new RunOpts(WorkingDir: Path.GetDirectoryName(steamCmd), TimeoutMs: 0)), cancellationToken)
                    .ConfigureAwait(false);
                exitCode = result.Code;
                capturedOutput = result.AllOutput;
            }

            // steamcmd can return exit code 0 after printing "ERROR! Failed to install app".
            // The executable on disk is the authoritative success condition.
            if (IsInstalled(installPath))
                return (true, $"DayZ Dedicated Server installed and verified at {installPath}");

            var detail = LatestSteamCmdError(consoleLog, previousLogLength)
                         ?? capturedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                             .LastOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase))?.Trim();
            var suffix = string.IsNullOrWhiteSpace(detail) ? $"steamcmd exited with code {exitCode}" : detail;
            return (false, $"{ServerExecutable} was not installed at {installPath}. {suffix}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static string ResolveSteamCmd(DzlConfig cfg, string configPath)
    {
        if (!string.IsNullOrWhiteSpace(cfg.SteamCmdPath)) return cfg.SteamCmdPath.Trim();
        var exe = OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh";
        return Path.Combine(Path.GetDirectoryName(configPath) ?? Environment.CurrentDirectory, "steamcmd", exe);
    }

    private static string? LatestSteamCmdError(string logPath, long previousLength)
    {
        try
        {
            if (!File.Exists(logPath)) return null;
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (previousLength == stream.Length) return null; // no new log output; never report a stale failure
            if (previousLength > 0 && previousLength < stream.Length) stream.Position = previousLength;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .LastOrDefault(line => line.Contains("ERROR!", StringComparison.OrdinalIgnoreCase)
                                       || line.Contains("No subscription", StringComparison.OrdinalIgnoreCase)
                                       || line.Contains("Missing configuration", StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }
}
