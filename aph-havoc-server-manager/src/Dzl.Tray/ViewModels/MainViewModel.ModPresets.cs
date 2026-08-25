using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.Config;
using Dzl.Core.Mods;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // === Mod presets (loadouts) ===========================================
    //
    // Like instance-preset ops, everything here runs DIRECTLY against Core (file I/O) —
    // never through ControlPlane (self-pipe deadlock; see MainViewModel.Presets.cs).

    /// <summary>Sentinel for "don't apply a loadout" in the New-server mod-preset dropdown.</summary>
    public const string NoModPresetChoice = "(no mod preset)";

    [ObservableProperty] private string _selectedModPreset = "";
    [ObservableProperty] private string _newModPresetName = "";
    [ObservableProperty] private bool _modPresetModified;
    [ObservableProperty] private string _modPresetStatus = "";

    public ObservableCollection<string> ModPresetNames { get; } = new();
    public ObservableCollection<string> ModPresetChoices { get; } = new();

    /// <summary>Rows for the Mods page "Mod presets" card (name + summary + applied flag).</summary>
    public ObservableCollection<ModPresetRowVm> ModPresetRows { get; } = new();

    /// <summary>True while <see cref="LoadModPresets"/> repopulates the combo, so the TwoWay
    /// selection binding doesn't re-apply the loadout during programmatic refresh.</summary>
    private bool _suppressModPresetSwitch;

    private void LoadModPresets()
    {
        _suppressModPresetSwitch = true;
        try
        {
            ModPresetNames.Clear();
            ModPresetChoices.Clear();
            ModPresetRows.Clear();
            ModPresetChoices.Add(NoModPresetChoice);
            foreach (var p in ModPresetStore.List(_configPath))
            {
                ModPresetNames.Add(p.Name);
                ModPresetChoices.Add(p.Name);
                ModPresetRows.Add(ModPresetRowVm.From(p, _cfg.ModPreset, ActiveName));
            }
            SelectedModPreset = ModPresetNames.FirstOrDefault(n =>
                string.Equals(n, _cfg.ModPreset, StringComparison.OrdinalIgnoreCase)) ?? "";
        }
        finally { _suppressModPresetSwitch = false; }
        RefreshModPresetModified();
    }

    /// <summary>Combo auto-apply: selecting a loadout applies it to the active server immediately
    /// (same pattern as the top-bar server switcher).</summary>
    partial void OnSelectedModPresetChanged(string value)
    {
        if (_suppressModPresetSwitch || string.IsNullOrEmpty(value)) return;
        ApplyModPresetByName(value);
    }

    /// <summary>Apply a loadout to the active server and reload (shared by the editor combo
    /// and the Mods page card rows).</summary>
    private void ApplyModPresetByName(string name)
    {
        var preset = ModPresetStore.Load(name, _configPath);
        if (preset is null) return;
        var cfg = _cfg with { Mods = ModPresetStore.Apply(_cfg.Mods, preset), ModPreset = preset.Name };
        Profiles.Save(cfg, ActiveName, _configPath);
        Reload();
        ModPresetStatus = $"✓ applied '{preset.Name}' ({preset.Mods.Count} mods)";
    }

    [RelayCommand]
    private void ApplyModPresetRow(ModPresetRowVm? row)
    {
        if (row is not null) ApplyModPresetByName(row.Name);
    }

    /// <summary>Recompute the "(modified)" hint from the live rows vs the selected preset file.
    /// Called from <see cref="Persist"/> (any loadout edit) and <see cref="LoadModPresets"/>.</summary>
    private void RefreshModPresetModified()
    {
        var preset = string.IsNullOrEmpty(SelectedModPreset)
            ? null : ModPresetStore.Load(SelectedModPreset, _configPath);
        ModPresetModified = preset is not null && ModPresetStore.IsModified(_cfg.Mods, preset);
    }

    /// <summary>Save the current loadout under the name typed in the inline box (confirm on overwrite).</summary>
    [RelayCommand]
    private void SaveModPresetAs()
    {
        var name = NewModPresetName.Trim();
        if (!ModPresetStore.IsValidName(name)) { ModPresetStatus = "✗ enter a valid preset name"; return; }
        if (ModPresetNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            var ok = System.Windows.MessageBox.Show(
                $"Mod preset \"{name}\" already exists. Overwrite it with the current loadout?",
                "Save mod preset", System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
            if (!ok) return;
        }
        Persist();   // flush live checkbox/order state into _cfg before snapshotting
        var (saved, message) = ModPresetStore.Save(ModPresetStore.FromMods(name, _cfg.Mods), _configPath);
        ModPresetStatus = (saved ? "✓ " : "✗ ") + message;
        if (!saved) return;
        Profiles.Save(_cfg with { ModPreset = name }, ActiveName, _configPath);
        NewModPresetName = "";
        Reload();
    }

    /// <summary>Overwrite the selected preset with the current (modified) loadout.</summary>
    [RelayCommand]
    private void UpdateModPreset()
    {
        if (string.IsNullOrEmpty(SelectedModPreset)) { ModPresetStatus = "✗ no preset selected"; return; }
        Persist();
        var (saved, message) = ModPresetStore.Save(
            ModPresetStore.FromMods(SelectedModPreset, _cfg.Mods), _configPath);
        ModPresetStatus = (saved ? "✓ " : "✗ ") + message;
        RefreshModPresetModified();
    }

    /// <summary>Delete the selected preset (bar ⋯ menu).</summary>
    [RelayCommand]
    private void DeleteModPreset() => DeleteModPresetByName(SelectedModPreset);

    /// <summary>Delete a preset from its Mods page card row.</summary>
    [RelayCommand]
    private void DeleteModPresetRow(ModPresetRowVm? row)
    {
        if (row is not null) DeleteModPresetByName(row.Name);
    }

    /// <summary>Delete a preset (with confirmation). The active instance's dangling
    /// mod_preset pointer is cleared so the editor combo shows no selection.</summary>
    private void DeleteModPresetByName(string name)
    {
        if (string.IsNullOrEmpty(name)) { ModPresetStatus = "✗ no preset selected"; return; }
        var ok = System.Windows.MessageBox.Show(
            $"Delete mod preset \"{name}\"? Server mod lists are not touched.",
            "Delete mod preset", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        if (!ok) return;
        ModPresetStatus = ModPresetStore.Delete(name, _configPath)
            ? $"✓ deleted mod preset '{name}'" : $"✗ no mod preset '{name}'";
        if (string.Equals(_cfg.ModPreset, name, StringComparison.OrdinalIgnoreCase))
            Profiles.Save(_cfg with { ModPreset = "" }, ActiveName, _configPath);
        Reload();
    }
}
