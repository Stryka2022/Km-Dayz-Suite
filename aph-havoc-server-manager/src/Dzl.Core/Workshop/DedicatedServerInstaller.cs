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

    /// <summary>Return an already-installed Steam copy that can seed an isolated instance.
    /// Reusing the local official files avoids a second SteamCMD sign-in and a multi-gigabyte
    /// download. The destination itself is returned when it is already complete.</summary>
    public static string? FindReusableInstall(DzlConfig cfg, string installPath)
    {
        if (IsInstalled(installPath)) return Path.GetFullPath(installPath);
        // The active named instance is often the only configured valid runtime. Reuse it before
        // falling back to the machine-wide install so adding server #2/#3 never needlessly returns
        // to SteamCMD authentication or another download.
        foreach (var candidate in new[] { cfg.ServerInstallPathOverride, cfg.DayzServerPath })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var source = Path.GetFullPath(candidate.Trim());
            if (IsInstalled(source)) return source;
        }
        return null;
    }

    /// <summary>Copy an existing official DayZ Server installation into an isolated instance.
    /// Matching files are skipped so an interrupted copy can be resumed cheaply.</summary>
    public static async Task<(bool ok, string message)> CopyExistingInstallAsync(
        string sourcePath, string installPath, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var source = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var destination = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!IsInstalled(source))
                return (false, $"no valid DayZ Dedicated Server installation was found at {source}");
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
                return (true, $"DayZ Dedicated Server is already installed at {destination}");
            if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return (false, "the instance install folder cannot be inside the source DayZ Server folder");

            var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
            var totalBytes = files.Sum(file => new FileInfo(file).Length);
            long completedBytes = 0;
            var lastReport = Stopwatch.StartNew();
            progress?.Report($"copying the installed DayZ Server into {destination} — 0%");

            await Task.Run(() =>
            {
                Directory.CreateDirectory(destination);
                for (var index = 0; index < files.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourceFile = files[index];
                    var relative = Path.GetRelativePath(source, sourceFile);
                    var sourceInfo = new FileInfo(sourceFile);
                    if (!IsInstanceOwnedPath(relative))
                    {
                        var destinationFile = Path.Combine(destination, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                        var destinationInfo = new FileInfo(destinationFile);
                        if (!destinationInfo.Exists || destinationInfo.Length != sourceInfo.Length
                                                    || destinationInfo.LastWriteTimeUtc != sourceInfo.LastWriteTimeUtc)
                            File.Copy(sourceFile, destinationFile, overwrite: true);
                    }

                    completedBytes += sourceInfo.Length;
                    if (lastReport.ElapsedMilliseconds >= 400 || index == files.Length - 1)
                    {
                        var percent = totalBytes <= 0 ? 100 : (int)Math.Min(100, completedBytes * 100 / totalBytes);
                        progress?.Report($"copying the installed DayZ Server into {destination} — {percent}% " +
                                         $"({index + 1}/{files.Length} files)");
                        lastReport.Restart();
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            return IsInstalled(destination)
                ? (true, $"DayZ Dedicated Server copied and verified at {destination}")
                : (false, $"{ServerExecutable} was not copied to {destination}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Configuration, mission and persistence content belongs to the named instance and
    /// must survive a runtime install/repair copied into the same folder.</summary>
    public static bool IsInstanceOwnedPath(string relativePath)
    {
        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var first = normalized.Split(Path.DirectorySeparatorChar, 2)[0];
        return first.Equals("serverDZ.cfg", StringComparison.OrdinalIgnoreCase)
               || first.Equals("mpmissions", StringComparison.OrdinalIgnoreCase)
               || first.Equals("profiles", StringComparison.OrdinalIgnoreCase)
               || first.Equals("profiles_client", StringComparison.OrdinalIgnoreCase)
               || first.Equals(".dzl", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<(bool ok, string message)> InstallAsync(
        string configPath, string installPath, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (cfg, _, _) = Profiles.ResolveActive(configPath);
            var reusable = FindReusableInstall(cfg, installPath);
            if (reusable is not null)
            {
                if (Path.GetFullPath(reusable).Equals(Path.GetFullPath(installPath), StringComparison.OrdinalIgnoreCase))
                    return (true, $"DayZ Dedicated Server is already installed at {installPath}");
                return await CopyExistingInstallAsync(reusable, installPath, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(cfg.SteamLogin))
                return (false, "Steam account name is required because DayZ Dedicated Server app 223350 " +
                               "does not install anonymously. SteamCMD uses a separate console sign-in; " +
                               "set the Steam account name in Settings, then retry.");
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
                progress?.Report("SteamCMD needs a separate interactive sign-in. In the console, type your " +
                                 "Steam password (characters stay hidden), press Enter, then complete Steam Guard. " +
                                 "Do not close the console while the server downloads.");
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
            if (exitCode == unchecked((int)0xC000013A))
                suffix = "SteamCMD was closed before sign-in/download completed. At the password prompt, type " +
                         "the Steam password even though no characters are displayed, then press Enter and " +
                         "complete Steam Guard.";
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
