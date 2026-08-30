using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Data.Models;
using Microsoft.Extensions.Logging;
using SharedLibraryCore;
using SharedLibraryCore.Database.Models;
using SharedLibraryCore.Events.Game;
using SharedLibraryCore.Events.Management;
using SharedLibraryCore.Events.Server;
using SharedLibraryCore.Interfaces;

namespace ServerPulse;

public sealed class AnalyticsEngine : IDisposable
{
    private readonly ServerPulseConfig _config;
    private readonly AnalyticsStore _store;
    private readonly ChatSignalClassifier _classifier;
    private readonly IGeoLocationService _geoLocation;
    private readonly ILogger<AnalyticsEngine> _logger;
    private readonly ConcurrentDictionary<string, PlayerSessionRecord> _activeSessions = new();
    private readonly ConcurrentDictionary<string, MapRoundRecord> _activeRounds = new();
    private readonly ConcurrentDictionary<string, string> _pendingDisconnectReasons = new();
    private Timer? _populationTimer;
    private IManager? _manager;
    private bool _disposed;

    public AnalyticsEngine(
        ServerPulseConfig config,
        AnalyticsStore store,
        ChatSignalClassifier classifier,
        IGeoLocationService geoLocation,
        ILogger<AnalyticsEngine> logger)
    {
        _config = config;
        _store = store;
        _classifier = classifier;
        _geoLocation = geoLocation;
        _logger = logger;
    }

    public async Task StartAsync(IManager manager, CancellationToken token)
    {
        _manager = manager;
        await _store.LoadAsync(token);
        var interval = TimeSpan.FromSeconds(Math.Clamp(_config.PopulationSnapshotSeconds, 15, 900));
        _populationTimer = new Timer(_ => CapturePopulation(), null, TimeSpan.Zero, interval);
    }

    public DashboardSnapshot Snapshot() => _store.Snapshot(_activeSessions.Count, _activeRounds.Count);

    public async Task ClientAuthorizedAsync(ClientStateAuthorizeEvent clientEvent, CancellationToken token)
    {
        if (!ShouldTrack(clientEvent.Client.CurrentServer, clientEvent.Client))
            return;

        var client = clientEvent.Client;
        var server = client.CurrentServer;
        var key = SessionKey(server.Id, client);
        var countryCode = string.Empty;
        var countryName = "Unknown";
        if (CountryEnabled(server.Id) && !client.IsBot && !string.IsNullOrWhiteSpace(client.IPAddressString))
        {
            try
            {
                var location = await _geoLocation.Locate(client.IPAddressString);
                countryCode = location.CountryCode ?? string.Empty;
                countryName = string.IsNullOrWhiteSpace(location.Country) ? "Unknown" : location.Country;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "[ServerPulse] country lookup failed for a connecting player");
            }
        }

