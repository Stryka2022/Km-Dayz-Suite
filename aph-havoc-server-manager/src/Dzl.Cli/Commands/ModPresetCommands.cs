using System.CommandLine;
using Dzl.Core.App;

namespace Dzl.Cli.Commands;

/// <summary>modpreset (list / save / apply / rm) — named mod loadouts, independent of server instances.</summary>
internal static class ModPresetCommands
{
    public static Command ModPreset(CliContext c)
    {
        var cmd = new Command("modpreset", "Save/apply named mod loadouts (lists of active mods).");
        cmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var presets = new LauncherService(configPath).ModPresets();
            CliOut.List(presets, "(no mod presets)",
                p => $"{(p.Active ? "*" : " ")} {p.Name}  ({p.ModCount} mods)");
        });

        var saveArg = new Argument<string>("name", "Preset name.");
        var saveCmd = new Command("save", "Snapshot the active server's enabled mods as a loadout.") { saveArg };
        saveCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var res = new LauncherService(configPath).SaveModPreset(ctx.ParseResult.GetValueForArgument(saveArg));
            CliOut.Result(ctx, res.Ok, res.Message);
        });
        cmd.AddCommand(saveCmd);

        var applyArg = new Argument<string>("name", "Preset name.");
        var applyCmd = new Command("apply", "Apply a loadout to the active server (takes effect on next start).") { applyArg };
        applyCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var res = new LauncherService(configPath).ApplyModPreset(ctx.ParseResult.GetValueForArgument(applyArg));
            CliOut.Result(ctx, res.Ok, res.Message);
        });
        cmd.AddCommand(applyCmd);

        var rmArg = new Argument<string>("name", "Preset name.");
        var rmCmd = new Command("rm", "Delete a mod preset.") { rmArg };
        rmCmd.SetHandler(ctx =>
        {
            var (_, _, _, configPath) = c.Resolve(ctx);
            var res = new LauncherService(configPath).DeleteModPreset(ctx.ParseResult.GetValueForArgument(rmArg));
            CliOut.Result(ctx, res.Ok, res.Message);
        });
        cmd.AddCommand(rmCmd);
        return cmd;
    }
}
