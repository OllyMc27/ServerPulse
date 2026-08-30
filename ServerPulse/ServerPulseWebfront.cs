using System.Net;
using System.Text;
using SharedLibraryCore.Helpers;
using SharedLibraryCore.Interfaces;

namespace ServerPulse;

public sealed class ServerPulseWebfront : IDisposable
{
    public const string InteractionKey = "Webfront::Nav::Admin::ServerPulse";
    private const string Styles = """
        <style>
          .sp-shell{width:min(1680px,calc(100vw - 18rem));position:relative;left:50%;transform:translateX(-50%)}
          .sp-kpis{display:grid;grid-template-columns:repeat(5,minmax(0,1fr))}
          .sp-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(15rem,1fr));gap:1rem}
          .sp-two{display:grid;grid-template-columns:minmax(0,1.5fr) minmax(18rem,.5fr);gap:1rem}
          .sp-heat{display:grid;grid-template-columns:4.5rem repeat(24,minmax(1.25rem,1fr));gap:2px;align-items:center}
          .sp-heat-cell{height:1.65rem;border-radius:.2rem;background:color-mix(in srgb,var(--color-primary) calc(var(--heat)*1%),transparent)}
          @media(max-width:1100px){.sp-shell{width:auto;left:auto;transform:none}.sp-kpis{grid-template-columns:repeat(2,1fr)}.sp-two{grid-template-columns:1fr}}
        </style>
        """;

    private readonly IInteractionRegistration _interactions;
    private readonly IConfigurationHandlerV2<ServerPulseConfig> _configurationHandler;
    private readonly ServerPulseConfig _config;
    private readonly AnalyticsEngine _engine;
    private readonly AnalyticsStore _store;
    private readonly RecommendationEngine _recommendations;
    private bool _disposed;

    public ServerPulseWebfront(
        IInteractionRegistration interactions,
        IConfigurationHandlerV2<ServerPulseConfig> configurationHandler,
        ServerPulseConfig config,
        AnalyticsEngine engine,
        AnalyticsStore store,
        RecommendationEngine recommendations)
    {
        _interactions = interactions;
        _configurationHandler = configurationHandler;
        _config = config;
        _engine = engine;
        _store = store;
        _recommendations = recommendations;
        _configurationHandler.Updated += OnConfigurationUpdated;
    }

    public void Register()
    {
        _interactions.UnregisterInteraction(InteractionKey);
        if (!_config.EnableWebfrontDashboard)
            return;

        _interactions.RegisterInteraction(InteractionKey, (_, _, _) =>
        {
            IInteractionData interaction = new InteractionData
            {
                Enabled = true,
                Name = "ServerPulse",
                Description = "Server population, retention, maps and community analytics",
                DisplayMeta = "ph-chart-line-up",
                InteractionId = InteractionKey,
                MinimumPermission = _config.WebfrontMinimumPermission,
                InteractionType = InteractionType.TemplateContent,
                Source = "ServerPulse",
                PermissionEntity = "Interaction",
                PermissionAccess = "Read",
                Action = (_, _, _, meta, _) => Task.FromResult(Render(meta))
            };
            return Task.FromResult(interaction);
        });
    }

    private string Render(IDictionary<string, string> meta)
    {
        var view = meta.TryGetValue("view", out var requested) ? requested.ToLowerInvariant() : "overview";
        var days = meta.TryGetValue("period", out var period) && int.TryParse(period, out var parsed)
            ? Math.Clamp(parsed, 1, 365)
            : 30;
        var snapshot = _engine.Snapshot();
        var from = DateTimeOffset.UtcNow.AddDays(-days);
        var builder = new StringBuilder(Styles);
        builder.Append("<div class=\"sp-shell space-y-5\">")
            .Append(Header(view, days))
            .Append(view switch
            {
                "servers" => Servers(snapshot, from),
                "maps" => Maps(snapshot, from),
                "activity" => Activity(snapshot, from),
                "audience" => Audience(snapshot, from),
                "chat" => Chat(snapshot, from),
                "recommendations" => Recommendations(snapshot, from),
                "health" => Health(snapshot),
                _ => Overview(snapshot, from)
            })
            .Append("</div>");
        return builder.ToString();
    }

