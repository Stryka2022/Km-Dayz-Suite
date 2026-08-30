using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Remote;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    private bool _workshopAutomationBusy;
    private DateTime _nextWorkshopAutomationCheck = DateTime.UtcNow.AddSeconds(20);
    private DateTime _lastLogNotification = DateTime.MinValue;
    private string _lastLogNotificationFingerprint = "";

    /// <summary>Turns important new live-log lines into throttled, per-instance Discord events.</summary>
    private void ObserveLogBatch(string paneName, IReadOnlyList<string> batch)
    {
        var flagged = batch.Where(line =>
                ContainsAny(line, "error", "exception", "fatal", "crash", "kicked", "banned", "admin", "rcon"))
            .TakeLast(5)
            .Select(line => line.Length <= 300 ? line : line[..300])
            .ToList();
        if (flagged.Count == 0) return;

        var fingerprint = string.Join("\n", flagged);
        if (fingerprint == _lastLogNotificationFingerprint ||
            DateTime.UtcNow - _lastLogNotification < TimeSpan.FromSeconds(60)) return;
        _lastLogNotificationFingerprint = fingerprint;
        _lastLogNotification = DateTime.UtcNow;

        var admin = flagged.Any(line => ContainsAny(line, "kicked", "banned", "admin", "rcon"));
        _ = NotifyDiscordAsync(
            admin ? DiscordNotificationCategory.AdminActivity : DiscordNotificationCategory.LogAlerts,
            admin ? $"Admin activity · {paneName}" : $"Log alert · {paneName}",
            "```\n" + string.Join("\n", flagged).Replace("```", "'''", StringComparison.Ordinal) + "\n```");
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>The status timer drives metadata checks and minute-by-minute warning countdowns.</summary>
    private void ScheduleWorkshopAutomation()
    {
        if (_disposed || _workshopAutomationBusy || DateTime.UtcNow < _nextWorkshopAutomationCheck) return;
        var configs = Profiles.List(_configPath)
            .Select(name => { try { return Profiles.Load(name, _configPath); } catch { return null; } })
            .Where(c => c is not null && (c.AutoUpdateWorkshopMods || c.AutoCopyWorkshopKeys))
            .Cast<DzlConfig>().ToList();
        var warningActive = configs.Any(c => c.WorkshopWarningDeadlineUtc > 0);
        var pending = configs.Any(c => c.WorkshopPendingUpdates.Count > 0);
        var interval = configs.Count == 0 ? 30 : configs.Min(c => Math.Clamp(c.WorkshopUpdateIntervalMinutes, 5, 1440));
        _nextWorkshopAutomationCheck = DateTime.UtcNow.Add(
            warningActive ? TimeSpan.FromSeconds(20) : pending ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(interval));
        _ = CheckWorkshopUpdatesAcrossInstancesAsync(manual: false);
    }

    public async Task<string> InstallWorkshopOnActiveServerAsync(string id)
    {
        var instance = string.IsNullOrWhiteSpace(ActivePreset) ? "default" : ActivePreset;
        var source = new WorkshopService(_configPath).ResolveContentDir(id);
        if (string.IsNullOrWhiteSpace(source))
        {
            WorkshopStatus = $"✗ Workshop {id} is not downloaded yet — subscribe or download it first";
            return WorkshopStatus;
        }

        var cfg = Profiles.Load(instance, _configPath);
        var result = await Task.Run(() => WorkshopInstanceService.EnableForInstance(
            _configPath, instance, id, source, cfg.AutoCopyWorkshopKeys));
        WorkshopStatus = (result.Ok ? "✓ " : "✗ ") + result.Message;
        if (result.Ok)
        {
            Reload();
            RefreshWorkshopDetailState();
            await NotifyDiscordAsync(DiscordNotificationCategory.AdminActivity, "Workshop mod enabled",
                $"Workshop item `{id}` was enabled for **{instance}**.", instance);
        }
        return WorkshopStatus;
    }

    public async Task<string> UninstallWorkshopFromActiveServerAsync(string id)
    {
        var instance = string.IsNullOrWhiteSpace(ActivePreset) ? "default" : ActivePreset;
        var result = await Task.Run(() => WorkshopInstanceService.DisableForInstance(
            _configPath, instance, id));
        WorkshopStatus = (result.Ok ? "✓ " : "✗ ") + result.Message;
        if (result.Ok)
        {
            Reload();
            RefreshWorkshopDetailState();
            await NotifyDiscordAsync(DiscordNotificationCategory.AdminActivity, "Workshop mod uninstalled",
                $"Workshop item `{id}` was removed from the **{instance}** loadout. Shared downloaded files were kept.",
                instance);
        }
        return WorkshopStatus;
    }

    public async Task<string> CheckWorkshopUpdatesAcrossInstancesAsync(bool manual)
    {
        if (_workshopAutomationBusy) return "Workshop update check is already running.";
        _workshopAutomationBusy = true;
        try
        {
            var service = new WorkshopService(_configPath);
            var details = new Dictionary<string, Dzl.Core.Workshop.WorkshopItem?>();
            var downloadResults = new Dictionary<string, OpResult>(StringComparer.Ordinal);
            var checkedInstances = 0;
            var changes = 0;
            var keyCopies = 0;
            var deployed = 0;

            foreach (var instance in Profiles.List(_configPath))
            {
                DzlConfig cfg;
                try { cfg = Profiles.Load(instance, _configPath); }
                catch { continue; }
                if (!manual && !cfg.AutoUpdateWorkshopMods && !cfg.AutoCopyWorkshopKeys) continue;
                var ids = WorkshopInstanceService.ConfiguredWorkshopIds(cfg);
                if (ids.Count == 0) continue;
                checkedInstances++;

                var known = new Dictionary<string, long>(cfg.WorkshopKnownUpdates, StringComparer.Ordinal);
                var pending = new Dictionary<string, long>(cfg.WorkshopPendingUpdates, StringComparer.Ordinal);
                var titles = new Dictionary<string, string>(cfg.WorkshopPendingTitles, StringComparer.Ordinal);
                var instanceChanges = new List<string>();

                foreach (var id in ids)
                {
                    if (!details.TryGetValue(id, out var detail))
                    {
                        detail = await service.DetailsAsync(id);
                        details[id] = detail;
                    }
                    if (detail is null) continue;
                    known.TryGetValue(id, out var previous);
                    if (previous == 0)
                    {
                        // First observation is a baseline, not a false update alert.
                        if (detail.Updated > 0) known[id] = detail.Updated;
                    }
                    else if (detail.Updated > previous)
                    {
                        pending.TryGetValue(id, out var alreadyQueued);
                        if (detail.Updated > alreadyQueued)
                        {
                            changes++;
                            instanceChanges.Add($"{detail.Title} (`{id}`) updated {detail.UpdatedText}");
                        }
                        if (cfg.AutoUpdateWorkshopMods)
                        {
                            pending[id] = detail.Updated;
                            titles[id] = detail.Title;
                        }
                        else
                        {
                            // Detection-only instances acknowledge after reporting, preventing repeated alerts.
                            known[id] = detail.Updated;
                        }
                    }

                    if (cfg.AutoCopyWorkshopKeys && service.ResolveContentDir(id) is { } source)
                        keyCopies += WorkshopInstanceService.CopyPublicKeys(
                            source, Profiles.InstanceDir(instance, _configPath));
                }

                cfg = cfg with
                {
                    WorkshopKnownUpdates = known,
                    WorkshopPendingUpdates = pending,
                    WorkshopPendingTitles = titles
                };
                Profiles.Save(cfg, instance, _configPath);

                if (instanceChanges.Count > 0)
                    await NotifyDiscordAsync(DiscordNotificationCategory.WorkshopUpdates,
                        $"Workshop changes detected · {instance}",
                        string.Join("\n", instanceChanges) +
                        (cfg.AutoUpdateWorkshopMods
                            ? $"\nQueued using policy `{cfg.WorkshopUpdatePolicy}`."
                            : "\nAutomatic deployment is disabled."), instance, cfg);

                if (cfg.AutoUpdateWorkshopMods && pending.Count > 0)
                {
                    var outcome = await ProcessPendingWorkshopUpdatesAsync(
                        instance, cfg, service, downloadResults, _cts.Token);
                    deployed += outcome.deployed;
                    keyCopies += outcome.keyCopies;
                }
            }

            var result = $"checked {checkedInstances} instance(s) · {changes} changed mod(s) · " +
                         $"{deployed} deployment(s) · {keyCopies} key file(s) refreshed";
            if (manual) WorkshopStatus = "✓ " + result;
            return result;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return "Workshop update check cancelled while Server Manager was closing.";
        }
        catch (Exception ex)
        {
            var result = "Workshop update check failed: " + ex.Message;
            if (manual) WorkshopStatus = "✗ " + result;
            return result;
        }
        finally { _workshopAutomationBusy = false; }
    }

    private async Task<(int deployed, int keyCopies)> ProcessPendingWorkshopUpdatesAsync(
        string instance, DzlConfig cfg, WorkshopService service,
        Dictionary<string, OpResult> downloadResults, CancellationToken cancellationToken)
    {
        var pending = new Dictionary<string, long>(cfg.WorkshopPendingUpdates, StringComparer.Ordinal);
        var titles = new Dictionary<string, string>(cfg.WorkshopPendingTitles, StringComparer.Ordinal);
        var known = new Dictionary<string, long>(cfg.WorkshopKnownUpdates, StringComparer.Ordinal);
        var deadline = cfg.WorkshopWarningDeadlineUtc;
        var warningsSent = cfg.WorkshopWarningMinutesSent.ToHashSet();
        var activeAndRunning = string.Equals(instance, ActiveName, StringComparison.OrdinalIgnoreCase) && ServerUp;
        var remote = FindRemoteProfile(instance);
        var policy = string.Equals(cfg.WorkshopUpdatePolicy, "warn-15", StringComparison.OrdinalIgnoreCase)
            ? "warn-15" : "when-empty";
        var changedNames = pending.Keys.Select(id => titles.GetValueOrDefault(id, id)).ToList();

        if (policy == "when-empty")
        {
            if (remote is not null)
            {
                var players = await QueryPlayersAsync(remote, cancellationToken);
                if (!players.ok || players.count is null || players.count.Value > 0)
                {
                    Profiles.Save(cfg with
                    {
                        WorkshopPendingUpdates = pending,
                        WorkshopPendingTitles = titles
                    }, instance, _configPath);
                    return (0, 0);
                }
            }
            else if (activeAndRunning)
            {
                // A running server cannot be proven empty without its linked RCon endpoint.
                return (0, 0);
            }
        }
        else if (activeAndRunning || remote is not null)
        {
            if (remote is null) return (0, 0); // In-game countdown requires a linked RCon profile.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (deadline <= 0)
            {
                deadline = now + 15 * 60;
                warningsSent.Clear();
            }
            var remaining = Math.Max(0, (int)Math.Ceiling((deadline - now) / 60d));
            var threshold = new[] { 15, 10, 5, 1 }.FirstOrDefault(value => remaining == value && !warningsSent.Contains(value));
            if (threshold > 0)
            {
                var message = $"Workshop update in {threshold} minute{(threshold == 1 ? "" : "s")}: " +
                              string.Join(", ", changedNames);
                if (await BroadcastAsync(remote, message, cancellationToken)) warningsSent.Add(threshold);
            }
            cfg = cfg with
            {
                WorkshopWarningDeadlineUtc = deadline,
                WorkshopWarningMinutesSent = warningsSent.OrderDescending().ToList(),
                WorkshopPendingUpdates = pending,
                WorkshopPendingTitles = titles
            };
            Profiles.Save(cfg, instance, _configPath);
            if (now < deadline) return (0, 0);
        }

        var restartLocal = activeAndRunning;
        if (restartLocal)
        {
            var stopped = _svc.StopTarget("server", "workshop-auto-update");
            if (!stopped.Ok) return (0, 0);
            await Task.Delay(1500, cancellationToken);
        }

        var deployed = 0;
        var keyCopies = 0;
        var failures = new List<string>();
        foreach (var (id, updated) in pending.ToList())
        {
            if (!downloadResults.TryGetValue(id, out var download))
            {
                download = await service.DownloadAndWaitAsync(id, cancellationToken);
                downloadResults[id] = download;
            }
            if (!download.Ok)
            {
                failures.Add($"{titles.GetValueOrDefault(id, id)}: {download.Message}");
                continue;
            }

            var source = service.ResolveContentDir(id);
            if (source is null)
            {
                failures.Add($"{titles.GetValueOrDefault(id, id)}: downloaded content folder was not found");
                continue;
            }
            var enabled = WorkshopInstanceService.EnableForInstance(
                _configPath, instance, id, source, copyKeys: false);
            if (!enabled.Ok)
            {
                failures.Add($"{titles.GetValueOrDefault(id, id)}: {enabled.Message}");
                continue;
            }
            if (cfg.AutoCopyWorkshopKeys)
                keyCopies += WorkshopInstanceService.CopyPublicKeys(source, Profiles.InstanceDir(instance, _configPath));
            known[id] = updated;
            pending.Remove(id);
            titles.Remove(id);
            deployed++;
        }

        // EnableForInstance may have repointed the instance from Steam-client content to the
        // completed steamcmd download. Reload before saving automation state so that mod path is retained.
        var latestCfg = Profiles.Load(instance, _configPath);
        var retryDeadline = pending.Count > 0 && policy == "warn-15"
            ? DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds()
            : 0;
        cfg = latestCfg with
        {
            WorkshopKnownUpdates = known,
            WorkshopPendingUpdates = pending,
            WorkshopPendingTitles = titles,
            // A failed or partial deployment receives a fresh warning window. This prevents
            // retrying SteamCMD every scheduler tick after the original deadline has passed.
            WorkshopWarningDeadlineUtc = retryDeadline,
            WorkshopWarningMinutesSent = new()
        };
        Profiles.Save(cfg, instance, _configPath);

        if (restartLocal)
        {
            var started = _svc.StartTarget("server", cfg.Mode, "workshop-auto-update");
            await NotifyDiscordAsync(DiscordNotificationCategory.ServerLifecycle,
                "Server restarted after Workshop update",
                $"**{instance}**: {started.Message}", instance, cfg);
        }

        if (deployed > 0)
            await NotifyDiscordAsync(DiscordNotificationCategory.WorkshopUpdates,
                $"Workshop update deployed · {instance}",
                $"Updated {deployed} mod(s): {string.Join(", ", changedNames)}. " +
                $"Public keys refreshed: {keyCopies}." +
                (restartLocal ? " The server was restarted." : ""), instance, cfg);
        if (failures.Count > 0)
            await NotifyDiscordAsync(DiscordNotificationCategory.LogAlerts,
                $"Workshop deployment needs attention · {instance}", string.Join("\n", failures), instance, cfg);
        return (deployed, keyCopies);
    }

    private RemoteServerProfile? FindRemoteProfile(string instance) =>
        RemoteProfileStore.Load(_configPath).FirstOrDefault(profile =>
            string.Equals(profile.InstanceName, instance, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(profile.EffectiveRconHost));

    private async Task<(bool ok, int? count, string error)> QueryPlayersAsync(
        RemoteServerProfile profile, CancellationToken cancellationToken)
    {
        var password = RemoteProfileStore.GetRconPassword(_configPath, profile.Id);
        if (password.Length == 0) return (false, null, "the linked RCon profile has no saved password");
        try
        {
            await using var client = new BattlEyeRconClient();
            await client.ConnectAsync(profile.EffectiveRconHost, profile.RconPort, password, cancellationToken);
            var response = await client.ExecuteAsync("players", cancellationToken);
            var count = BattlEyePlayerParser.ParseCount(response);
            return count is null ? (false, null, "BattlEye did not return a player total") : (true, count, "");
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    private async Task<bool> BroadcastAsync(
        RemoteServerProfile profile, string message, CancellationToken cancellationToken)
    {
        var password = RemoteProfileStore.GetRconPassword(_configPath, profile.Id);
        if (password.Length == 0) return false;
        try
        {
            var ascii = new string(message.Select(ch => ch <= 0x7f ? ch : '?').Take(220).ToArray());
            await using var client = new BattlEyeRconClient();
            await client.ConnectAsync(profile.EffectiveRconHost, profile.RconPort, password, cancellationToken);
            await client.ExecuteAsync("say -1 " + ascii, cancellationToken);
            return true;
        }
        catch { return false; }
    }

    public async Task<OpResult> NotifyDiscordAsync(
        DiscordNotificationCategory category, string title, string message,
        string? instanceName = null, DzlConfig? cfg = null, string? webhookOverride = null)
    {
        var instance = string.IsNullOrWhiteSpace(instanceName)
            ? (string.IsNullOrWhiteSpace(ActivePreset) ? "default" : ActivePreset)
            : instanceName;
        try
        {
            cfg ??= Profiles.Load(instance, _configPath);
            return await DiscordWebhookService.SendAsync(
                _configPath, instance, cfg, category, title, message, webhookOverride);
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }
}