        var session = new PlayerSessionRecord
        {
            PlayerKey = PlayerKey(client),
            ServerId = server.Id,
            ServerName = server.ServerName,
            Game = server.GameCode.ToString(),
            Map = FriendlyMap(server),
            Mode = FriendlyMode(server.Gametype),
            CountryCode = countryCode,
            CountryName = countryName,
            StartedAt = clientEvent.CreatedAt,
            IsNewPlayer = client.Connections <= 1,
            IsBot = client.IsBot
        };
        _activeSessions[key] = session;
    }

    public Task ClientDisposedAsync(ClientStateDisposeEvent clientEvent, CancellationToken token)
    {
        var client = clientEvent.Client;
        var server = client.CurrentServer;
        if (server is null)
            return Task.CompletedTask;

        var key = SessionKey(server.Id, client);
        if (!_activeSessions.TryRemove(key, out var session))
            return Task.CompletedTask;

        session.EndedAt = clientEvent.CreatedAt;
        session.DurationSeconds = Math.Max(0, (int)(clientEvent.CreatedAt - session.StartedAt).TotalSeconds);
        session.DisconnectReason = _pendingDisconnectReasons.TryRemove(key, out var reason) ? reason : "Quit or lost connection";
        _store.AddSession(session);
        return Task.CompletedTask;
    }

    public Task PenaltyAsync(ClientPenaltyEvent penaltyEvent, CancellationToken token)
    {
        if (penaltyEvent.Client.CurrentServer is null)
            return Task.CompletedTask;
        var reason = penaltyEvent.Penalty.Type switch
        {
            EFPenalty.PenaltyType.Kick => "Kicked",
            EFPenalty.PenaltyType.TempBan => "Temporarily banned",
            EFPenalty.PenaltyType.Ban => "Banned",
            _ => string.Empty
        };
        if (reason.Length > 0)
            _pendingDisconnectReasons[SessionKey(penaltyEvent.Client.CurrentServer.Id, penaltyEvent.Client)] = reason;
        return Task.CompletedTask;
    }

    public Task MatchStartedAsync(MatchStartEvent matchEvent, CancellationToken token)
    {
        if (!ServerEnabled(matchEvent.Server.Id))
            return Task.CompletedTask;
        var players = HumanCount(matchEvent.Server.Id, matchEvent.Server.ConnectedClients);
        _activeRounds[matchEvent.Server.Id] = new MapRoundRecord
        {
            ServerId = matchEvent.Server.Id,
            ServerName = matchEvent.Server.ServerName,
            Game = matchEvent.Server.GameCode.ToString(),
            Map = FriendlyMap(matchEvent.Server),
            Mode = FriendlyMode(matchEvent.Server.Gametype),
            StartedAt = matchEvent.CreatedAt,
            PlayersAtStart = players,
            PeakPlayers = players
        };
        return Task.CompletedTask;
    }

    public Task MatchEndedAsync(MatchEndEvent matchEvent, CancellationToken token)
    {
        if (!_activeRounds.TryRemove(matchEvent.Server.Id, out var round))
            return Task.CompletedTask;
        round.EndedAt = matchEvent.CreatedAt;
        round.DurationSeconds = Math.Max(0, (int)(matchEvent.CreatedAt - round.StartedAt).TotalSeconds);
        round.PlayersAtEnd = HumanCount(matchEvent.Server.Id, matchEvent.Server.ConnectedClients);
        round.EndedNormally = true;
        _store.AddMapRound(round);
        return Task.CompletedTask;
    }

    public Task ClientEnteredMatchAsync(ClientEnterMatchEvent clientEvent, CancellationToken token)
    {
        if (_activeRounds.TryGetValue(clientEvent.Server.Id, out var round) &&
            (!ExcludeBots(clientEvent.Server.Id) || !clientEvent.Client.IsBot))
        {
            lock (round)
                round.Joins++;
        }
        return Task.CompletedTask;
    }

    public Task ClientExitedMatchAsync(ClientExitMatchEvent clientEvent, CancellationToken token)
    {
        if (_activeRounds.TryGetValue(clientEvent.Server.Id, out var round) &&
            (!ExcludeBots(clientEvent.Server.Id) || !clientEvent.Client.IsBot))
        {
            lock (round)
                round.Leaves++;
        }
        return Task.CompletedTask;
    }

    public Task ChatAsync(ClientMessageEvent messageEvent, CancellationToken token)
    {
        if (!ShouldTrack(messageEvent.Server, messageEvent.Client) || !ChatEnabled(messageEvent.Server.Id))
            return Task.CompletedTask;
        var categories = _classifier.Classify(messageEvent.Message);
        if (categories.Count == 0)
            return Task.CompletedTask;

        var excerpt = _config.StoreRawChat
            ? Truncate(messageEvent.Message, Math.Clamp(_config.ChatExcerptMaximumLength, 20, 250))
            : _classifier.Excerpt(messageEvent.Message);
        _store.AddChatSignals(categories.Select(category => new ChatSignalRecord
        {
            ServerId = messageEvent.Server.Id,
            ServerName = messageEvent.Server.ServerName,
            Game = messageEvent.Server.GameCode.ToString(),
            PlayerKey = PlayerKey(messageEvent.Client),
            Category = category,
            CapturedAt = messageEvent.CreatedAt,
            Excerpt = excerpt
        }));
        return Task.CompletedTask;
    }

    public Task MonitoringStartedAsync(MonitorStartEvent serverEvent, CancellationToken token)
    {
        _store.ResolveIncident(serverEvent.Server.Id, "Monitoring stopped", serverEvent.CreatedAt);
        return Task.CompletedTask;
    }

    public Task MonitoringStoppedAsync(MonitorStopEvent serverEvent, CancellationToken token)
    {
        CloseRound(serverEvent.Server.Id, serverEvent.CreatedAt, false);
        _store.AddIncident(new ServerIncidentRecord
        {
            ServerId = serverEvent.Server.Id,
            ServerName = serverEvent.Server.ServerName,
            Type = "Monitoring stopped",
            StartedAt = serverEvent.CreatedAt
        });
        return Task.CompletedTask;
    }

    public Task ConnectionInterruptedAsync(ConnectionInterruptEvent serverEvent, CancellationToken token)
    {
        _store.AddIncident(new ServerIncidentRecord
        {
            ServerId = serverEvent.Server.Id,
            ServerName = serverEvent.Server.ServerName,
            Type = "Connection interrupted",
            StartedAt = serverEvent.CreatedAt
        });
        return Task.CompletedTask;
    }

    public Task ConnectionRestoredAsync(ConnectionRestoreEvent serverEvent, CancellationToken token)
    {
        _store.ResolveIncident(serverEvent.Server.Id, "Connection interrupted", serverEvent.CreatedAt);
        return Task.CompletedTask;
    }

    private void CapturePopulation()
    {
        try
        {
            foreach (var server in _manager?.GetServers() ?? [])
            {
                if (!ServerEnabled(server.Id))
                    continue;
                var clients = server.ConnectedClients;
                var humans = clients.Count(item => !item.IsBot);
                var bots = clients.Count(item => item.IsBot);
                if (_activeRounds.TryGetValue(server.Id, out var round))
                    round.PeakPlayers = Math.Max(round.PeakPlayers, humans);
                _store.AddPopulationSample(new PopulationSampleRecord
                {
                    ServerId = server.Id,
                    ServerName = server.ServerName,
                    Game = server.GameCode.ToString(),
                    Map = FriendlyMap(server),
                    Mode = FriendlyMode(server.Gametype),
                    CapturedAt = DateTimeOffset.UtcNow,
                    HumanPlayers = humans,
                    BotPlayers = bots,
                    MaximumPlayers = server.MaxClients,
                    RconLatencyMilliseconds = ReadLatencyMetric(server, "RconRoundTripMs"),
                    EventLatencyMilliseconds = ReadLatencyMetric(server, "GameLogPipelineMs")
                });
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[ServerPulse] population snapshot failed");
        }
    }

    private void CloseRound(string serverId, DateTimeOffset at, bool normal)
    {
        if (!_activeRounds.TryRemove(serverId, out var round))
            return;
        round.EndedAt = at;
        round.DurationSeconds = Math.Max(0, (int)(at - round.StartedAt).TotalSeconds);
        round.EndedNormally = normal;
        _store.AddMapRound(round);
    }

    private static double ReadLatencyMetric(object server, string metricName)
    {
        // LatencyMetrics was added to newer IW4MAdmin builds. Reflection keeps the
        // plugin compatible with older supported hosts while using it when present.
        var metrics = server.GetType().GetProperty("LatencyMetrics")?.GetValue(server);
        var value = metrics?.GetType().GetProperty(metricName)?.GetValue(metrics);
        return value is null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private bool ShouldTrack(IGameServer? server, EFClient client) =>
        _config.Enabled && server is not null && ServerEnabled(server.Id) && (!ExcludeBots(server.Id) || !client.IsBot);

    private bool ServerEnabled(string serverId)
    {
        if (_config.ExcludedServers.Contains(serverId, StringComparer.OrdinalIgnoreCase))
            return false;
        return !_config.ServerOverrides.TryGetValue(serverId, out var value) || value.Enabled != false;
    }

    private bool ChatEnabled(string serverId) =>
        !_config.ServerOverrides.TryGetValue(serverId, out var value) || value.EnableChatAnalysis != false;

    private bool CountryEnabled(string serverId) => _config.EnableCountryAnalytics &&
        (!_config.ServerOverrides.TryGetValue(serverId, out var value) || value.EnableCountryAnalytics != false);

    private bool ExcludeBots(string serverId) =>
        _config.ServerOverrides.TryGetValue(serverId, out var value) && value.ExcludeBots.HasValue
            ? value.ExcludeBots.Value
            : _config.ExcludeBots;

    private int HumanCount(string serverId, IReadOnlyList<EFClient> clients) => ExcludeBots(serverId)
        ? clients.Count(item => !item.IsBot)
        : clients.Count;

    private string PlayerKey(EFClient client)
    {
        var identity = client.NetworkId != 0 ? client.NetworkId.ToString() : $"client:{client.ClientId}";
        var key = Encoding.UTF8.GetBytes(_config.AnonymizationSalt);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
    }

    private static string SessionKey(string serverId, EFClient client) =>
        $"{serverId}:{(client.ClientId > 0 ? client.ClientId : client.NetworkId)}";

    private static string FriendlyMap(IGameServer server) =>
        string.IsNullOrWhiteSpace(server.Map?.Alias) ? server.Map?.Name ?? "Unknown" : server.Map.Alias;

    public static string FriendlyMode(string? mode) => (mode ?? string.Empty).ToLowerInvariant() switch
    {
        "dm" or "ffa" => "Free For All",
        "war" or "tdm" => "Team Deathmatch",
        "dom" => "Domination",
        "koth" => "Hardpoint",
        "hq" => "Headquarters",
        "sd" or "sd2" => "Search & Destroy",
        "ctf" => "Capture the Flag",
        "conf" => "Kill Confirmed",
        "dem" => "Demolition",
        "sab" => "Sabotage",
        "zclassic" => "Zombies Classic",
        "zstandard" => "Zombies",
        { Length: 0 } => "Unknown",
        var value => value
    };

    private static string Truncate(string? value, int maximum) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Length <= maximum ? value : value[..maximum] + "…";

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _populationTimer?.Dispose();
        var now = DateTimeOffset.UtcNow;
        foreach (var serverId in _activeRounds.Keys)
            CloseRound(serverId, now, false);
        _store.SaveIfDirtyAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
