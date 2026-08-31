using Data.Models.Client;
using SharedLibraryCore;
using SharedLibraryCore.Commands;
using SharedLibraryCore.Configuration;
using SharedLibraryCore.Interfaces;

namespace ServerPulse;

public sealed class PlayerGuidanceStatusCommand : Command
{
    private readonly PlayerGuidanceService _service;
    private readonly AnalyticsEngine _analytics;

    public PlayerGuidanceStatusCommand(
        CommandConfiguration config,
        ITranslationLookup translationLookup,
        PlayerGuidanceService service,
        AnalyticsEngine analytics) : base(config, translationLookup)
    {
        _service = service;
        _analytics = analytics;
        Name = "ccmstatus";
        Alias = "ccms";
        Description = "shows ServerPulse player-guidance status";
        Permission = EFClient.Permission.Moderator;
        RequiresTarget = false;
    }

    public override Task ExecuteAsync(GameEvent gameEvent) => gameEvent.Origin.TellAsync(
        [_service.StatusSummary(_analytics.Snapshot())],
        gameEvent.Owner.Manager.CancellationToken);
}

public sealed class PlayerGuidanceStatsCommand : Command
{
    private readonly AnalyticsEngine _analytics;

    public PlayerGuidanceStatsCommand(
        CommandConfiguration config,
        ITranslationLookup translationLookup,
        AnalyticsEngine analytics) : base(config, translationLookup)
    {
        _analytics = analytics;
        Name = "ccmstats";
        Alias = "ccmst";
        Description = "shows retained ServerPulse player-guidance statistics";
        Permission = EFClient.Permission.Moderator;
        RequiresTarget = false;
    }

    public override Task ExecuteAsync(GameEvent gameEvent)
    {
        var events = _analytics.Snapshot().PlayerGuidanceEvents;
        var accusations = events.Where(item => item.EventType.Equals("Accusation", StringComparison.OrdinalIgnoreCase)).ToList();
        var reports = events.Count(item => item.EventType.Equals("Report", StringComparison.OrdinalIgnoreCase));
        var reminders = accusations.Count(item => item.Outcome.Contains("reminder sent", StringComparison.OrdinalIgnoreCase));
        var targets = accusations.Where(item => !string.IsNullOrWhiteSpace(item.TargetKey)).Select(item => item.TargetKey).Distinct().Count();
        var alerts = accusations.Count(item => item.StaffAlertSent);
        var categories = accusations.GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).Take(5).Select(group => $"{group.Key}={group.Count()}");
        return gameEvent.Origin.TellAsync(
            [
                $"ServerPulse guidance: accusations={accusations.Count}, reports={reports}, reminders={reminders}, resolved targets={targets}, staff alerts={alerts}.",
                accusations.Count == 0 ? "Guidance categories: no detections yet." : $"Guidance categories: {string.Join(", ", categories)}"
            ],
            gameEvent.Owner.Manager.CancellationToken);
    }
}

public sealed class PlayerGuidanceTestCommand : Command
{
    private readonly PlayerGuidanceService _service;

    public PlayerGuidanceTestCommand(
        CommandConfiguration config,
        ITranslationLookup translationLookup,
        PlayerGuidanceService service) : base(config, translationLookup)
    {
        _service = service;
        Name = "ccmtest";
        Alias = "ccmt";
        Description = "tests a message against ServerPulse player-guidance rules";
        Permission = EFClient.Permission.Moderator;
        RequiresTarget = false;
    }

    public override Task ExecuteAsync(GameEvent gameEvent)
    {
        if (string.IsNullOrWhiteSpace(gameEvent.Data))
            return gameEvent.Origin.TellAsync(["Usage: !ccmtest <message>"], gameEvent.Owner.Manager.CancellationToken);
        var analysis = _service.AnalyzeMessage(gameEvent.Data, gameEvent.Owner.Id, gameEvent.Origin, gameEvent.Owner.ConnectedClients);
        var result = analysis.Matched
            ? $"Guidance match: category={analysis.Category}, rule={analysis.Pattern}, target={analysis.TargetName ?? "unresolved"}. {analysis.Reason}"
            : $"Guidance no match: {analysis.Reason}";
        return gameEvent.Origin.TellAsync([result], gameEvent.Owner.Manager.CancellationToken);
    }
}

public sealed class PlayerGuidanceReloadCommand : Command
{
    private readonly PlayerGuidanceService _service;

    public PlayerGuidanceReloadCommand(
        CommandConfiguration config,
        ITranslationLookup translationLookup,
        PlayerGuidanceService service) : base(config, translationLookup)
    {
        _service = service;
        Name = "ccmreload";
        Alias = "ccmr";
        Description = "rebuilds ServerPulse player-guidance rules";
        Permission = EFClient.Permission.SeniorAdmin;
        RequiresTarget = false;
    }

    public override Task ExecuteAsync(GameEvent gameEvent)
    {
        _service.RefreshConfiguration();
        return gameEvent.Origin.TellAsync(
            [$"ServerPulse player-guidance rules refreshed with {_service.ConfigurationIssues.Count} configuration issue(s)."],
            gameEvent.Owner.Manager.CancellationToken);
    }
}
