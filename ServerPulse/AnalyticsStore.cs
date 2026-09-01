using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ServerPulse;

public sealed class AnalyticsStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ServerPulseConfig _config;
    private readonly ILogger<AnalyticsStore> _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Timer _saveTimer;
    private AnalyticsState _state = new();
    private bool _dirty;
    private bool _disposed;

    public AnalyticsStore(ServerPulseConfig config, ILogger<AnalyticsStore> logger)
    {
        _config = config;
        _logger = logger;
        _saveTimer = new Timer(_ => _ = SaveIfDirtyAsync(CancellationToken.None), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public string? LastError { get; private set; }

    public async Task LoadAsync(CancellationToken token)
    {
        var path = ResolvePath();
        try
        {
            if (File.Exists(path))
            {
                await using var stream = File.OpenRead(path);
                _state = await JsonSerializer.DeserializeAsync<AnalyticsState>(stream, JsonOptions, token) ?? new AnalyticsState();
            }

            _state.SchemaVersion = 5;
            foreach (var item in _state.PlayerGuidanceEvents)
            {
                item.ContextMessages ??= [];
                item.PlayersAtCapture ??= [];
                if (string.IsNullOrWhiteSpace(item.ReviewStatus))
                    item.ReviewStatus = item.TargetClientId.HasValue ? "AutomaticallyResolved" : "Unresolved";
            }
            Prune();
            LastError = null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            _logger.LogError(exception, "[ServerPulse] failed to load analytics data from {Path}", path);
            _state = new AnalyticsState();
        }
    }

    public DashboardSnapshot Snapshot(int activeSessions, int activeMapRounds)
    {
        lock (_gate)
        {
            return new DashboardSnapshot(
                DateTimeOffset.UtcNow,
                _state.Sessions.ToArray(),
                _state.MapRounds.ToArray(),
                _state.PopulationSamples.ToArray(),
                _state.ChatSignals.ToArray(),
                _state.PlayerGuidanceEvents.ToArray(),
                _state.Incidents.ToArray(),
                _state.Annotations.ToArray(),
                activeSessions,
                activeMapRounds,
                LastError);
        }
    }

    public void AddSession(PlayerSessionRecord value) => Mutate(() => _state.Sessions.Add(value));
    public void AddMapRound(MapRoundRecord value) => Mutate(() => _state.MapRounds.Add(value));
    public void AddPopulationSample(PopulationSampleRecord value) => Mutate(() => _state.PopulationSamples.Add(value));
    public void AddChatSignals(IEnumerable<ChatSignalRecord> values) => Mutate(() => _state.ChatSignals.AddRange(values));
    public void AddPlayerGuidanceEvent(PlayerGuidanceEventRecord value) => Mutate(() => _state.PlayerGuidanceEvents.Add(value));
    public PlayerGuidanceEventRecord? GetPlayerGuidanceEvent(string id)
    {
        lock (_gate)
        {
            var value = _state.PlayerGuidanceEvents.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return value is null ? null : Clone(value);
        }
    }

    public bool UpdatePlayerGuidanceEvent(string id, Action<PlayerGuidanceEventRecord> update)
    {
        var updated = false;
        Mutate(() =>
        {
            var value = _state.PlayerGuidanceEvents.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (value is null)
                return;
            update(value);
            updated = true;
        });
        return updated;
    }

    public void AppendPlayerGuidanceContext(
        string serverId,
        GuidanceContextMessageRecord message,
        TimeSpan afterWindow,
        int maximumMessages)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var item in _state.PlayerGuidanceEvents.Where(item =>
                         item.EventType.Equals("Accusation", StringComparison.OrdinalIgnoreCase) &&
                         item.ServerId.Equals(serverId, StringComparison.OrdinalIgnoreCase) &&
                         message.CapturedAt >= item.CapturedAt &&
                         message.CapturedAt - item.CapturedAt <= afterWindow))
            {
                item.ContextMessages ??= [];
                if (item.ContextMessages.Any(value => value.MessageId.Equals(message.MessageId, StringComparison.Ordinal)))
                    continue;
                item.ContextMessages.Add(message);
                if (item.ContextMessages.Count > maximumMessages)
                    item.ContextMessages = item.ContextMessages.TakeLast(maximumMessages).ToList();
                changed = true;
            }
            if (changed)
                _dirty = true;
        }
        if (changed)
            _saveTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }
    public void AddIncident(ServerIncidentRecord value) => Mutate(() => _state.Incidents.Add(value));

    public void ResolveIncident(string serverId, string type, DateTimeOffset resolvedAt) => Mutate(() =>
    {
        var incident = _state.Incidents.LastOrDefault(item => item.ServerId == serverId &&
            item.Type == type && item.ResolvedAt is null);
        if (incident is not null)
            incident.ResolvedAt = resolvedAt;
    });

    public void AddAnnotation(AnalyticsAnnotation value) => Mutate(() => _state.Annotations.Add(value));

    public async Task SaveIfDirtyAsync(CancellationToken token)
    {
        AnalyticsState snapshot;
        lock (_gate)
        {
            if (!_dirty)
                return;
            _dirty = false;
            _state.UpdatedAt = DateTimeOffset.UtcNow;
            snapshot = new AnalyticsState
            {
                SchemaVersion = _state.SchemaVersion,
                CreatedAt = _state.CreatedAt,
                UpdatedAt = _state.UpdatedAt,
                Sessions = _state.Sessions.ToList(),
                MapRounds = _state.MapRounds.ToList(),
                PopulationSamples = _state.PopulationSamples.ToList(),
                ChatSignals = _state.ChatSignals.ToList(),
                PlayerGuidanceEvents = _state.PlayerGuidanceEvents.ToList(),
                Incidents = _state.Incidents.ToList(),
                Annotations = _state.Annotations.ToList()
            };
        }

        await _writeGate.WaitAsync(token);
        try
        {
            var path = ResolvePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var temporary = path + ".tmp";
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, token);
            File.Move(temporary, path, true);
            LastError = null;
        }
        catch (Exception exception)
        {
            lock (_gate)
                _dirty = true;
            LastError = exception.Message;
            _logger.LogError(exception, "[ServerPulse] failed to save analytics data");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Prune()
    {
        lock (_gate)
        {
            var rawCutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _config.RawDataRetentionDays));
            var aggregateCutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(_config.RawDataRetentionDays, _config.AggregateRetentionDays));
            _state.Sessions = _state.Sessions.Where(item => item.StartedAt >= aggregateCutoff)
                .TakeLast(Math.Max(1_000, _config.MaxSessions)).ToList();
            _state.MapRounds = _state.MapRounds.Where(item => item.StartedAt >= aggregateCutoff)
                .TakeLast(Math.Max(500, _config.MaxMapRounds)).ToList();
            _state.PopulationSamples = _state.PopulationSamples.Where(item => item.CapturedAt >= aggregateCutoff)
                .TakeLast(Math.Max(5_000, _config.MaxPopulationSamples)).ToList();
            _state.ChatSignals = _state.ChatSignals.Where(item => item.CapturedAt >= rawCutoff)
                .TakeLast(Math.Max(500, _config.MaxChatSignals)).ToList();
            _state.PlayerGuidanceEvents = _state.PlayerGuidanceEvents.Where(item => item.CapturedAt >= rawCutoff)
                .TakeLast(Math.Max(500, _config.MaxChatSignals)).ToList();
            _state.Incidents = _state.Incidents.Where(item => item.StartedAt >= aggregateCutoff).ToList();
            _state.Annotations = _state.Annotations.Where(item => item.OccurredAt >= aggregateCutoff).ToList();
            _dirty = true;
        }
    }

    private void Mutate(Action action)
    {
        lock (_gate)
        {
            action();
            _dirty = true;
        }
        _saveTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    private static PlayerGuidanceEventRecord Clone(PlayerGuidanceEventRecord value) =>
        JsonSerializer.Deserialize<PlayerGuidanceEventRecord>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    private string ResolvePath() => Path.GetFullPath(_config.StateFilePath, Directory.GetCurrentDirectory());

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _saveTimer.Dispose();
        SaveIfDirtyAsync(CancellationToken.None).GetAwaiter().GetResult();
        _writeGate.Dispose();
    }
}
