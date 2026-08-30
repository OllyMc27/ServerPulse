namespace ServerPulse;

public sealed class RecommendationEngine
{
    private readonly ServerPulseConfig _config;

    public RecommendationEngine(ServerPulseConfig config) => _config = config;

    public IReadOnlyList<Recommendation> Build(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        if (!_config.EnableRecommendations)
            return [];

        var sessions = snapshot.Sessions.Where(item => item.StartedAt >= from && !item.IsBot).ToList();
        var rounds = snapshot.MapRounds.Where(item => item.StartedAt >= from).ToList();
        var signals = snapshot.ChatSignals.Where(item => item.CapturedAt >= from).ToList();
        var recommendations = new List<Recommendation>();

        foreach (var server in sessions.GroupBy(item => new { item.ServerId, item.ServerName }))
        {
            var complete = server.Where(item => item.EndedAt is not null).ToList();
            if (complete.Count < 10)
                continue;
            var bounced = complete.Count(item => item.DurationSeconds <= _config.BounceThresholdSeconds);
            var bounceRate = bounced * 100d / complete.Count;
            if (bounceRate >= 35)
            {
                recommendations.Add(new Recommendation("High", "High short-session rate",
                    $"{server.Key.ServerName} loses {bounceRate:N0}% of completed sessions within {_config.BounceThresholdSeconds / 60d:N1} minutes.",
                    "Check the opening map, bot count, connection quality and welcome experience.", complete.Count,
                    Confidence(complete.Count, 25)));
            }
        }

        foreach (var map in rounds.GroupBy(item => new { item.Game, item.Map, item.Mode }))
        {
            var complete = map.Where(item => item.EndedAt is not null).ToList();
            if (complete.Count < 3)
                continue;
            var averageChange = complete.Average(item => item.PlayersAtEnd - item.PlayersAtStart);
            if (averageChange <= -2)
            {
                recommendations.Add(new Recommendation("High", "Rotation retention warning",
                    $"{Friendly(map.Key.Map)} · {Friendly(map.Key.Mode)} loses {Math.Abs(averageChange):N1} players per completed round on average.",
                    "Reduce its rotation weight or test a different mode pairing.", complete.Count,
                    Confidence(complete.Count, 12)));
            }
        }

        var recentSignals = signals.Where(item => item.CapturedAt >= DateTimeOffset.UtcNow.AddHours(-24)).ToList();
        var baselineDays = Math.Max(1, (DateTimeOffset.UtcNow - from).TotalDays);
        foreach (var category in recentSignals.GroupBy(item => item.Category))
        {
            var historic = signals.Count(item => item.Category.Equals(category.Key, StringComparison.OrdinalIgnoreCase));
            var expectedDaily = Math.Max(1, historic / baselineDays);
            if (category.Count() >= Math.Max(5, expectedDaily * 2))
            {
                recommendations.Add(new Recommendation("Medium", $"{category.Key} complaints are elevated",
                    $"{category.Count():N0} signals were recorded in the last 24 hours, above the recent baseline.",
                    "Review the affected servers and the anonymised time distribution.", category.Count(),
                    Confidence(category.Count(), 20)));
            }
        }

        var countries = sessions.Where(item => !string.IsNullOrWhiteSpace(item.CountryCode))
            .GroupBy(item => new { item.CountryCode, item.CountryName })
            .OrderByDescending(group => group.Select(item => item.PlayerKey).Distinct().Count())
            .FirstOrDefault();
        if (countries is not null && countries.Count() >= _config.MinimumCountrySampleSize)
        {
            var peak = countries.GroupBy(item => AnalyticsTime.Local(item.StartedAt).Hour)
                .OrderByDescending(group => group.Count()).First();
            recommendations.Add(new Recommendation("Opportunity", "Regional event opportunity",
                $"{countries.Key.CountryName} is the leading audience and most often joins around {peak.Key:00}:00 local dashboard time.",
                "Schedule a community event shortly before this period and measure the annotated result.", countries.Count(),
                Confidence(countries.Count(), 40)));
        }

        return recommendations.OrderBy(item => SeverityOrder(item.Severity)).ThenByDescending(item => item.Confidence)
            .Take(12).ToArray();
    }

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
