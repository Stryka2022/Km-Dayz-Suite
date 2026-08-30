using System.Text.RegularExpressions;

namespace Dzl.Core.Servers;

/// <summary>The commonly managed settings in one instance's own serverDZ.cfg.</summary>
public sealed record DayzServerSettings
{
    public string Hostname { get; init; } = "DayZ Server";
    public string Password { get; init; } = "";
    public string PasswordAdmin { get; init; } = "";
    public string Motd { get; init; } = "";
    public int MotdInterval { get; init; } = 5;
    public int MaxPlayers { get; init; } = 60;
    public bool EnableWhitelist { get; init; }
    public int VerifySignatures { get; init; } = 2;
    public bool ForceSameBuild { get; init; } = true;
    public bool DisableVoN { get; init; }
    public int VonCodecQuality { get; init; } = 20;
    public bool DisableThirdPerson { get; init; }
    public bool DisableCrosshair { get; init; }
    public string ServerTime { get; init; } = "SystemTime";
    public double ServerTimeAcceleration { get; init; } = 1;
    public double ServerNightTimeAcceleration { get; init; } = 1;
    public bool ServerTimePersistent { get; init; }
    public int LoginQueueConcurrentPlayers { get; init; } = 5;
    public int LoginQueueMaxPlayers { get; init; } = 500;
    public int InstanceId { get; init; } = 1;
    public bool StorageAutoFix { get; init; } = true;
    public int DefaultVisibility { get; init; } = 1375;
    public int DefaultObjectViewDistance { get; init; } = 1375;
    public bool EnableCfgGameplayFile { get; init; }
    public int LightingConfig { get; init; }
    public bool DisablePersonalLight { get; init; } = true;
    public int PingWarning { get; init; } = 200;
    public int PingCritical { get; init; } = 250;
    public int MaxPing { get; init; } = 300;
    public int ServerFpsWarning { get; init; } = 15;
    public bool AllowFilePatching { get; init; }
}

