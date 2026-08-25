using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Dzl.Core.Ipc;
using Dzl.Core.Launch;

namespace Dzl.Cli.Commands;

/// <summary>start / stop / restart.</summary>
internal static class LifecycleCommands
{
    /// <summary>Print the routed OpResult's real message (and fail the exit code on ok=false)
    /// instead of fabricating a success line — e.g. an offline instance refuses server starts.</summary>
    private static void PrintOp(InvocationContext ctx, string json, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var ok = doc.RootElement.TryGetProperty("ok", out var o) && o.GetBoolean();
            var message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (ok) Console.WriteLine(message ?? fallback);
            else CliOut.Fail(ctx, message ?? "operation failed");
        }
        catch (JsonException) { Console.WriteLine(fallback); }
    }
    public static Command Start(CliContext c)
    {
        // Intentional no-op: '--debug' is the default mode, so the flag is accepted
        // (scripts may pass it) but never read — '--normal' is the only mode switch.
        var startDebug = new Option<bool>("--debug", () => true, "Debug mode (default).");
        var startNormal = new Option<bool>("--normal", "Normal (release) mode.");
        var startClient = new Option<bool>("--client", "Also start the client.");
        var startNoConnect = new Option<bool>("--no-connect",
            "Start the client without -connect: mods + mission load, but the game stays in the main menu.");
        var startDryRun = new Option<bool>("--dry-run", "Print argv, don't spawn.");
        var startCmd = new Command("start", "Start the server (and optionally client).")
        { startDebug, startNormal, startClient, startNoConnect, startDryRun };
        startCmd.SetHandler(ctx =>
        {
            var (cfg, _, _, configPath) = c.Resolve(ctx);
            var normal = ctx.ParseResult.GetValueForOption(startNormal);
            var mode = normal ? "normal" : "debug";
            var client = ctx.ParseResult.GetValueForOption(startClient);
            var noConnect = ctx.ParseResult.GetValueForOption(startNoConnect);
            var dryRun = ctx.ParseResult.GetValueForOption(startDryRun);
            if (dryRun)
            {
                var targets = new List<string> { "server" };
                if (client) targets.Add("client");
                foreach (var target in targets)
                {
                    var exe = target == "server"
                        ? ProcessManager.ServerExe(cfg, mode)
                        : ProcessManager.ClientExe(cfg, mode);
                    // Mirror the real launch path: an offline instance's client never connects.
                    var args = ArgvBuilder.Build(mode, target, cfg, connect: !noConnect && !cfg.OfflineMode);
                    Console.WriteLine($"{exe} {string.Join(' ', args)}");
                }
                return;
            }
            var json = new ControlPlane(configPath).StartJson(mode, client, "cli", noConnect);
            PrintOp(ctx, json, $"started server{(client ? " + client" : "")} ({mode})");
        });
        return startCmd;
    }

    public static Command Stop(CliContext c)
    {
        var stopClient = new Option<bool>("--client", "Also stop the client.");
        var stopCmd = new Command("stop", "Stop server (and client with --client).") { stopClient };
        stopCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var client = ctx.ParseResult.GetValueForOption(stopClient);
            var json = new ControlPlane(configPath).StopJson(client);
            PrintOp(ctx, json, $"stopped server{(client ? " + client" : "")}");
        });
        return stopCmd;
    }

    public static Command Restart(CliContext c)
    {
        // Intentional no-op: '--debug' is the default mode, so the flag is accepted
        // (scripts may pass it) but never read — '--normal' is the only mode switch.
        var restartDebug = new Option<bool>("--debug", () => true, "Debug mode (default).");
        var restartNormal = new Option<bool>("--normal", "Normal (release) mode.");
        var restartCmd = new Command("restart", "Restart the server.") { restartDebug, restartNormal };
        restartCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var mode = ctx.ParseResult.GetValueForOption(restartNormal) ? "normal" : "debug";
            var json = new ControlPlane(configPath).RestartJson(mode, "cli");
            PrintOp(ctx, json, "restarted server");
        });
        return restartCmd;
    }
}
