using System.Collections.Concurrent;
using System.Globalization;
using Data.Models;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Database.Models;
using SharedLibraryCore.Events.Game;
using SharedLibraryCore.Interfaces;

namespace ServerPulse;

public sealed record GuidanceMessageAnalysis(
    bool Matched,
    string? Category,
    string? Pattern,
    string? TargetName,
    string Reason);

public sealed class PlayerGuidanceService : IDisposable
{
    private readonly ServerPulseConfig _rootConfig;
    private readonly IConfigurationHandlerV2<ServerPulseConfig> _configurationHandler;
    private readonly PlayerGuidanceDetectionEngine _detectionEngine;
    private readonly AnalyticsStore _store;
    private readonly ILogger<PlayerGuidanceService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<PlayerCooldownKey, long> _playerCooldowns = new();
    private readonly ConcurrentDictionary<string, long> _serverCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StaffAlertWindow> _staffAlertWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ChatHistoryWindow> _chatHistory = new(StringComparer.OrdinalIgnoreCase);
    private int _messagesSincePrune;
    private bool _disposed;

    public PlayerGuidanceService(
        ServerPulseConfig rootConfig,
        IConfigurationHandlerV2<ServerPulseConfig> configurationHandler,
        PlayerGuidanceDetectionEngine detectionEngine,
        AnalyticsStore store,
        ILogger<PlayerGuidanceService> logger,
        TimeProvider timeProvider)
    {
        _rootConfig = rootConfig;
        _configurationHandler = configurationHandler;
        _detectionEngine = detectionEngine;
        _store = store;
        _logger = logger;
        _timeProvider = timeProvider;
        _configurationHandler.Updated += OnConfigurationUpdated;
        EnsureConfiguration();
        RefreshConfiguration();
    }

    private PlayerGuidanceConfig Config => _rootConfig.PlayerGuidance;
    public IReadOnlyList<GuidanceConfigurationIssue> ConfigurationIssues => _detectionEngine.Issues;

    public void RefreshConfiguration()
    {
        EnsureConfiguration();
        _detectionEngine.Reload(Config);
        foreach (var issue in ConfigurationIssues)
        {
            if (issue.Severity == GuidanceConfigurationIssueSeverity.Error)
                _logger.LogError("[ServerPulse] Player guidance configuration: {Message}", issue.Message);
            else
                _logger.LogWarning("[ServerPulse] Player guidance configuration: {Message}", issue.Message);
        }

        _logger.LogInformation(
            "[ServerPulse] Player guidance refreshed. Enabled={Enabled}, Categories={CategoryCount}, Issues={IssueCount}",
            Config.Enabled,
            Config.Categories.Count(category => category.Enabled),
            ConfigurationIssues.Count);
    }

    private void EnsureConfiguration()
    {
        _rootConfig.PlayerGuidance ??= new PlayerGuidanceConfig();
        Config.Categories ??= PlayerGuidanceConfig.DefaultCategories();
        Config.ReminderMessages ??= PlayerGuidanceConfig.DefaultReminderMessages();
        Config.ExcludedPhrases ??= [];
        Config.CommunityReportPhrases ??= PlayerGuidanceConfig.DefaultCommunityReportPhrases();
        Config.CommunityReportExclusions ??= [];
        Config.ServerOverrides ??= new Dictionary<string, PlayerGuidanceServerOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in Config.Categories)
        {
            category.Phrases ??= [];
            category.RegexPatterns ??= [];
            category.ReminderMessages ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        foreach (var serverOverride in Config.ServerOverrides.Values)
            serverOverride.ExcludedPhrases ??= [];
    }

