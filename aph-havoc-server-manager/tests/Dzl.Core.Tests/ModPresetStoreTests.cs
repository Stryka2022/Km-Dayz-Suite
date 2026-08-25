using Dzl.Core.Config;
using Dzl.Core.Mods;
using FluentAssertions;

public class ModPresetStoreTests
{
    private static ModEntry M(string path, bool enabled = false, string side = "both") =>
        new() { Path = path, Enabled = enabled, Side = side };

    private static ModPreset P(params (string path, string side)[] mods) => new()
    {
        Name = "test",
        Mods = mods.Select(m => new ModPresetEntry { Path = m.path, Side = m.side }).ToList(),
    };

    // --- Apply (pure) ------------------------------------------------------

    [Fact]
    public void Apply_orders_preset_mods_first_enabled_in_preset_order()
    {
        var current = new List<ModEntry> { M(@"P:\@A"), M(@"P:\@B", enabled: true), M(@"P:\@C") };
        var preset = P((@"P:\@C", "both"), (@"P:\@A", "both"));

        var r = ModPresetStore.Apply(current, preset);

        r.Select(m => m.Path).Should().ContainInOrder(@"P:\@C", @"P:\@A", @"P:\@B");
        r[0].Enabled.Should().BeTrue();
        r[1].Enabled.Should().BeTrue();
        r[2].Enabled.Should().BeFalse();   // was enabled, not in preset -> off
    }

    [Fact]
    public void Apply_keeps_relative_order_of_non_preset_mods()
    {
        var current = new List<ModEntry> { M(@"P:\@X"), M(@"P:\@Y"), M(@"P:\@Z") };
        var preset = P((@"P:\@Y", "both"));

        var r = ModPresetStore.Apply(current, preset);

        r.Select(m => m.Path).Should().ContainInOrder(@"P:\@Y", @"P:\@X", @"P:\@Z");
    }

    [Fact]
    public void Apply_adds_preset_mods_missing_from_current_list()
    {
        var current = new List<ModEntry> { M(@"P:\@A") };
        var preset = P((@"P:\@New", "server"), (@"P:\@A", "both"));

        var r = ModPresetStore.Apply(current, preset);

        r.Should().HaveCount(2);
        r[0].Path.Should().Be(@"P:\@New");
        r[0].Enabled.Should().BeTrue();
        r[0].Side.Should().Be("server");
    }