    private string Header(string view, int days)
    {
        var builder = new StringBuilder();
        builder.Append("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"flex flex-col gap-4 border-b border-line px-6 py-5 lg:flex-row lg:items-center lg:justify-between\"><div class=\"flex items-center gap-4\"><div class=\"flex h-12 w-12 items-center justify-center rounded-xl border border-primary/30 bg-primary/10\"><i class=\"ph ph-chart-line-up text-2xl text-primary\"></i></div><div><h2 class=\"text-xl font-semibold text-foreground\">ServerPulse analytics</h2><p class=\"mt-1 text-sm text-muted\">Turn population, retention and community signals into practical growth decisions.</p></div></div>")
            .Append("<div class=\"flex flex-wrap gap-2\">");
        foreach (var value in new[] { 1, 7, 30, 90 })
            builder.Append($"<a data-enhance-nav=\"false\" class=\"rounded-lg border px-3 py-2 text-sm font-medium {(days == value ? "border-primary bg-primary/10 text-primary" : "border-line bg-surface-alt text-muted hover:text-foreground")}\" href=\"{Url(view, value)}\">{(value == 1 ? "24 hours" : $"{value} days")}</a>");
        builder.Append("</div></div><nav class=\"flex gap-1 overflow-x-auto bg-surface-alt/30 px-4 py-2\" aria-label=\"ServerPulse sections\">");
        foreach (var tab in Tabs)
            builder.Append(Tab(tab.Key, tab.Value.Label, tab.Value.Icon, view, days));
        return builder.Append("</nav></section>").ToString();
    }

    private string Overview(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var sessions = HumanSessions(snapshot, from);
        var completed = sessions.Where(item => item.EndedAt is not null).ToList();
        var unique = sessions.Select(item => item.PlayerKey).Distinct().Count();
        var returning = sessions.GroupBy(item => item.PlayerKey).Count(group => group.Count() > 1);
        var avgMinutes = completed.Count == 0 ? 0 : completed.Average(item => item.DurationSeconds) / 60d;
        var bounce = completed.Count == 0 ? 0 : completed.Count(item => item.DurationSeconds <= _config.BounceThresholdSeconds) * 100d / completed.Count;
        var latest = LatestSamples(snapshot);
        var online = latest.Sum(item => item.HumanPlayers);
        var peak = snapshot.PopulationSamples.Where(item => item.CapturedAt >= from).DefaultIfEmpty().Max(item => item?.HumanPlayers ?? 0);
        var bestServer = sessions.GroupBy(item => item.ServerName).OrderByDescending(group => group.Select(item => item.PlayerKey).Distinct().Count()).FirstOrDefault();
        var bestMap = snapshot.MapRounds.Where(item => item.StartedAt >= from).GroupBy(item => new { item.Map, item.Mode })
            .OrderByDescending(group => group.Sum(item => item.Joins)).FirstOrDefault();

        var builder = new StringBuilder();
        builder.Append("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"sp-kpis divide-x divide-line\">")
            .Append(Kpi("Online now", online, "ph-users-three", "text-emerald-400"))
            .Append(Kpi("Unique players", unique, "ph-identification-card", "text-primary"))
            .Append(Kpi("Average session", $"{avgMinutes:N1}m", "ph-clock", "text-sky-400"))
            .Append(Kpi("Returning players", unique == 0 ? "0%" : $"{returning * 100d / unique:N0}%", "ph-arrow-u-up-left", "text-violet-400"))
            .Append(Kpi("Short sessions", $"{bounce:N0}%", "ph-sign-out", bounce >= 35 ? "text-red-400" : "text-amber-400"))
            .Append("</div></section><div class=\"sp-grid\">")
            .Append(FeatureCard("Peak population", peak.ToString("N0"), "Highest simultaneous human population in this period.", "ph-trend-up", "text-emerald-400"))
            .Append(FeatureCard("Top server", bestServer?.Key ?? "Collecting data", bestServer is null ? "No completed sessions yet." : $"{bestServer.Select(item => item.PlayerKey).Distinct().Count():N0} unique players", "ph-hard-drives", "text-primary"))
            .Append(FeatureCard("Top rotation", bestMap is null ? "Collecting data" : $"{E(bestMap.Key.Map)} · {E(bestMap.Key.Mode)}", bestMap is null ? "No completed matches yet." : $"{bestMap.Sum(item => item.Joins):N0} recorded joins", "ph-map-trifold", "text-sky-400"))
            .Append(FeatureCard("Data health", snapshot.LastError is null ? "Healthy" : "Attention needed", snapshot.LastError ?? $"{snapshot.Sessions.Count:N0} sessions retained", snapshot.LastError is null ? "ph-check-circle" : "ph-warning", snapshot.LastError is null ? "text-emerald-400" : "text-red-400"))
            .Append("</div>")
            .Append(RecommendationsPreview(snapshot, from));
        return builder.ToString();
    }

