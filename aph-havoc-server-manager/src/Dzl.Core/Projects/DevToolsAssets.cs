using Dzl.Core.Config;

namespace Dzl.Core.Projects;

public sealed record DevToolsDeployResult(bool Ok, string ModDir, string Message);

/// <summary>Deploys the bundled <b>DzlDevTools</b> mod (COM-derived dev tools) from the app package
/// into the user's workspace — editable source under <c>mods\</c> and the prebuilt (unsigned,
/// unbinarized) PBO under <c>build\</c>, surfaced on P: via the usual junctions. Opt-in: the user
/// triggers it from My Mods; it never auto-runs and never edits any instance loadout.</summary>
public static class DevToolsAssets
{
    public const string ModName = "DzlDevTools";
    private const string PboName = "DzlDevTools.pbo";

    /// <summary>The bundled <c>assets\DzlDevTools</c> folder shipped in the app package. Resolves from
    /// the app base dir (installed) or by walking up to the repo root (dev run); null if not found.</summary>
    public static string? BundleDir()
    {
        foreach (var baseDir in Candidates())
        {
            var dir = Path.Combine(baseDir, "assets", ModName);
            if (File.Exists(Path.Combine(dir, "build", PboName))) return dir;
        }
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        yield return AppContext.BaseDirectory;
        // Dev run: bin\Debug\net8.0-windows -> walk up to the repo root that holds assets\.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && d is not null; i++, d = d.Parent)
            yield return d.FullName;
    }

    /// <summary>Copy source (only when absent — never clobber user edits) + the prebuilt PBO (always
    /// refreshed to the shipped version) into the projects root, and ensure the P: junctions so the
    /// mod loads and can be rebuilt. Never throws.</summary>
    public static DevToolsDeployResult Deploy(DzlConfig cfg, string? bundleOverride = null)
    {
        var bundle = bundleOverride ?? BundleDir();
        if (bundle is null)
            return new(false, "", "bundled APH Havoc Dev Tools not found in the app package");

        try
        {
            var root = ProjectPaths.Root(cfg);
            var modDir = ProjectPaths.ModDir(root, ModName);

            // Source: deploy once; a re-import must not overwrite the user's own changes.
            var srcCopied = false;
            if (!Directory.Exists(modDir))
            {
                CopyTree(Path.Combine(bundle, "source"), modDir);
                srcCopied = true;
            }

            // Prebuilt PBO: always refresh to the shipped build.
            var addons = ProjectPaths.BuildAddonsDir(root, ModName);
            Directory.CreateDirectory(addons);
            File.Copy(Path.Combine(bundle, "build", PboName), Path.Combine(addons, PboName), overwrite: true);
            File.WriteAllText(ProjectPaths.BuildMarkerPath(root, ModName), "aph-havoc-bundled");

            // Junctions: source (P:\DzlDevTools, for rebuild) + build area (P:\Mods\@DzlDevTools).
            var anchor = Env.EnvDetect.WorkDriveSource(cfg.WorkDriveSource, cfg.DayzToolsPath);
            Junction.Ensure(ProjectPaths.JunctionPath(anchor, ModName), modDir);
            Junction.Ensure(ProjectPaths.BuildAreaJunction(anchor), ProjectPaths.BuildRoot(root));

            var what = srcCopied ? "source + PBO installed" : "PBO refreshed (source kept — your edits are intact)";
            return new(true, modDir, $"APH Havoc Dev Tools: {what}. Enable the bundled client helper in a mod loadout to use it.");
        }
        catch (Exception ex) { return new(false, "", ex.Message); }
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dst));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(src, dst), overwrite: true);
    }
}