    [Fact]
    public void Apply_matches_paths_case_insensitively_and_ignores_trailing_separators()
    {
        var current = new List<ModEntry> { M(@"p:\@cf\") };
        var preset = P((@"P:\@CF", "client"));

        var r = ModPresetStore.Apply(current, preset);

        r.Should().HaveCount(1);
        r[0].Path.Should().Be(@"p:\@cf\");     // existing entry's stored path wins
        r[0].Enabled.Should().BeTrue();
        r[0].Side.Should().Be("client");       // side comes from the preset
    }

    [Fact]
    public void Apply_empty_preset_disables_everything()
    {
        var current = new List<ModEntry> { M(@"P:\@A", enabled: true), M(@"P:\@B", enabled: true) };

        var r = ModPresetStore.Apply(current, new ModPreset { Name = "empty" });

        r.Should().OnlyContain(m => !m.Enabled);
        r.Should().HaveCount(2);
    }

    // --- FromMods / IsModified (pure) --------------------------------------

    [Fact]
    public void FromMods_snapshots_only_enabled_mods_in_order()
    {
        var mods = new List<ModEntry>
        {
            M(@"P:\@A", enabled: true, side: "server"), M(@"P:\@B"), M(@"P:\@C", enabled: true),
        };

        var p = ModPresetStore.FromMods("loadout", mods);

        p.Name.Should().Be("loadout");
        p.Mods.Select(m => m.Path).Should().ContainInOrder(@"P:\@A", @"P:\@C");
        p.Mods[0].Side.Should().Be("server");
    }

    [Fact]
    public void IsModified_false_right_after_apply_true_after_any_loadout_change()
    {
        var preset = P((@"P:\@A", "both"), (@"P:\@B", "server"));
        var applied = ModPresetStore.Apply(new List<ModEntry> { M(@"P:\@C") }, preset);

        ModPresetStore.IsModified(applied, preset).Should().BeFalse();

        var extraEnabled = applied.Select((m, i) => i == 2 ? m with { Enabled = true } : m).ToList();
        ModPresetStore.IsModified(extraEnabled, preset).Should().BeTrue();

        var sideChanged = applied.Select((m, i) => i == 1 ? m with { Side = "client" } : m).ToList();
        ModPresetStore.IsModified(sideChanged, preset).Should().BeTrue();

        var reordered = new List<ModEntry> { applied[1], applied[0], applied[2] };
        ModPresetStore.IsModified(reordered, preset).Should().BeTrue();
    }

    // --- Name validation ----------------------------------------------------

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("..", false)]
    [InlineData("a<b", false)]
    [InlineData("vanilla-plus", true)]
    [InlineData("PvP loadout 2", true)]
    public void IsValidName_accepts_filenames_rejects_garbage(string? name, bool ok) =>
        ModPresetStore.IsValidName(name).Should().Be(ok);

    // --- File I/O (temp config, same pattern as ConfigSplitTests) ----------

    private static string TempConfig(out string root)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "config.json");
        root = Path.Combine(dir, "projects");
        GlobalStore.Save(new GlobalConfig { ProjectsRoot = root }, path);
        return path;
    }

    [Fact]
    public void Save_Load_round_trips_snake_case()
    {
        var cfgPath = TempConfig(out var root);
        var p = P((@"P:\@CF", "both"), (@"P:\@VPP", "server")) with { Name = "vanilla-plus" };

        var (ok, _) = ModPresetStore.Save(p, cfgPath);

        ok.Should().BeTrue();
        var file = Path.Combine(root, "mod-presets", "vanilla-plus.json");
        File.Exists(file).Should().BeTrue();
        File.ReadAllText(file).Should().Contain("\"mods\"").And.Contain("\"side\"");

        var loaded = ModPresetStore.Load("vanilla-plus", cfgPath);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("vanilla-plus");
        loaded.Mods.Should().BeEquivalentTo(p.Mods, o => o.WithStrictOrdering());
    }

    [Fact]
    public void List_returns_sorted_names_Delete_removes()
    {
        var cfgPath = TempConfig(out _);
        ModPresetStore.Save(P((@"P:\@A", "both")) with { Name = "zulu" }, cfgPath);
        ModPresetStore.Save(P((@"P:\@B", "both")) with { Name = "alpha" }, cfgPath);

        ModPresetStore.List(cfgPath).Select(p => p.Name).Should().ContainInOrder("alpha", "zulu");

        ModPresetStore.Delete("zulu", cfgPath).Should().BeTrue();
        ModPresetStore.Delete("zulu", cfgPath).Should().BeFalse();
        ModPresetStore.List(cfgPath).Should().ContainSingle(p => p.Name == "alpha");
    }

    [Fact]
    public void Save_rejects_invalid_name_Load_missing_returns_null()
    {
        var cfgPath = TempConfig(out _);
        ModPresetStore.Save(new ModPreset { Name = "a<b" }, cfgPath).Ok.Should().BeFalse();
        ModPresetStore.Load("nope", cfgPath).Should().BeNull();
    }

    [Fact]
    public void Load_is_case_insensitive_and_returns_canonical_on_disk_name()
    {
        var cfgPath = TempConfig(out _);
        ModPresetStore.Save(P((@"P:\@A", "both")) with { Name = "vanilla-plus" }, cfgPath);

        var loaded = ModPresetStore.Load("VANILLA-PLUS", cfgPath);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("vanilla-plus");
    }
}