    public async Task HandleAsync(
        ClientMessageEvent chatEvent,
        string messageId,
        string reporterKey,
        Func<EFClient, string> playerKey,
        PlayerSessionRecord? session,
        string? excerpt,
        CancellationToken token)
    {
        var client = chatEvent.Client;
        var server = client?.CurrentServer;
        var message = chatEvent.Message ?? string.Empty;
        if (client is null || server is null || string.IsNullOrWhiteSpace(message))
            return;

        var settings = EffectiveSettings(server.Id);
        if (!settings.Enabled || settings.ResponseMode == GuidanceResponseMode.Disabled ||
            (Config.IgnoreTeamMessages && chatEvent.IsTeamMessage))
            return;

        var contextMessage = new GuidanceContextMessageRecord
        {
            MessageId = messageId,
            CapturedAt = chatEvent.CreatedAt,
            ClientId = client.ClientId,
            PlayerKey = reporterKey,
            PlayerName = AnalyticsEngine.CleanDisplayText(client.CleanedName),
            Message = TruncateContext(message),
            IsTeamMessage = chatEvent.IsTeamMessage
        };
        RecordContext(server.Id, contextMessage);
        EFClient? target = null;

        if (IsReportCommand(message, settings.ReportCommand))
        {
            target = FindReportTarget(message, settings.ReportCommand, client, server.ConnectedClients, ExcludeBots(server.Id));
            if (Config.TrackReportCommands)
                StoreEvent("Report", "Official report", settings.ReportCommand, "Report command observed", false);
            return;
        }

        var match = _detectionEngine.Detect(message, settings.ExcludedPhrases);
        if (match is null)
            return;

        target = Config.EnableTargetAssistance
            ? FindMentionedTarget(message, match.Pattern, match.Category, client, server.ConnectedClients, ExcludeBots(server.Id))
            : null;

        PruneIfNeeded();
        var outcome = "Signal recorded";
        if (!TryAcquireCooldown(_playerCooldowns, new PlayerCooldownKey(server.Id, client.ClientId), settings.PlayerCooldown))
        {
            outcome = "Reminder suppressed by player cooldown";
        }
        else
        {
            var reminder = FormatReminder(match.Category, client.CleanedName, target?.CleanedName, settings);
            var messages = SplitMessage(reminder, Math.Clamp(Config.MaxMessageLength, 40, 1000));
            var outcomes = new List<string>();

            if (settings.ResponseMode is GuidanceResponseMode.Private or GuidanceResponseMode.Both)
            {
                await client.TellAsync(messages, token);
                outcomes.Add("private reminder sent");
            }

            if (settings.ResponseMode is GuidanceResponseMode.Public or GuidanceResponseMode.Both)
            {
                if (TryAcquireCooldown(_serverCooldowns, server.Id, settings.ServerCooldown))
                {
                    await server.BroadcastAsync(messages, token: token);
                    outcomes.Add("public reminder sent");
                }
                else
                {
                    outcomes.Add("public reminder suppressed by server cooldown");
                }
            }

            if (outcomes.Count > 0)
                outcome = string.Join("; ", outcomes);
        }

        var staffAlertSent = await MaybeNotifyStaffAsync(
            server.ConnectedClients, server.Id, reporterKey, match.Category, target, token);
        StoreEvent("Accusation", match.Category, match.Pattern, outcome, staffAlertSent);

        if (_rootConfig.Debug)
        {
            _logger.LogDebug(
                "[ServerPulse] Player guidance matched {Category}/{Pattern} for {Player} on {Server}; target={Target}; outcome={Outcome}",
                match.Category,
                match.Pattern,
                client.Name,
                server.Id,
                target?.CleanedName ?? "unresolved",
                outcome);
        }

        void StoreEvent(string eventType, string category, string pattern, string eventOutcome, bool alertSent)
        {
            _store.AddPlayerGuidanceEvent(new PlayerGuidanceEventRecord
            {
                MessageId = messageId,
                EventType = eventType,
                ServerId = server.Id,
                ServerName = AnalyticsEngine.CleanDisplayText(server.ServerName),
                LegacyServerId = server.LegacyDatabaseId,
                Game = server.GameCode.ToString(),
                Map = string.IsNullOrWhiteSpace(server.Map?.Alias) ? server.Map?.Name ?? "Unknown" : server.Map.Alias,
                Mode = AnalyticsEngine.FriendlyMode(server.Gametype),
                CountryCode = session?.CountryCode ?? string.Empty,
                CountryName = session?.CountryName ?? "Unknown",
                ReporterKey = reporterKey,
                TargetKey = target is null ? string.Empty : playerKey(target),
                TargetClientId = target?.ClientId,
                TargetNetworkId = target?.NetworkId,
                TargetName = target is null ? string.Empty : AnalyticsEngine.CleanDisplayText(target.CleanedName),
                ResolutionMethod = target is null ? string.Empty : "Automatic unique name match",
                ReviewStatus = target is null ? "Unresolved" : "AutomaticallyResolved",
                Category = category,
                Pattern = pattern,
                Outcome = eventOutcome,
                StaffAlertSent = alertSent,
                CapturedAt = chatEvent.CreatedAt,
                Excerpt = excerpt,
                ContextMessages = Config.RetainAdminReviewContext ? ContextBefore(server.Id, chatEvent.CreatedAt) : [],
                PlayersAtCapture = Config.RetainAdminReviewContext
                    ? server.ConnectedClients.Select(player => new GuidancePlayerSnapshotRecord
                    {
                        ClientId = player.ClientId,
                        NetworkId = player.NetworkId,
                        PlayerKey = playerKey(player),
                        PlayerName = AnalyticsEngine.CleanDisplayText(player.CleanedName),
                        IsBot = player.IsBot
                    }).ToList()
                    : []
            });
        }
    }

