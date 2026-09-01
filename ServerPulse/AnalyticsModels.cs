namespace ServerPulse;

public sealed class AnalyticsState
{
    public int SchemaVersion { get; set; } = 5;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<PlayerSessionRecord> Sessions { get; set; } = [];
    public List<MapRoundRecord> MapRounds { get; set; } = [];
    public List<PopulationSampleRecord> PopulationSamples { get; set; } = [];
    public List<ChatSignalRecord> ChatSignals { get; set; } = [];
    public List<PlayerGuidanceEventRecord> PlayerGuidanceEvents { get; set; } = [];
    public List<ServerIncidentRecord> Incidents { get; set; } = [];
    public List<AnalyticsAnnotation> Annotations { get; set; } = [];
}

public sealed class PlayerSessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PlayerKey { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Game { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = "Unknown";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public bool IsNewPlayer { get; set; }
    public bool IsBot { get; set; }
    public string DisconnectReason { get; set; } = "Unknown";
}

public sealed class MapRoundRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Game { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public int PlayersAtStart { get; set; }
    public int PeakPlayers { get; set; }
    public int PlayersAtEnd { get; set; }
    public int Joins { get; set; }
    public int Leaves { get; set; }
    public bool EndedNormally { get; set; }
}

public sealed class PopulationSampleRecord
{
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Game { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public int HumanPlayers { get; set; }
    public int BotPlayers { get; set; }
    public int MaximumPlayers { get; set; }
    public double RconLatencyMilliseconds { get; set; }
    public double EventLatencyMilliseconds { get; set; }
}

public sealed class ChatSignalRecord
{
    public string MessageId { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Game { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = "Unknown";
    public string PlayerKey { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public string? Excerpt { get; set; }
}

public sealed class ServerIncidentRecord
{
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class PlayerGuidanceEventRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MessageId { get; set; } = string.Empty;
    public string EventType { get; set; } = "Accusation";
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public long? LegacyServerId { get; set; }
    public string Game { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = "Unknown";
    public string ReporterKey { get; set; } = string.Empty;
    public string TargetKey { get; set; } = string.Empty;
    public int? TargetClientId { get; set; }
    public long? TargetNetworkId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public string ResolutionMethod { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = "Unresolved";
    public int? ResolvedByClientId { get; set; }
    public string ResolvedByName { get; set; } = string.Empty;
    public DateTimeOffset? ResolvedAt { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public string DemosToDiscordCaseId { get; set; } = string.Empty;
    public string DemosToDiscordError { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public bool StaffAlertSent { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string? Excerpt { get; set; }
    public List<GuidanceContextMessageRecord> ContextMessages { get; set; } = [];
    public List<GuidancePlayerSnapshotRecord> PlayersAtCapture { get; set; } = [];
}

public sealed class GuidanceContextMessageRecord
{
    public string MessageId { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public int ClientId { get; set; }
    public string PlayerKey { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsTeamMessage { get; set; }
}

public sealed class GuidancePlayerSnapshotRecord
{
    public int ClientId { get; set; }
    public long NetworkId { get; set; }
    public string PlayerKey { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public bool IsBot { get; set; }
}

public sealed class AnalyticsAnnotation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ServerId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Category { get; set; } = "General";
    public string Note { get; set; } = string.Empty;
}

public sealed record DashboardSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PlayerSessionRecord> Sessions,
    IReadOnlyList<MapRoundRecord> MapRounds,
    IReadOnlyList<PopulationSampleRecord> PopulationSamples,
    IReadOnlyList<ChatSignalRecord> ChatSignals,
    IReadOnlyList<PlayerGuidanceEventRecord> PlayerGuidanceEvents,
    IReadOnlyList<ServerIncidentRecord> Incidents,
    IReadOnlyList<AnalyticsAnnotation> Annotations,
    int ActiveSessions,
    int ActiveMapRounds,
    string? LastError);

public sealed record Recommendation(
    string Severity,
    string Title,
    string Detail,
    string Action,
    int SampleSize,
    int Confidence);
