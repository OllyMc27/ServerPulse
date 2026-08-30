using Data.Models.Client;

namespace ServerPulse;

public sealed class ServerPulseConfig
{
    public bool Enabled { get; set; } = true;
    public bool EnableWebfrontDashboard { get; set; } = true;
    public EFClient.Permission WebfrontMinimumPermission { get; set; } = EFClient.Permission.Moderator;
    public string TimeZone { get; set; } = AnalyticsTime.DefaultTimeZoneId;
    public string StateFilePath { get; set; } = "Configuration/ServerPulseData.json";
    public int PopulationSnapshotSeconds { get; set; } = 60;
    public int RawDataRetentionDays { get; set; } = 30;
    public int AggregateRetentionDays { get; set; } = 730;
    public int MaxSessions { get; set; } = 100_000;
    public int MaxMapRounds { get; set; } = 25_000;
    public int MaxPopulationSamples { get; set; } = 250_000;
    public int MaxChatSignals { get; set; } = 25_000;
    public int BounceThresholdSeconds { get; set; } = 120;
    public bool ExcludeBots { get; set; } = true;
    public string AnonymizationSalt { get; set; } = string.Empty;
    public bool StoreRawChat { get; set; }
    public bool StoreRedactedChatExcerpts { get; set; }
    public int ChatExcerptMaximumLength { get; set; } = 100;
    public bool EnableCountryAnalytics { get; set; } = true;
    public int MinimumCountrySampleSize { get; set; } = 3;
    public bool EnableRecommendations { get; set; } = true;
    public bool EnableOperationalMonitoring { get; set; } = true;
    public bool Debug { get; set; }
    public List<string> ExcludedServers { get; set; } = [];
    public Dictionary<string, List<string>> ChatCategories { get; set; } = DefaultChatCategories();
    public Dictionary<string, ServerPulseServerOverride> ServerOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, List<string>> DefaultChatCategories() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cheating"] = ["cheater", "cheating", "hacker", "hacking", "aimbot", "wallhack", "walls"],
            ["Lag"] = ["lag", "lagging", "latency", "packet loss", "rubber band", "connection interrupted"],
            ["Map"] = ["bad map", "hate this map", "change map", "skip map", "next map"],
            ["Mode"] = ["bad mode", "change mode", "game mode", "gamemode"],
            ["Bots"] = ["too many bots", "remove bots", "bot lobby", "bots are"],
            ["Balance"] = ["unbalanced", "team balance", "stacked team", "teams are stacked"],
            ["Spawns"] = ["bad spawns", "spawn trap", "spawn trapping", "spawn killed"],
            ["Admin"] = ["admin", "moderator", "need staff", "where are the admins"],
            ["Positive"] = ["good server", "great server", "nice server", "love this server", "gg"]
        };
}

public sealed class ServerPulseServerOverride
{
    public bool? Enabled { get; set; }
    public string? TimeZone { get; set; }
    public bool? ExcludeBots { get; set; }
    public bool? EnableChatAnalysis { get; set; }
    public bool? EnableCountryAnalytics { get; set; }
}
