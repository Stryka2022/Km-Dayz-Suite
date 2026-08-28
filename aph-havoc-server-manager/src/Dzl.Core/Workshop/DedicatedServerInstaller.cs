using Dzl.Core.Config;
using Dzl.Core.Procs;

namespace Dzl.Core.Workshop;

/// <summary>Installs or validates Steam app 223350 for one server instance.</summary>
public static class DedicatedServerInstaller
{
    public const string AppId = "223350";

    public static async Task<(bool ok, string message)> InstallAsync(
        string configPath, string installPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var (cfg, _, _) = Profiles.ResolveActive(configPath);
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
            var login = string.IsNullOrWhiteSpace(cfg.SteamLogin) ? "anonymous" : cfg.SteamLogin.Trim();
            var result = await Task.Run(() => ProcRunner.Run(steamCmd,
                new[]
                {
                    "+force_install_dir", installPath,
                    "+login", login,
                    "+app_update", AppId, "validate",
                    "+quit",
                }, new RunOpts(WorkingDir: Path.GetDirectoryName(steamCmd), TimeoutMs: 0)), cancellationToken)
                .ConfigureAwait(false);

            if (result.Ok)
                return (true, $"DayZ Dedicated Server installed at {installPath}");
            var detail = result.AllOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            return (false, string.IsNullOrWhiteSpace(detail) ? $"steamcmd exited with code {result.Code}" : detail);
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
}
