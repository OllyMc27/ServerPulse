using Data.Models.Client;

namespace ServerPulse;

public enum GuidanceResponseMode
{
    Disabled,
    Private,
    Public,
    Both
}

public enum GuidancePhraseMatchMode
{
    WholeWord,
    Substring
}

public sealed class GuidanceCategoryConfig
{
    public string Name { get; set; } = "Cheating";
    public bool Enabled { get; set; } = true;
    public GuidancePhraseMatchMode MatchMode { get; set; } = GuidancePhraseMatchMode.WholeWord;
    public List<string> Phrases { get; set; } = [];
    public List<string> RegexPatterns { get; set; } = [];
    public Dictionary<string, string> ReminderMessages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PlayerGuidanceServerOverride
{
    public bool? Enabled { get; set; }
    public GuidanceResponseMode? ResponseMode { get; set; }
    public string? ReportCommand { get; set; }
    public int? PlayerCooldownSeconds { get; set; }
    public int? ServerCooldownSeconds { get; set; }
    public string? Language { get; set; }
    public List<string> ExcludedPhrases { get; set; } = [];
}

public sealed class PlayerGuidanceConfig
{
    public bool Enabled { get; set; }
    public GuidanceResponseMode ResponseMode { get; set; } = GuidanceResponseMode.Public;
    public string ReportCommand { get; set; } = "!rep";
    public int PlayerCooldownSeconds { get; set; } = 45;
    public int ServerCooldownSeconds { get; set; } = 20;
    public bool TrackReportCommands { get; set; } = true;
    public bool IgnoreTeamMessages { get; set; }
    public bool EnableLeetNormalization { get; set; } = true;
    public bool EnableTargetAssistance { get; set; } = true;
    public int MinimumTargetNameLength { get; set; } = 3;
    public bool RetainAdminReviewContext { get; set; } = true;
    public int ReviewContextBeforeSeconds { get; set; } = 60;
    public int ReviewContextAfterSeconds { get; set; } = 30;
    public int ReviewContextMaximumMessages { get; set; } = 20;
    public bool EnableDemosToDiscordEscalation { get; set; } = true;
    public int MaxMessageLength { get; set; } = 140;
    public string DefaultLanguage { get; set; } = "en";
    public bool NotifyStaff { get; set; }
    public EFClient.Permission StaffMinimumPermission { get; set; } = EFClient.Permission.Moderator;
    public int StaffAlertThreshold { get; set; } = 3;
    public int StaffAlertWindowSeconds { get; set; } = 120;
    public string StaffAlertMessage { get; set; } =
        "^1[ServerPulse]^3 {count} unique players accused {target} of {category} in {window}s.";
    public Dictionary<string, string> ReminderMessages { get; set; } = DefaultReminderMessages();
    public Dictionary<string, string> UnresolvedReminderMessages { get; set; } = DefaultUnresolvedReminderMessages();
    public List<string> ExcludedPhrases { get; set; } = [];
    public List<string> CommunityReportPhrases { get; set; } = DefaultCommunityReportPhrases();
    public List<string> CommunityReportExclusions { get; set; } = ["anti cheat", "anticheat", "not cheating", "not a cheater"];
    public List<GuidanceCategoryConfig> Categories { get; set; } = DefaultCategories();
    public Dictionary<string, PlayerGuidanceServerOverride> ServerOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> DefaultReminderMessages() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "^1REMINDER:^3 If you believe {target} is {category}, type ^1{reportCommand} {target} <reason>^3 to report it to the admins."
    };

    public static Dictionary<string, string> DefaultUnresolvedReminderMessages() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "^1REMINDER:^3 Suspect {category}? Type ^1{reportCommand} <player name> <reason>^3 to send an official report to the admins."
    };

    public static List<string> DefaultCommunityReportPhrases() =>
    [
        "soft aimbot", "aimbotting", "aim lock", "aimlock", "magnetic aim", "wall hacks",
        "wallhacks", "wallhacking", "walls", "wh", "hallhack", "hallhacking", "esp", "chams",
        "red boxes", "redboxes", "tracking through walls", "tracking thru walls", "radar hack",
        "radar cheat"
    ];

    public static List<GuidanceCategoryConfig> DefaultCategories() =>
    [
        new()
        {
            Name = "cheating",
            Phrases =
            [
                "cheat", "cheater", "cheating", "cheats", "aimbot", "aim bot", "soft aim",
                "wallhack", "wall hack", "walling", "waller", "spinbot", "spin bot", "hacks",
                "hackers", "hacker", "hacking", "modding", "modded", "this guy is cheating",
                "he's cheating", "he is cheating"
            ]
        },
        new()
        {
            Name = "exploiting",
            Phrases = ["exploit", "exploits", "exploiting", "abusing an exploit", "bug abuse"]
        },
        new()
        {
            Name = "glitching",
            Phrases = ["glitch", "glitching", "under the map", "under map", "out of map", "outside the map"]
        }
    ];
}

public enum GuidanceConfigurationIssueSeverity
{
    Warning,
    Error
}

public sealed record GuidanceConfigurationIssue(GuidanceConfigurationIssueSeverity Severity, string Message);
