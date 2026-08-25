using Dzl.Core.Mods;

namespace Dzl.Tray.ViewModels;

/// <summary>One row of the Mods page "Mod presets" card: name, a short summary of the
/// loadout's contents, and whether the active server currently has this preset applied.</summary>
public sealed record ModPresetRowVm(string Name, int ModCount, string Summary, bool IsApplied, string AppliedLabel)
{
    public static ModPresetRowVm From(ModPreset p, string appliedName, string serverName)
    {
        var leafs = p.Mods.Take(3)
            .Select(m => System.IO.Path.GetFileName(m.Path.TrimEnd('\\', '/')));
        var count = p.Mods.Count == 1 ? "1 mod" : $"{p.Mods.Count} mods";
        var summary = p.Mods.Count == 0
            ? "empty"
            : $"{count} · {string.Join(", ", leafs)}{(p.Mods.Count > 3 ? "…" : "")}";
        return new(p.Name, p.Mods.Count, summary,
            string.Equals(p.Name, appliedName, StringComparison.OrdinalIgnoreCase), $"applied · {serverName}");
    }
}
