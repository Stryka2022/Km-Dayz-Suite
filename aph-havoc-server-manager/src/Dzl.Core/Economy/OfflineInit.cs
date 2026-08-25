using Dzl.Core.Config;

namespace Dzl.Core.Economy;

public enum OfflineInitStatus { NoMission, NeedsPatch, Patched }

/// <summary>State of the active offline instance's mission <c>init.c</c> — whether it carries the dzl
/// offline bootstrap (a client-side <see cref="Marker"/> mission that spawns a character with a NULL
/// identity). A vanilla <c>MissionServer</c> mission never spawns a character offline, so the client
/// hangs on load; this makes that visible + one-click fixable.</summary>
/// <param name="Patchable">True when there is an offline instance with a readable init.c that isn't
/// already patched (i.e. the "Fix" button would change something).</param>
public sealed record OfflineInitResult(OfflineInitStatus Status, string InitPath, string Message, bool Patchable);

/// <summary>Detects + injects the dzl offline bootstrap into a mission's <c>init.c</c>. Pure detection
/// split from thin, never-throwing file I/O (backup + wrap-the-original transform). The transform wraps
/// whatever <c>CreateCustomMission</c> the mission already has, so it works on ANY mission/map.</summary>
public static class OfflineInit
{
    /// <summary>Class name injected into init.c — also the detection marker.</summary>
    public const string Marker = "DzlOfflineMission";
    private const string OrigFn = "CreateCustomMission_dzlOrig";
    private const string BackupSuffix = ".dzl-backup";

    /// <summary>The injected block. <see cref="Transform"/> appends this after renaming the mission's
    /// own <c>CreateCustomMission</c> to <see cref="OrigFn"/>. Public so the UI can offer a copy-to-
    /// clipboard fallback (hand-edit).</summary>
    public const string Snippet =
@"// --- dzl offline bootstrap -------------------------------------------------
// A vanilla MissionServer mission spawns a character only from engine connection
// events, which never fire when a diag client boots with -mission and no -connect.
// Offline the game is its own authority (IsMultiplayer()==false), so this
// MissionGameplay mission creates the character itself with a NULL identity.
class DzlOfflineMission : MissionGameplay
{
    override void OnInit()
    {
        super.OnInit();

        vector spawnPos = ""7500 0 7500"";
        spawnPos[1] = GetGame().SurfaceY(spawnPos[0], spawnPos[2]);

        PlayerBase player = PlayerBase.Cast(
            GetGame().CreatePlayer(NULL, GetGame().CreateRandomPlayer(), spawnPos, 0, ""NONE"") );

        GetGame().SelectPlayer(NULL, player);
    }
}

Mission CreateCustomMission(string path)
{
    // A real (hosted/dedicated) server keeps the mission's own logic; a lone
    // offline game gets the client mission that spawns its own character.
    if ( GetGame().IsServer() && GetGame().IsMultiplayer() )
        return CreateCustomMission_dzlOrig(path);

    return new DzlOfflineMission();
}
";

    private static string? InitPathOf(DzlConfig cfg)
    {
        var mission = MissionLocator.Resolve(cfg)?.MissionDir;
        return mission is null ? null : Path.Combine(mission, "init.c");
    }

    /// <summary>Evaluated for ANY instance (patch is harmless online — the server branch delegates to
    /// the mission's own logic) since a mission can be launched offline via "Menu only" too.</summary>
    public static OfflineInitResult Check(DzlConfig cfg)
    {
        var init = InitPathOf(cfg);
        if (init is null || !File.Exists(init))
            return new(OfflineInitStatus.NoMission, init ?? "", "no mission init.c found for this instance", false);

        string src;
        try { src = File.ReadAllText(init); }
        catch { return new(OfflineInitStatus.NoMission, init, "could not read the mission init.c", false); }

        if (src.Contains(Marker))
            return new(OfflineInitStatus.Patched, init, "offline bootstrap is installed", false);
        return new(OfflineInitStatus.NeedsPatch, init,
            "vanilla mission won't spawn a character offline — patch init.c to boot into the world", true);
    }

    /// <summary>Pure: rename the mission's own <c>CreateCustomMission</c> to <see cref="OrigFn"/> and
    /// append <see cref="Snippet"/>. Returns null if there's nothing to rewrite or it's already patched.</summary>
    public static string? Transform(string src)
    {
        if (src.Contains(Marker)) return null;
        const string fn = "Mission CreateCustomMission(";
        var i = src.IndexOf(fn, StringComparison.Ordinal);
        if (i < 0) return null;
        var renamed = src.Remove(i, fn.Length).Insert(i, $"Mission {OrigFn}(");
        var sep = renamed.EndsWith("\n") ? "\n" : "\n\n";
        return renamed + sep + Snippet;
    }

    /// <summary>Backup the mission init.c (once) then inject the offline bootstrap. Never throws.</summary>
    public static (bool Ok, string Message) Patch(DzlConfig cfg)
    {
        var check = Check(cfg);
        if (!check.Patchable) return (false, check.Message);

        try
        {
            var init = check.InitPath;
            var patched = Transform(File.ReadAllText(init));
            if (patched is null) return (false, "init.c has no CreateCustomMission to wrap");

            var backup = init + BackupSuffix;
            if (!File.Exists(backup)) File.Copy(init, backup);
            File.WriteAllText(init, patched);
            return (true, $"offline bootstrap installed (original backed up to init.c{BackupSuffix})");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