    private string Servers(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var sessions = HumanSessions(snapshot, from);
        var latest = LatestSamples(snapshot).ToDictionary(item => item.ServerId, StringComparer.OrdinalIgnoreCase);
        var rows = sessions.GroupBy(item => new { item.ServerId, item.ServerName, item.Game })
            .Select(group =>
            {
                var completed = group.Where(item => item.EndedAt is not null).ToList();
                var unique = group.Select(item => item.PlayerKey).Distinct().Count();
                var returning = group.GroupBy(item => item.PlayerKey).Count(item => item.Count() > 1);
                var average = completed.Count == 0 ? 0 : completed.Average(item => item.DurationSeconds) / 60d;
                var bounce = completed.Count == 0 ? 0 : completed.Count(item => item.DurationSeconds <= _config.BounceThresholdSeconds) * 100d / completed.Count;
                latest.TryGetValue(group.Key.ServerId, out var live);
                var score = Math.Round(unique * 2 + average + returning * 3 - bounce / 5, 1);
                return new { group.Key.ServerId, group.Key.ServerName, group.Key.Game, Online = live?.HumanPlayers ?? 0, Unique = unique, Average = average, Returning = unique == 0 ? 0 : returning * 100d / unique, Bounce = bounce, Score = score };
            }).OrderByDescending(item => item.Score).ToList();
        return TableSection("Server leaderboard", "Human-only demand, retention and session quality.",
            ["Server", "Game", "Online", "Unique", "Avg session", "Returning", "Short sessions", "Health score"],
            rows.Select(item => new[] { E(item.ServerName), E(item.Game), item.Online.ToString("N0"), item.Unique.ToString("N0"), $"{item.Average:N1}m", $"{item.Returning:N0}%", $"{item.Bounce:N0}%", item.Score.ToString("N1") }));
    }

    private string Maps(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var rows = snapshot.MapRounds.Where(item => item.StartedAt >= from && item.EndedAt is not null)
            .GroupBy(item => new { item.Game, item.Map, item.Mode })
            .Select(group => new
            {
                group.Key.Game, group.Key.Map, group.Key.Mode, Rounds = group.Count(),
                AverageStart = group.Average(item => item.PlayersAtStart),
                AverageEnd = group.Average(item => item.PlayersAtEnd),
                AveragePeak = group.Average(item => item.PeakPlayers),
                Joins = group.Sum(item => item.Joins),
                Leaves = group.Sum(item => item.Leaves),
                Survival = group.Sum(item => item.PlayersAtStart) == 0 ? 0 : Math.Min(100, group.Sum(item => item.PlayersAtEnd) * 100d / group.Sum(item => item.PlayersAtStart))
            }).OrderByDescending(item => item.Survival).ThenByDescending(item => item.Rounds).ToList();
        return TableSection("Map and mode performance", "Population movement and map survival across completed rounds.",
            ["Map", "Mode", "Game", "Rounds", "Start", "Peak", "End", "Joins", "Leaves", "Survival"],
            rows.Select(item => new[] { E(item.Map), E(item.Mode), E(item.Game), item.Rounds.ToString("N0"), item.AverageStart.ToString("N1"), item.AveragePeak.ToString("N1"), item.AverageEnd.ToString("N1"), item.Joins.ToString("N0"), item.Leaves.ToString("N0"), $"{item.Survival:N0}%" }));
    }