/// <summary>
/// Reads and updates the familiar top-level serverDZ.cfg assignments while preserving comments,
/// unknown settings, and the mission class block. A backup is kept next to the config before save.
/// </summary>
public static class DayzServerConfig
{
    public static DayzServerSettings Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("serverDZ.cfg was not found", path);
        var text = File.ReadAllText(path);
        return new DayzServerSettings
        {
            Hostname = StringValue(text, "hostname", "DayZ Server"),
            Password = StringValue(text, "password", ""),
            PasswordAdmin = StringValue(text, "passwordAdmin", ""),
            Motd = MotdValue(text),
            MotdInterval = IntValue(text, "motdInterval", 5),
            MaxPlayers = IntValue(text, "maxPlayers", 60),
            EnableWhitelist = BoolValue(text, "enableWhitelist"),
            VerifySignatures = IntValue(text, "verifySignatures", 2),
            ForceSameBuild = BoolValue(text, "forceSameBuild", true),
            DisableVoN = BoolValue(text, "disableVoN"),
            VonCodecQuality = IntValue(text, "vonCodecQuality", 20),
            DisableThirdPerson = BoolValue(text, "disable3rdPerson"),
            DisableCrosshair = BoolValue(text, "disableCrosshair"),
            ServerTime = StringValue(text, "serverTime", "SystemTime"),
            ServerTimeAcceleration = DoubleValue(text, "serverTimeAcceleration", 1),
            ServerNightTimeAcceleration = DoubleValue(text, "serverNightTimeAcceleration", 1),
            ServerTimePersistent = BoolValue(text, "serverTimePersistent"),
            LoginQueueConcurrentPlayers = IntValue(text, "loginQueueConcurrentPlayers", 5),
            LoginQueueMaxPlayers = IntValue(text, "loginQueueMaxPlayers", 500),
            InstanceId = IntValue(text, "instanceId", 1),
            StorageAutoFix = BoolValue(text, "storageAutoFix", true),
            DefaultVisibility = IntValue(text, "defaultVisibility", 1375),
            DefaultObjectViewDistance = IntValue(text, "defaultObjectViewDistance", 1375),
            EnableCfgGameplayFile = BoolValue(text, "enableCfgGameplayFile"),
            LightingConfig = IntValue(text, "lightingConfig", 0),
            DisablePersonalLight = BoolValue(text, "disablePersonalLight", true),
            PingWarning = IntValue(text, "pingWarning", 200),
            PingCritical = IntValue(text, "pingCritical", 250),
            MaxPing = IntValue(text, "MaxPing", 300),
            ServerFpsWarning = IntValue(text, "serverFpsWarning", 15),
            AllowFilePatching = BoolValue(text, "allowFilePatching")
        };
    }

    public static void Save(string path, DayzServerSettings value)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("serverDZ.cfg was not found", path);
        var text = File.ReadAllText(path);
        text = UpsertString(text, "hostname", value.Hostname);
        text = UpsertString(text, "password", value.Password);
        text = UpsertString(text, "passwordAdmin", value.PasswordAdmin);
        text = UpsertMotd(text, value.Motd);
        text = Upsert(text, "motdInterval", value.MotdInterval.ToString());
        text = Upsert(text, "maxPlayers", value.MaxPlayers.ToString());
        text = UpsertBool(text, "enableWhitelist", value.EnableWhitelist);
        text = Upsert(text, "verifySignatures", value.VerifySignatures.ToString());
        text = UpsertBool(text, "forceSameBuild", value.ForceSameBuild);
        text = UpsertBool(text, "disableVoN", value.DisableVoN);
        text = Upsert(text, "vonCodecQuality", value.VonCodecQuality.ToString());
        text = UpsertBool(text, "disable3rdPerson", value.DisableThirdPerson);
        text = UpsertBool(text, "disableCrosshair", value.DisableCrosshair);
        text = UpsertString(text, "serverTime", value.ServerTime);
        text = Upsert(text, "serverTimeAcceleration", Invariant(value.ServerTimeAcceleration));
        text = Upsert(text, "serverNightTimeAcceleration", Invariant(value.ServerNightTimeAcceleration));
        text = UpsertBool(text, "serverTimePersistent", value.ServerTimePersistent);
        text = Upsert(text, "loginQueueConcurrentPlayers", value.LoginQueueConcurrentPlayers.ToString());
        text = Upsert(text, "loginQueueMaxPlayers", value.LoginQueueMaxPlayers.ToString());
        text = Upsert(text, "instanceId", value.InstanceId.ToString());
        text = UpsertBool(text, "storageAutoFix", value.StorageAutoFix);
        text = Upsert(text, "defaultVisibility", value.DefaultVisibility.ToString());
        text = Upsert(text, "defaultObjectViewDistance", value.DefaultObjectViewDistance.ToString());
        text = UpsertBool(text, "enableCfgGameplayFile", value.EnableCfgGameplayFile);
        text = Upsert(text, "lightingConfig", value.LightingConfig.ToString());
        text = UpsertBool(text, "disablePersonalLight", value.DisablePersonalLight);
        text = Upsert(text, "pingWarning", value.PingWarning.ToString());
        text = Upsert(text, "pingCritical", value.PingCritical.ToString());
        text = Upsert(text, "MaxPing", value.MaxPing.ToString());
        text = Upsert(text, "serverFpsWarning", value.ServerFpsWarning.ToString());
        text = UpsertBool(text, "allowFilePatching", value.AllowFilePatching);

        File.Copy(path, path + ".km-backup", overwrite: true);
        File.WriteAllText(path, text);
    }

    private static string StringValue(string text, string name, string fallback)
    {
        var m = Regex.Match(text, "(?im)^\\s*" + Regex.Escape(name) +
            "\\s*=\\s*\"(?<v>(?:\\\\.|[^\"\\\\])*)\"\\s*;");
        return m.Success ? Regex.Unescape(m.Groups["v"].Value) : fallback;
    }

    private static int IntValue(string text, string name, int fallback)
    {
        var m = Assignment(text, name);
        return m.Success && int.TryParse(m.Groups["v"].Value.Trim(), out var value) ? value : fallback;
    }

    private static double DoubleValue(string text, string name, double fallback)
    {
        var m = Assignment(text, name);
        return m.Success && double.TryParse(m.Groups["v"].Value.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static bool BoolValue(string text, string name, bool fallback = false)
    {
        var m = Assignment(text, name);
        if (!m.Success) return fallback;
        var raw = m.Groups["v"].Value.Trim();
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1";
    }

    private static Match Assignment(string text, string name) =>
        Regex.Match(text, $@"(?im)^\s*{Regex.Escape(name)}\s*=\s*(?<v>[^;\r\n]+)\s*;");

    private static string MotdValue(string text)
    {
        var m = Regex.Match(text, @"(?is)\bmotd\s*\[\s*\]\s*=\s*\{(?<v>.*?)\}\s*;");
        if (!m.Success) return "";
        return string.Join(Environment.NewLine, Regex.Matches(m.Groups["v"].Value,
                "\"(?<v>(?:\\\\.|[^\"\\\\])*)\"")
            .Select(x => Regex.Unescape(x.Groups["v"].Value)));
    }

    private static string UpsertString(string text, string name, string value) =>
        Upsert(text, name, $"\"{Escape(value)}\"");

    private static string UpsertBool(string text, string name, bool value) =>
        Upsert(text, name, value ? "1" : "0");

    private static string Upsert(string text, string name, string value)
    {
        var rx = new Regex($@"(?im)^(?<indent>\s*){Regex.Escape(name)}\s*=\s*[^;\r\n]+\s*;");
        var replacement = $"${{indent}}{name} = {value};";
        if (rx.IsMatch(text)) return rx.Replace(text, replacement, 1);
        return InsertBeforeMissions(text, $"{name} = {value};{Environment.NewLine}");
    }

    private static string UpsertMotd(string text, string motd)
    {
        var lines = motd.Replace("\r\n", "\n").Split('\n')
            .Where(line => line.Length > 0).Select(line => $"    \"{Escape(line)}\"");
        var value = $"motd[] = {{{Environment.NewLine}{string.Join("," + Environment.NewLine, lines)}{Environment.NewLine}}};";
        var rx = new Regex(@"(?is)\bmotd\s*\[\s*\]\s*=\s*\{.*?\}\s*;");
        return rx.IsMatch(text) ? rx.Replace(text, value, 1) : InsertBeforeMissions(text, value + Environment.NewLine);
    }

    private static string InsertBeforeMissions(string text, string line)
    {
        var m = Regex.Match(text, @"(?im)^\s*class\s+Missions\b");
        return m.Success ? text.Insert(m.Index, line + Environment.NewLine) : text.TrimEnd() + Environment.NewLine + line;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Invariant(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