    public GuidanceMessageAnalysis AnalyzeMessage(
        string message,
        string serverId,
        EFClient? origin,
        IReadOnlyList<EFClient>? connectedClients)
    {
        var settings = EffectiveSettings(serverId);
        if (!settings.Enabled)
            return new GuidanceMessageAnalysis(false, null, null, null, "Player guidance is disabled for this server.");
        if (IsReportCommand(message, settings.ReportCommand))
            return new GuidanceMessageAnalysis(false, null, null, null, "Message is already an official report command.");

        var match = _detectionEngine.Detect(message, settings.ExcludedPhrases);
        if (match is null)
            return new GuidanceMessageAnalysis(false, null, null, null, "No player-guidance rule matched.");

        var target = origin is not null && connectedClients is not null && Config.EnableTargetAssistance
            ? FindMentionedTarget(message, match.Pattern, match.Category, origin, connectedClients, ExcludeBots(serverId))
            : null;
        return new GuidanceMessageAnalysis(
            true,
            match.Category,
            match.Pattern,
            target?.CleanedName,
            target is null ? "Matched; no unique online target was identified." : "Matched and resolved an online target.");
    }

    public string StatusSummary(DashboardSnapshot snapshot)
    {
        var events = snapshot.PlayerGuidanceEvents;
        var accusations = events.Count(item => item.EventType.Equals("Accusation", StringComparison.OrdinalIgnoreCase));
        var reports = events.Count(item => item.EventType.Equals("Report", StringComparison.OrdinalIgnoreCase));
        var reminders = events.Count(item => item.Outcome.Contains("reminder sent", StringComparison.OrdinalIgnoreCase));
        return $"ServerPulse guidance {(Config.Enabled ? "enabled" : "disabled")} | mode={Config.ResponseMode} | accusations={accusations} | reports={reports} | reminders={reminders} | config issues={ConfigurationIssues.Count}";
    }

    public void RemoveClientCooldowns(int clientId)
    {
        foreach (var key in _playerCooldowns.Keys.Where(key => key.ClientId == clientId))
            _playerCooldowns.TryRemove(key, out _);
    }