    private string Activity(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var samples = snapshot.PopulationSamples.Where(item => item.CapturedAt >= from).ToList();
        var cells = samples.GroupBy(item => new { Day = (int)ServerTime(item.ServerId, item.CapturedAt).DayOfWeek, ServerTime(item.ServerId, item.CapturedAt).Hour })
            .ToDictionary(group => (group.Key.Day, group.Key.Hour), group => group.Average(item => item.HumanPlayers));
        var maximum = Math.Max(1, cells.Values.DefaultIfEmpty(0).Max());
        var builder = new StringBuilder("<section class=\"overflow-x-auto rounded-xl border border-line bg-surface p-5 shadow-sm\"><h3 class=\"text-lg font-semibold text-foreground\">Activity heatmap</h3><p class=\"mt-1 mb-5 text-sm text-muted\">Average human population by local dashboard time. Brighter cells are busier.</p><div class=\"min-w-[980px] space-y-1\">");
        builder.Append("<div class=\"sp-heat text-[10px] text-muted\"><span></span>");
        for (var hour = 0; hour < 24; hour++) builder.Append($"<span class=\"text-center\">{hour:00}</span>");
        builder.Append("</div>");
        foreach (var day in Enumerable.Range(1, 7).Select(value => value % 7))
        {
            builder.Append("<div class=\"sp-heat\"><span class=\"text-xs text-muted\">").Append(((DayOfWeek)day).ToString()[..3]).Append("</span>");
            for (var hour = 0; hour < 24; hour++)
            {
                var value = cells.GetValueOrDefault((day, hour));
                var heat = Math.Round(value * 100 / maximum);
                builder.Append($"<div class=\"sp-heat-cell\" style=\"--heat:{heat}\" title=\"{E(((DayOfWeek)day).ToString())} {hour:00}:00 · {value:N1} players\"></div>");
            }
            builder.Append("</div>");
        }
        builder.Append("</div></section>");
        var disconnects = HumanSessions(snapshot, from).Where(item => item.EndedAt is not null)
            .GroupBy(item => item.DisconnectReason)
            .Select(group => new
            {
                Reason = group.Key,
                Sessions = group.Count(),
                Average = group.Average(item => item.DurationSeconds) / 60d,
                Share = group.Count() * 100d / Math.Max(1, snapshot.Sessions.Count(item => item.StartedAt >= from && !item.IsBot && item.EndedAt is not null))
            }).OrderByDescending(item => item.Sessions);
        return builder.Append(TableSection("Disconnect outcomes", "Why completed player sessions ended, with average session length.",
            ["Reason", "Sessions", "Share", "Avg session"],
            disconnects.Select(item => new[] { E(item.Reason), item.Sessions.ToString("N0"), $"{item.Share:N0}%", $"{item.Average:N1}m" }))).ToString();
    }

    private string Audience(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var rows = HumanSessions(snapshot, from).Where(item => !string.IsNullOrWhiteSpace(item.CountryCode))
            .GroupBy(item => new { item.CountryCode, item.CountryName })
            .Select(group => new
            {
                group.Key.CountryCode, group.Key.CountryName,
                Players = group.Select(item => item.PlayerKey).Distinct().Count(),
                Sessions = group.Count(),
                Average = group.Where(item => item.EndedAt is not null).DefaultIfEmpty().Average(item => item is null ? 0 : item.DurationSeconds) / 60d,
                PeakHour = group.GroupBy(item => ServerTime(item.ServerId, item.StartedAt).Hour).OrderByDescending(item => item.Count()).First().Key
            }).Where(item => item.Players >= _config.MinimumCountrySampleSize).OrderByDescending(item => item.Players).ToList();
        return TableSection("Player audience", $"Countries below {_config.MinimumCountrySampleSize:N0} unique players are hidden for privacy.",
            ["Country", "Code", "Unique players", "Sessions", "Avg session", "Popular time"],
            rows.Select(item => new[] { E(item.CountryName), E(item.CountryCode), item.Players.ToString("N0"), item.Sessions.ToString("N0"), $"{item.Average:N1}m", $"{item.PeakHour:00}:00" }));
    }

