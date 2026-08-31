namespace ServerPulse;

public sealed class RecommendationEngine
{
    private readonly ServerPulseConfig _config;

    public RecommendationEngine(ServerPulseConfig config) => _config = config;

    public IReadOnlyList<Recommendation> Build(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        if (!_config.EnableRecommendations)
            return [];

        var now = snapshot.GeneratedAt;
        var sessions = snapshot.Sessions.Where(item => item.StartedAt >= from && !item.IsBot).ToList();
        var rounds = snapshot.MapRounds.Where(item => item.StartedAt >= from && item.EndedAt is not null).ToList();
        var signals = snapshot.ChatSignals.Where(item => item.CapturedAt >= from).ToList();
        var recommendations = new List<Recommendation>();

        BuildServerRetention(sessions, recommendations);
        BuildRotationSignals(rounds, recommendations);
        BuildChatSignals(signals, from, now, recommendations);
        BuildAudienceOpportunity(sessions, recommendations);
        BuildOperationalWarnings(snapshot, recommendations);

        return recommendations
            .OrderBy(item => SeverityOrder(item.Severity))
            .ThenByDescending(item => item.Confidence)
            .ThenByDescending(item => item.SampleSize)
            .Take(12)
            .ToArray();
    }

    private void BuildServerRetention(
        IReadOnlyCollection<PlayerSessionRecord> sessions,
        ICollection<Recommendation> output)
    {
        foreach (var server in sessions.GroupBy(item => new { item.ServerId, item.ServerName }))
        {
            var complete = server.Where(item => item.EndedAt is not null).ToList();
            if (complete.Count < 10)
                continue;

            var bounced = complete.Count(item => item.DurationSeconds <= _config.BounceThresholdSeconds);
            var bounceRate = bounced * 100d / complete.Count;
            if (bounceRate < 35)
                continue;

            output.Add(new Recommendation(
                "High",
                "Players are leaving this server quickly",
                $"{AnalyticsEngine.CleanDisplayText(server.Key.ServerName)} loses {bounceRate:N0}% of completed sessions within {_config.BounceThresholdSeconds / 60d:N1} minutes.",
                "Compare its opening map, bot count and connection quality with the network average, then test one change at a time.",
                complete.Count,
                Confidence(complete.Count, 30)));
        }
    }

    private static void BuildRotationSignals(
        IReadOnlyCollection<MapRoundRecord> rounds,
        ICollection<Recommendation> output)
    {
        foreach (var rotation in rounds.GroupBy(item => new { item.Game, item.Map, item.Mode }))
        {
            var complete = rotation.ToList();
            if (complete.Count < 5)
                continue;

            var averageChange = complete.Average(item => item.PlayersAtEnd - item.PlayersAtStart);
            if (averageChange <= -1.5)
            {
                output.Add(new Recommendation(
                    "High",
                    "Rotation is losing players",
                    $"{Friendly(rotation.Key.Map)} · {Friendly(rotation.Key.Mode)} loses {Math.Abs(averageChange):N1} players per completed round on average.",
                    "Reduce its rotation weight or test the map with a different mode, then compare another five rounds.",
                    complete.Count,
                    Confidence(complete.Count, 20)));
            }
            else if (averageChange >= 1.5)
            {
                output.Add(new Recommendation(
                    "Opportunity",
                    "Rotation is attracting players",
                    $"{Friendly(rotation.Key.Map)} · {Friendly(rotation.Key.Mode)} gains {averageChange:N1} players per completed round on average.",
                    "Give this pairing a little more rotation weight and check that session length remains healthy.",
                    complete.Count,
                    Confidence(complete.Count, 20)));
            }
        }
    }