    private EffectiveGuidanceSettings EffectiveSettings(string serverId)
    {
        var serverOverride = FindServerOverride(serverId);
        return new EffectiveGuidanceSettings(
            serverOverride?.Enabled ?? Config.Enabled,
            serverOverride?.ResponseMode ?? Config.ResponseMode,
            string.IsNullOrWhiteSpace(serverOverride?.ReportCommand) ? Config.ReportCommand : serverOverride!.ReportCommand!,
            TimeSpan.FromSeconds(Math.Max(0, serverOverride?.PlayerCooldownSeconds ?? Config.PlayerCooldownSeconds)),
            TimeSpan.FromSeconds(Math.Max(0, serverOverride?.ServerCooldownSeconds ?? Config.ServerCooldownSeconds)),
            string.IsNullOrWhiteSpace(serverOverride?.Language) ? Config.DefaultLanguage : serverOverride!.Language!,
            Config.ExcludedPhrases.Concat(serverOverride?.ExcludedPhrases ?? []).ToArray());
    }

    private PlayerGuidanceServerOverride? FindServerOverride(string serverId)
    {
        var exact = Config.ServerOverrides.FirstOrDefault(pair => pair.Key.Equals(serverId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(exact.Key)) return exact.Value;
        var wildcard = Config.ServerOverrides.FirstOrDefault(pair => pair.Key == "*");
        return string.IsNullOrEmpty(wildcard.Key) ? null : wildcard.Value;
    }

    private string FormatReminder(string categoryName, string senderName, string? targetName, EffectiveGuidanceSettings settings)
    {
        var category = Config.Categories.FirstOrDefault(item => item.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
        var template = Localized(category?.ReminderMessages, settings.Language)
                       ?? Localized(Config.ReminderMessages, settings.Language)
                       ?? "^1REMINDER:^3 Use {reportCommand} {target} <reason> to report suspected {category}.";
        return template
            .Replace("{target}", string.IsNullOrWhiteSpace(targetName) ? "<player>" : targetName.StripColors(), StringComparison.OrdinalIgnoreCase)
            .Replace("{player}", senderName.StripColors(), StringComparison.OrdinalIgnoreCase)
            .Replace("{category}", categoryName, StringComparison.OrdinalIgnoreCase)
            .Replace("{reportCommand}", settings.ReportCommand, StringComparison.OrdinalIgnoreCase)
            .Replace("{cooldown}", ((int)settings.PlayerCooldown.TotalSeconds).ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private string? Localized(IReadOnlyDictionary<string, string>? messages, string language)
    {
        if (messages is null || messages.Count == 0) return null;
        static string? Find(IReadOnlyDictionary<string, string> source, string key) =>
            source.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
        var value = Find(messages, language);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        var separator = language.IndexOfAny(['-', '_']);
        if (separator > 0 && !string.IsNullOrWhiteSpace(value = Find(messages, language[..separator]))) return value;
        if (!string.IsNullOrWhiteSpace(value = Find(messages, Config.DefaultLanguage))) return value;
        if (!string.IsNullOrWhiteSpace(value = Find(messages, "en"))) return value;
        return messages.Values.FirstOrDefault();
    }

    private EFClient? FindMentionedTarget(
        string message,
        string matchedPattern,
        string category,
        EFClient origin,
        IReadOnlyList<EFClient> clients,
        bool excludeBots)
    {
        var candidates = clients
            .Where(client => client.ClientId != origin.ClientId && (!excludeBots || !client.IsBot))
            .ToArray();
        var clientId = GuidanceTargetResolver.ResolveUniqueClientId(
            message,
            matchedPattern,
            category,
            candidates.Select(value => (value.ClientId, value.CleanedName)),
            Config.MinimumTargetNameLength,
            Config.EnableLeetNormalization);
        return clientId.HasValue ? candidates.FirstOrDefault(value => value.ClientId == clientId.Value) : null;
    }

    private EFClient? FindReportTarget(string message, string reportCommand, EFClient origin, IReadOnlyList<EFClient> clients, bool excludeBots)
    {
        var clean = message.StripColors().Trim();
        if (!clean.StartsWith(reportCommand, StringComparison.OrdinalIgnoreCase)) return null;
        var argument = clean[reportCommand.Length..].TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(argument)) return null;
        if (int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clientId))
            return clients.FirstOrDefault(item => item.ClientId == clientId && item.ClientId != origin.ClientId && (!excludeBots || !item.IsBot));

        var normalizedArgument = PlayerGuidanceDetectionEngine.Normalize(argument, Config.EnableLeetNormalization);
        var candidates = clients
            .Where(item => item.ClientId != origin.ClientId && (!excludeBots || !item.IsBot))
            .Select(item => new { Client = item, Name = PlayerGuidanceDetectionEngine.Normalize(item.CleanedName, Config.EnableLeetNormalization) })
            .Where(item => item.Name.Equals(normalizedArgument, StringComparison.Ordinal) || item.Name.StartsWith(normalizedArgument, StringComparison.Ordinal))
            .OrderBy(item => item.Name.Length)
            .ToArray();
        return candidates.Length == 1 ? candidates[0].Client : null;
    }

    private void RecordContext(string serverId, GuidanceContextMessageRecord message)
    {
        if (!Config.RetainAdminReviewContext)
            return;
        var maximum = Math.Clamp(Config.ReviewContextMaximumMessages, 5, 100);
        var after = TimeSpan.FromSeconds(Math.Clamp(Config.ReviewContextAfterSeconds, 0, 300));
        _store.AppendPlayerGuidanceContext(serverId, message, after, maximum);
        var window = _chatHistory.GetOrAdd(serverId, _ => new ChatHistoryWindow());
        lock (window.Messages)
        {
            window.Messages.Add(message);
            var cutoff = message.CapturedAt.AddSeconds(-Math.Clamp(Config.ReviewContextBeforeSeconds, 5, 600));
            window.Messages.RemoveAll(value => value.CapturedAt < cutoff);
            if (window.Messages.Count > maximum)
                window.Messages.RemoveRange(0, window.Messages.Count - maximum);
        }
    }

    private List<GuidanceContextMessageRecord> ContextBefore(string serverId, DateTimeOffset capturedAt)
    {
        if (!_chatHistory.TryGetValue(serverId, out var window))
            return [];
        var cutoff = capturedAt.AddSeconds(-Math.Clamp(Config.ReviewContextBeforeSeconds, 5, 600));
        lock (window.Messages)
            return window.Messages.Where(value => value.CapturedAt >= cutoff && value.CapturedAt <= capturedAt)
                .TakeLast(Math.Clamp(Config.ReviewContextMaximumMessages, 5, 100)).ToList();
    }

    private string TruncateContext(string value)
    {
        var clean = AnalyticsEngine.CleanDisplayText(value);
        var maximum = Math.Clamp(_rootConfig.ChatExcerptMaximumLength, 20, 250);
        return clean.Length <= maximum ? clean : clean[..maximum] + "…";
    }

    private bool ExcludeBots(string serverId) =>
        _rootConfig.ServerOverrides.TryGetValue(serverId, out var serverOverride) && serverOverride.ExcludeBots.HasValue
            ? serverOverride.ExcludeBots.Value
            : _rootConfig.ExcludeBots;

    private async Task<bool> MaybeNotifyStaffAsync(
        IReadOnlyList<EFClient> clients,
        string serverId,
        string reporterKey,
        string category,
        EFClient? target,
        CancellationToken token)
    {
        if (!Config.NotifyStaff || target is null) return false;
        var now = _timeProvider.GetUtcNow();
        var window = TimeSpan.FromSeconds(Math.Max(1, Config.StaffAlertWindowSeconds));
        var threshold = Math.Max(1, Config.StaffAlertThreshold);
        var key = $"{serverId}|{target?.ClientId.ToString(CultureInfo.InvariantCulture) ?? "unknown"}|{category}";
        var state = _staffAlertWindows.GetOrAdd(key, _ => new StaffAlertWindow(now));
        int count;
        lock (state)
        {
            if (now - state.WindowStartedAt > window)
            {
                state.WindowStartedAt = now;
                state.ReporterKeys.Clear();
                state.AlertSent = false;
            }
            state.ReporterKeys.Add(reporterKey);
            count = state.ReporterKeys.Count;
            if (state.AlertSent || count < threshold) return false;
            state.AlertSent = true;
        }

        var message = Config.StaffAlertMessage
            .Replace("{target}", target?.CleanedName?.StripColors() ?? "<unknown>", StringComparison.OrdinalIgnoreCase)
            .Replace("{category}", category, StringComparison.OrdinalIgnoreCase)
            .Replace("{count}", count.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{window}", ((int)window.TotalSeconds).ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace('\r', ' ').Replace('\n', ' ');
        foreach (var staff in clients.Where(item => item.Level >= Config.StaffMinimumPermission))
            await staff.TellAsync(SplitMessage(message, Math.Clamp(Config.MaxMessageLength, 40, 1000)), token);
        return true;
    }

    private static bool IsReportCommand(string message, string reportCommand)
    {
        var clean = message.StripColors().TrimStart();
        return clean.Equals(reportCommand, StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith(reportCommand + " ", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryAcquireCooldown<TKey>(ConcurrentDictionary<TKey, long> cooldowns, TKey key, TimeSpan cooldown) where TKey : notnull
    {
        if (cooldown <= TimeSpan.Zero) return true;
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        while (true)
        {
            if (!cooldowns.TryGetValue(key, out var previous))
            {
                if (cooldowns.TryAdd(key, nowTicks)) return true;
                continue;
            }
            if (TimeSpan.FromTicks(nowTicks - previous) < cooldown) return false;
            if (cooldowns.TryUpdate(key, nowTicks, previous)) return true;
        }
    }

    private void PruneIfNeeded()
    {
        if (Interlocked.Increment(ref _messagesSincePrune) < 256) return;
        Interlocked.Exchange(ref _messagesSincePrune, 0);
        var cutoff = _timeProvider.GetUtcNow().AddMinutes(-10).UtcTicks;
        foreach (var item in _playerCooldowns.Where(item => item.Value < cutoff)) _playerCooldowns.TryRemove(item.Key, out _);
        foreach (var item in _serverCooldowns.Where(item => item.Value < cutoff)) _serverCooldowns.TryRemove(item.Key, out _);
        var staffCutoff = _timeProvider.GetUtcNow().AddSeconds(-Math.Max(1, Config.StaffAlertWindowSeconds) - 300);
        foreach (var item in _staffAlertWindows)
        {
            lock (item.Value)
                if (item.Value.WindowStartedAt < staffCutoff) _staffAlertWindows.TryRemove(item.Key, out _);
        }
    }

    internal static string[] SplitMessage(string message, int maximumLength)
    {
        if (message.Length <= maximumLength) return [message];
        var values = new List<string>();
        var remaining = message;
        while (remaining.Length > maximumLength)
        {
            var split = remaining.LastIndexOf(' ', maximumLength);
            if (split < maximumLength / 2) split = maximumLength;
            values.Add(remaining[..split].TrimEnd());
            remaining = remaining[split..].TrimStart();
        }
        if (remaining.Length > 0) values.Add(remaining);
        return values.ToArray();
    }

    private void OnConfigurationUpdated(ServerPulseConfig _) => RefreshConfiguration();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _configurationHandler.Updated -= OnConfigurationUpdated;
    }

    private readonly record struct PlayerCooldownKey(string ServerId, int ClientId);
    private sealed class StaffAlertWindow(DateTimeOffset startedAt)
    {
        public DateTimeOffset WindowStartedAt = startedAt;
        public HashSet<string> ReporterKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool AlertSent;
    }
    private sealed class ChatHistoryWindow
    {
        public List<GuidanceContextMessageRecord> Messages { get; } = [];
    }
    private sealed record EffectiveGuidanceSettings(
        bool Enabled,
        GuidanceResponseMode ResponseMode,
        string ReportCommand,
        TimeSpan PlayerCooldown,
        TimeSpan ServerCooldown,
        string Language,
        string[] ExcludedPhrases);
}