    private string Chat(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var signals = snapshot.ChatSignals.Where(item => item.CapturedAt >= from).ToList();
        var categories = signals.GroupBy(item => item.Category).OrderByDescending(item => item.Count()).ToList();
        var builder = new StringBuilder("<div class=\"sp-two\"><section class=\"rounded-xl border border-line bg-surface p-5 shadow-sm\"><h3 class=\"text-lg font-semibold text-foreground\">Community signals</h3><p class=\"mt-1 mb-5 text-sm text-muted\">Categorised complaints, requests and positive feedback. Raw chat is not retained by default.</p><div class=\"space-y-4\">");
        var max = Math.Max(1, categories.Select(item => item.Count()).DefaultIfEmpty(0).Max());
        foreach (var category in categories)
        {
            var width = category.Count() * 100d / max;
            builder.Append($"<div><div class=\"mb-1 flex justify-between text-sm\"><span class=\"font-medium text-foreground\">{E(category.Key)}</span><span class=\"text-muted\">{category.Count():N0}</span></div><div class=\"h-2 overflow-hidden rounded-full bg-surface-alt\"><div class=\"h-full rounded-full bg-primary\" style=\"width:{width:N1}%\"></div></div></div>");
        }
        builder.Append("</div></section><section class=\"rounded-xl border border-line bg-surface p-5 shadow-sm\"><h3 class=\"font-semibold text-foreground\">Recent redacted examples</h3><div class=\"mt-4 space-y-3\">");
        var excerpts = signals.Where(item => !string.IsNullOrWhiteSpace(item.Excerpt)).OrderByDescending(item => item.CapturedAt).Take(12).ToList();
        if (excerpts.Count == 0)
            builder.Append("<p class=\"text-sm text-muted\">Excerpt retention is disabled. Category counts remain available.</p>");
        foreach (var signal in excerpts)
            builder.Append($"<div class=\"rounded-lg border border-line bg-surface-alt/20 p-3\"><div class=\"flex justify-between gap-3 text-xs text-muted\"><span>{E(signal.Category)} · {E(signal.ServerName)}</span><span>{E(AnalyticsTime.Short(signal.CapturedAt))}</span></div><p class=\"mt-2 text-sm text-foreground\">{E(signal.Excerpt)}</p></div>");
        return builder.Append("</div></section></div>").ToString();
    }

    private string Recommendations(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var values = _recommendations.Build(snapshot, from);
        var builder = new StringBuilder("<section class=\"rounded-xl border border-line bg-surface p-5 shadow-sm\"><h3 class=\"text-lg font-semibold text-foreground\">Opportunity feed</h3><p class=\"mt-1 mb-5 text-sm text-muted\">Prioritised actions derived from the selected period, with sample size and confidence shown.</p><div class=\"sp-grid\">");
        if (values.Count == 0)
            builder.Append("<div class=\"rounded-lg border border-dashed border-line p-8 text-center text-sm text-muted\">More sessions and completed matches are needed before recommendations can be generated.</div>");
        foreach (var item in values)
        {
            var color = item.Severity == "High" ? "text-red-400" : item.Severity == "Medium" ? "text-amber-400" : "text-emerald-400";
            builder.Append($"<article class=\"rounded-xl border border-line bg-surface-alt/20 p-4\"><div class=\"flex items-start justify-between gap-3\"><span class=\"text-xs font-semibold uppercase tracking-wide {color}\">{E(item.Severity)}</span><span class=\"text-xs text-muted\">{item.Confidence:N0}% confidence</span></div><h4 class=\"mt-3 font-semibold text-foreground\">{E(item.Title)}</h4><p class=\"mt-2 text-sm text-muted\">{E(item.Detail)}</p><p class=\"mt-3 text-sm text-foreground\"><strong>Suggested action:</strong> {E(item.Action)}</p><p class=\"mt-3 text-xs text-muted\">Sample: {item.SampleSize:N0}</p></article>");
        }
        return builder.Append("</div></section>").ToString();
    }