    private static void BuildChatSignals(
        IReadOnlyCollection<ChatSignalRecord> signals,
        DateTimeOffset from,
        DateTimeOffset now,
        ICollection<Recommendation> output)
    {
        var recentStart = now.AddHours(-24);
        var recent = signals.Where(item => item.CapturedAt >= recentStart).ToList();
        var baseline = signals.Where(item => item.CapturedAt >= from && item.CapturedAt < recentStart).ToList();
        var baselineDays = Math.Max(1, (recentStart - from).TotalDays);

        foreach (var category in recent.GroupBy(item => item.Category))
        {
            var recentMessages = UniqueMessageCount(category);
            var historicMessages = UniqueMessageCount(baseline.Where(item =>
                item.Category.Equals(category.Key, StringComparison.OrdinalIgnoreCase)));
            var expectedDaily = historicMessages / baselineDays;
            var hasBaseline = baseline.Count > 0;
            var elevated = hasBaseline
                ? recentMessages >= Math.Max(4, Math.Ceiling(expectedDaily * 1.75))
                : recentMessages >= 8;
            if (!elevated)
                continue;

            var positive = category.Key.Equals("Positive", StringComparison.OrdinalIgnoreCase);
            var title = positive ? "Positive feedback is rising" : $"{category.Key} reports are rising";
            var detail = hasBaseline
                ? $"{recentMessages:N0} player messages matched {category.Key.ToLowerInvariant()} in the last 24 hours, above the {expectedDaily:N1}-per-day baseline."
                : $"{recentMessages:N0} player messages matched {category.Key.ToLowerInvariant()} in the last 24 hours.";
            var action = positive
                ? "Read the recent excerpts to identify what players value and preserve it in future changes."
                : "Open Community voice, read the affected messages and identify the server, map or time pattern before changing anything.";

            output.Add(new Recommendation(
                positive ? "Opportunity" : "Medium",
                title,
                detail,
                action,
                recentMessages,
                Confidence(recentMessages, 20)));
        }
    }

    private void BuildAudienceOpportunity(
        IReadOnlyCollection<PlayerSessionRecord> sessions,
        ICollection<Recommendation> output)
    {
        var country = sessions
            .Where(item => !string.IsNullOrWhiteSpace(item.CountryCode))
            .GroupBy(item => new { item.CountryCode, item.CountryName })
            .OrderByDescending(group => group.Select(item => item.PlayerKey).Distinct().Count())
            .FirstOrDefault();
        if (country is null)
            return;

        var players = country.Select(item => item.PlayerKey).Distinct().Count();
        if (players < _config.MinimumCountrySampleSize)
            return;

        var peak = country.GroupBy(item => AnalyticsTime.Local(item.StartedAt).Hour)
            .OrderByDescending(group => group.Count())
            .First();
        output.Add(new Recommendation(
            "Opportunity",
            "Schedule around your leading audience",
            $"{country.Key.CountryName} supplied {players:N0} unique players and most sessions began around {peak.Key:00}:00 {AnalyticsTime.ConfigurationLabel}.",
            "Schedule a community event shortly before this hour and compare joins and session length against a normal day.",
            country.Count(),
            Confidence(country.Count(), 40)));
    }

    private static void BuildOperationalWarnings(
        DashboardSnapshot snapshot,
        ICollection<Recommendation> output)
    {
        var open = snapshot.Incidents.Where(item => item.ResolvedAt is null).ToList();
        if (open.Count == 0)
            return;

        output.Add(new Recommendation(
            "High",
            "Server monitoring needs attention",
            $"{open.Count:N0} monitoring or connectivity incident(s) are still open.",
            "Open Data health and resolve connectivity issues before interpreting traffic changes.",
            open.Count,
            95));
    }

    private static int UniqueMessageCount(IEnumerable<ChatSignalRecord> values) => values
        .Select(item => string.IsNullOrWhiteSpace(item.MessageId)
            ? $"{item.ServerId}|{item.PlayerKey}|{item.CapturedAt.UtcTicks}|{item.Excerpt}"
            : item.MessageId)
        .Distinct(StringComparer.Ordinal)
        .Count();

    private static int Confidence(int sample, int fullConfidenceSample) =>
        Math.Clamp((int)Math.Round(sample * 100d / Math.Max(1, fullConfidenceSample)), 20, 95);

    private static int SeverityOrder(string value) => value switch
    {
        "High" => 0,
        "Medium" => 1,
        _ => 2
    };

    private static string Friendly(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
}
