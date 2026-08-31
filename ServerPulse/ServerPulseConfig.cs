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
    public bool StoreRedactedChatExcerpts { get; set; } = true;
    public int ChatExcerptMaximumLength { get; set; } = 160;
    public bool EnableCountryAnalytics { get; set; } = true;
    public int MinimumCountrySampleSize { get; set; } = 3;
    public bool EnableRecommendations { get; set; } = true;
    public bool EnableOperationalMonitoring { get; set; } = true;
    public PlayerGuidanceConfig PlayerGuidance { get; set; } = new();
    public bool Debug { get; set; }
    public List<string> ExcludedServers { get; set; } = [];
    public Dictionary<string, List<string>> ChatCategories { get; set; } = DefaultChatCategories();
    public Dictionary<string, ServerPulseServerOverride> ServerOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, List<string>> DefaultChatCategories() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cheating"] = ["cheater", "cheating", "hacker", "hacking", "aimbot", "aim bot", "wallhack", "wall hack", "walls", "esp", "silent aim", "triggerbot", "no recoil", "soft aim", "spinbot", "speed hack", "rapid fire hack", "sus player", "report this player"],
            ["Lag"] = ["lag", "lagging", "server lag", "latency", "high ping", "bad ping", "ping spike", "packet loss", "rubber band", "rubberband", "stuttering", "teleporting players", "connection interrupted"],
            ["Connection"] = ["keeps disconnecting", "connection lost", "lost connection", "timed out", "server timeout", "cannot connect", "can't connect", "reconnecting", "kicked me"],
            ["Stability"] = ["server crashed", "game crashed", "keeps crashing", "server froze", "game froze", "keeps freezing", "server restart", "random restart"],
            ["Map"] = ["bad map", "map sucks", "hate this map", "change map", "skip map", "next map", "remove this map", "same map again", "map rotation", "map pool", "more maps", "different maps", "map is broken"],
            ["Mode"] = ["bad mode", "mode sucks", "hate this mode", "change mode", "switch mode", "game mode", "gamemode", "remove this mode"],
            ["Voting"] = ["vote not working", "voting is broken", "cannot vote", "can't vote", "map vote", "mode vote", "vote map", "vote mode"],
            ["Bots"] = ["too many bots", "remove bots", "remove the bots", "less bots", "fewer bots", "bot lobby", "bots are too hard", "bots are too easy", "bot difficulty"],
            ["Balance"] = ["unbalanced", "team balance", "balance teams", "stacked team", "teams are stacked", "unfair teams", "switch teams", "auto balance"],
            ["Spawns"] = ["bad spawns", "spawns suck", "spawn trap", "spawn trapping", "spawn camp", "spawn camping", "spawn killed", "spawn protection"],
            ["Weapons"] = ["overpowered weapon", "op weapon", "weapon balance", "ban this weapon", "remove this weapon", "shotgun spam", "grenade spam", "noob tube", "sniper limit", "weapon is broken", "gun is broken", "too many snipers"],
            ["Progression"] = ["xp not working", "no xp", "slow xp", "rank reset", "stats reset", "level reset", "lost my rank", "lost my stats"],
            ["Rules"] = ["what are the rules", "server rules", "rules are unclear", "rules unclear", "not allowed", "is this allowed"],
            ["Toxicity"] = ["toxic player", "toxic chat", "racist", "racism", "harassment", "being abusive", "abusive player", "insulting everyone"],
            ["Admin"] = ["need an admin", "need admin", "need a moderator", "need staff", "admin help", "moderator help", "where are the admins", "no admins online", "staff online"],
            ["Population"] = ["server is empty", "empty server", "dead server", "need more players", "where is everyone", "nobody is playing", "no players"],
            ["Request"] = ["please add", "can you add", "server suggestion", "feature request", "would be better with", "you should add", "please change"],
            ["Teamkilling"] = ["team killing", "teamkilling", "team killer", "teamkiller", "friendly fire", "killed by teammate", "stop killing teammates", "shooting teammates"],
            ["Camping"] = ["camper", "camping", "corner camping", "spawn camping", "rooftop camping", "stop camping", "everyone is camping"],
            ["Exploits"] = ["glitching", "using a glitch", "using an exploit", "out of map", "under the map", "inside the wall", "god mode", "invisible glitch", "broken spot"],
            ["AFK"] = ["afk player", "players are afk", "kick afk", "inactive player", "idle player", "not playing", "away from keyboard"],
            ["Voice chat"] = ["mic spam", "microphone spam", "loud mic", "music spam", "voice spam", "mute this player", "mute player", "voice chat broken", "cannot hear voice", "can't hear voice"],
            ["Chat spam"] = ["chat spam", "spamming chat", "message spam", "advertising in chat", "discord spam", "link spam", "stop spamming"],
            ["Joining"] = ["cannot join", "can't join", "unable to join", "server is full", "reserved slot", "password required", "wrong password", "whitelist", "stuck connecting"],
            ["Downloads"] = ["missing map", "missing files", "download failed", "map download", "slow download", "fast download", "custom map download", "file mismatch"],
            ["Match flow"] = ["match too long", "round too long", "match too short", "round too short", "time limit", "score limit", "waiting for players", "match won't start", "round won't end", "game is stuck"],
            ["Spawning"] = ["cannot spawn", "can't spawn", "stuck spectating", "stuck in spectator", "spectator bug", "spawn button not working", "waiting to spawn"],
            ["Commands"] = ["command not working", "commands not working", "unknown command", "menu not working", "help command", "stats command", "vote command", "how do i vote"],
            ["Ban appeal"] = ["why was i banned", "why am i banned", "false ban", "unfair ban", "banned for nothing", "appeal ban", "ban appeal", "wrongfully banned"],
            ["Zombies"] = ["zombie stuck", "round is stuck", "zombies too hard", "zombies too easy", "pack a punch broken", "mystery box broken", "doors not working", "easter egg broken"],
            ["Server settings"] = ["friendly fire setting", "killcam setting", "hardcore mode", "starting ammo", "health setting", "fov setting", "third person", "killstreak limit", "scorestreak limit"],
            ["Events"] = ["when is the event", "next event", "community event", "tournament", "double xp event", "event time", "special event"],
            ["Positive"] = ["good server", "great server", "nice server", "best server", "love this server", "fun server", "good maps", "nice maps", "great rotation", "good admins", "thanks admin", "good ping", "no lag", "gg", "good game"]
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