    private string Health(DashboardSnapshot snapshot)
    {
        var unresolved = snapshot.Incidents.Where(item => item.ResolvedAt is null).OrderByDescending(item => item.StartedAt).ToList();
        var latest = LatestSamples(snapshot);
        var issues = ConfigurationIssues();
        var builder = new StringBuilder("<div class=\"sp-grid\">")
            .Append(FeatureCard("Startup self-test", issues.Count == 0 ? "Configuration valid" : $"{issues.Count:N0} issue(s)", issues.Count == 0 ? "Timezone, retention, privacy and storage settings passed validation." : string.Join(" · ", issues), issues.Count == 0 ? "ph-check-circle" : "ph-warning", issues.Count == 0 ? "text-emerald-400" : "text-amber-400"))
            .Append(FeatureCard("Storage", snapshot.LastError is null ? "Healthy" : "Write error", snapshot.LastError ?? $"Updated {AnalyticsTime.Display(snapshot.GeneratedAt)}", snapshot.LastError is null ? "ph-database" : "ph-warning", snapshot.LastError is null ? "text-emerald-400" : "text-red-400"))
            .Append(FeatureCard("Sessions", snapshot.Sessions.Count.ToString("N0"), $"Maximum {_config.MaxSessions:N0} · {_config.AggregateRetentionDays:N0} day aggregate retention", "ph-users", "text-primary"))
            .Append(FeatureCard("Population samples", snapshot.PopulationSamples.Count.ToString("N0"), $"Every {Math.Clamp(_config.PopulationSnapshotSeconds, 15, 900):N0} seconds", "ph-waveform", "text-sky-400"))
            .Append(FeatureCard("Chat privacy", _config.StoreRawChat ? "Raw excerpts enabled" : _config.StoreRedactedChatExcerpts ? "Redacted excerpts" : "Counts only", $"{snapshot.ChatSignals.Count:N0} categorised signals retained", "ph-shield-check", _config.StoreRawChat ? "text-amber-400" : "text-emerald-400"))
            .Append("</div>")
            .Append(TableSection("Live server telemetry", "Latest native IW4MAdmin status and latency measurements.",
                ["Server", "Game", "Map", "Mode", "Humans", "Bots", "RCON", "Event pipeline", "Captured"],
                latest.Select(item => new[] { E(item.ServerName), E(item.Game), E(item.Map), E(item.Mode), item.HumanPlayers.ToString("N0"), item.BotPlayers.ToString("N0"), item.RconLatencyMilliseconds <= 0 ? "—" : $"{item.RconLatencyMilliseconds:N0} ms", item.EventLatencyMilliseconds <= 0 ? "—" : $"{item.EventLatencyMilliseconds:N0} ms", E(AnalyticsTime.Short(item.CapturedAt)) })))
            .Append(TableSection("Open incidents", "Monitoring and connectivity interruptions that have not yet recovered.",
                ["Server", "Incident", "Started"],
                unresolved.Select(item => new[] { E(item.ServerName), E(item.Type), E(AnalyticsTime.Display(item.StartedAt)) })));
        return builder.ToString();
    }

    private DateTimeOffset ServerTime(string serverId, DateTimeOffset value) =>
        _config.ServerOverrides.TryGetValue(serverId, out var server) && !string.IsNullOrWhiteSpace(server.TimeZone)
            ? AnalyticsTime.Local(value, server.TimeZone)
            : AnalyticsTime.Local(value);

    private IReadOnlyList<string> ConfigurationIssues()
    {
        var issues = new List<string>();
        if (!ValidTimeZone(_config.TimeZone)) issues.Add($"Unknown timezone: {_config.TimeZone}");
        foreach (var value in _config.ServerOverrides.Where(item => !string.IsNullOrWhiteSpace(item.Value.TimeZone) && !ValidTimeZone(item.Value.TimeZone)))
            issues.Add($"Unknown timezone for {value.Key}");
        if (_config.RawDataRetentionDays > _config.AggregateRetentionDays) issues.Add("Raw retention exceeds aggregate retention");
        if (_config.PopulationSnapshotSeconds is < 15 or > 900) issues.Add("Snapshot interval will be clamped to 15–900 seconds");
        if (_config.ChatCategories.Count == 0) issues.Add("No chat categories configured");
        if (_config.StoreRawChat) issues.Add("Raw chat storage is enabled");
        if (_config.AnonymizationSalt.Length < 16) issues.Add("Anonymization salt is too short");
        return issues;
    }

    private static bool ValidTimeZone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(value); return true; }
        catch { return false; }
    }

    private string RecommendationsPreview(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var values = _recommendations.Build(snapshot, from).Take(3).ToList();
        if (values.Count == 0)
            return string.Empty;
        var builder = new StringBuilder("<section class=\"rounded-xl border border-line bg-surface p-5 shadow-sm\"><div class=\"mb-4 flex items-center justify-between\"><div><h3 class=\"font-semibold text-foreground\">Priority opportunities</h3><p class=\"mt-1 text-sm text-muted\">The strongest actions found in this period.</p></div><a data-enhance-nav=\"false\" href=\"").Append(Url("recommendations", (int)Math.Max(1, (DateTimeOffset.UtcNow - from).TotalDays))).Append("\" class=\"text-sm font-medium text-primary hover:underline\">View all</a></div><div class=\"sp-grid\">");
        foreach (var item in values)
            builder.Append($"<div class=\"rounded-lg border border-line bg-surface-alt/20 p-4\"><div class=\"text-xs font-semibold uppercase tracking-wide text-amber-400\">{E(item.Severity)} · {item.Confidence:N0}% confidence</div><h4 class=\"mt-2 font-semibold text-foreground\">{E(item.Title)}</h4><p class=\"mt-2 text-sm text-muted\">{E(item.Detail)}</p></div>");
        return builder.Append("</div></section>").ToString();
    }

    private static IReadOnlyList<PlayerSessionRecord> HumanSessions(DashboardSnapshot snapshot, DateTimeOffset from) =>
        snapshot.Sessions.Where(item => item.StartedAt >= from && !item.IsBot).ToList();

    private static IReadOnlyList<PopulationSampleRecord> LatestSamples(DashboardSnapshot snapshot) =>
        snapshot.PopulationSamples.GroupBy(item => item.ServerId)
            .Select(group => group.OrderByDescending(item => item.CapturedAt).First())
            .Where(item => item.CapturedAt >= DateTimeOffset.UtcNow.AddMinutes(-10)).ToList();

    private static string TableSection(string title, string description, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var values = rows.ToList();
        var builder = new StringBuilder($"<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"border-b border-line px-5 py-4\"><h3 class=\"font-semibold text-foreground\">{E(title)}</h3><p class=\"mt-1 text-sm text-muted\">{E(description)}</p></div><div class=\"overflow-x-auto\"><table class=\"w-full min-w-[760px] text-left\"><thead class=\"bg-surface-alt/30 text-xs uppercase tracking-wide text-muted\"><tr>");
        foreach (var header in headers) builder.Append($"<th class=\"px-5 py-3 font-medium\">{E(header)}</th>");
        builder.Append("</tr></thead><tbody class=\"divide-y divide-line\">");
        if (values.Count == 0)
            builder.Append($"<tr><td colspan=\"{headers.Count}\" class=\"px-5 py-12 text-center text-sm text-muted\">No data is available for this period yet.</td></tr>");
        foreach (var row in values)
        {
            builder.Append("<tr class=\"hover:bg-surface-hover\">");
            foreach (var cell in row) builder.Append($"<td class=\"px-5 py-3 text-sm text-foreground\">{cell}</td>");
            builder.Append("</tr>");
        }
        return builder.Append("</tbody></table></div></section>").ToString();
    }

    private static string Kpi(string label, object value, string icon, string color) =>
        $"<div class=\"min-w-0 px-5 py-5\"><div class=\"flex items-center justify-between gap-3\"><div class=\"text-2xl font-bold text-foreground\">{E(value)}</div><i class=\"ph {E(icon)} text-xl {E(color)}\"></i></div><div class=\"mt-1 truncate text-xs uppercase tracking-wide text-muted\">{E(label)}</div></div>";

    private static string FeatureCard(string label, string value, string detail, string icon, string color) =>
        $"<section class=\"rounded-xl border border-line bg-surface p-5 shadow-sm\"><div class=\"flex items-start justify-between gap-3\"><div><div class=\"text-xs uppercase tracking-wide text-muted\">{E(label)}</div><div class=\"mt-2 text-lg font-semibold text-foreground\">{E(value)}</div></div><i class=\"ph {E(icon)} text-2xl {E(color)}\"></i></div><p class=\"mt-3 text-sm text-muted\">{E(detail)}</p></section>";

    private static string Tab(string key, string label, string icon, string active, int days) =>
        $"<a data-enhance-nav=\"false\" href=\"{Url(key, days)}\" class=\"inline-flex shrink-0 items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium {(key == active ? "bg-action-primary text-white" : "text-muted hover:bg-surface-hover hover:text-foreground")}\"><i class=\"ph {E(icon)}\"></i>{E(label)}</a>";

    private static string Url(string view, int days) =>
        $"/Interaction/Render/{InteractionKey}?view={WebUtility.UrlEncode(view)}&period={days}";

    private static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? string.Empty);

    private static readonly IReadOnlyDictionary<string, (string Label, string Icon)> Tabs =
        new Dictionary<string, (string, string)>
        {
            ["overview"] = ("Overview", "ph-squares-four"),
            ["servers"] = ("Servers", "ph-hard-drives"),
            ["maps"] = ("Maps & modes", "ph-map-trifold"),
            ["activity"] = ("Activity", "ph-calendar-dots"),
            ["audience"] = ("Audience", "ph-globe-hemisphere-west"),
            ["chat"] = ("Chat signals", "ph-chats-circle"),
            ["recommendations"] = ("Recommendations", "ph-lightbulb"),
            ["health"] = ("Data health", "ph-heartbeat")
        };

    private void OnConfigurationUpdated(ServerPulseConfig _) => Register();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _configurationHandler.Updated -= OnConfigurationUpdated;
        _interactions.UnregisterInteraction(InteractionKey);
    }
}
