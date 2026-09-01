using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using SharedLibraryCore.Helpers;
using SharedLibraryCore.Interfaces;

namespace ServerPulse;

public sealed class ServerPulseWebfront : IDisposable
{
    public const string InteractionKey = "Webfront::Nav::Admin::ServerPulse";
    private const int RotationPageSize = 25;
    private const int ChatPageSize = 20;
    private const string Styles = """
        <style>
          .max-w-7xl:has(.sp-workspace)>div.flex.items-center.gap-3.mb-8{display:none}
          .sp-status-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:.75rem}
          .sp-explore-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1px;background:var(--color-line)}
          .sp-summary-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:.75rem}
          .sp-two{display:grid;grid-template-columns:minmax(0,1.5fr) minmax(19rem,.5fr);gap:1rem}
          .sp-heat{display:grid;grid-template-columns:3.25rem repeat(24,minmax(1rem,1fr));gap:3px;align-items:center}
          .sp-heat-cell{height:1.55rem;border:1px solid color-mix(in srgb,var(--color-line) 65%,transparent);border-radius:.2rem;background:color-mix(in srgb,var(--color-primary) calc(var(--heat)*1%),var(--color-surface-alt))}
          .sp-quote{border-left:3px solid color-mix(in srgb,var(--color-primary) 65%,transparent)}
          @media (min-width:1280px){.sp-workspace{width:min(1600px,calc(100vw - 19rem));position:relative;left:50%;transform:translateX(-50%)}}
          @media (max-width:1023px){.sp-status-grid,.sp-summary-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.sp-explore-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.sp-two{grid-template-columns:1fr}}
          @media (max-width:560px){.sp-status-grid,.sp-summary-grid,.sp-explore-grid{grid-template-columns:1fr}}
        </style>
        """;

    private readonly IInteractionRegistration _interactions;
    private readonly IConfigurationHandlerV2<ServerPulseConfig> _configurationHandler;
    private readonly ServerPulseConfig _config;
    private readonly AnalyticsEngine _engine;
    private readonly RecommendationEngine _recommendations;
    private readonly PlayerGuidanceService _playerGuidance;
    private readonly IAntiforgery _antiforgery;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private bool _disposed;

    public ServerPulseWebfront(
        IInteractionRegistration interactions,
        IConfigurationHandlerV2<ServerPulseConfig> configurationHandler,
        ServerPulseConfig config,
        AnalyticsEngine engine,
        RecommendationEngine recommendations,
        PlayerGuidanceService playerGuidance,
        IAntiforgery antiforgery,
        IHttpContextAccessor httpContextAccessor)
    {
        _interactions = interactions;
        _configurationHandler = configurationHandler;
        _config = config;
        _engine = engine;
        _recommendations = recommendations;
        _playerGuidance = playerGuidance;
        _antiforgery = antiforgery;
        _httpContextAccessor = httpContextAccessor;
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
                Description = "Traffic, retention and community intelligence",
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
        var view = NormalizeView(ReadValue(meta, "view"));
        var days = ReadInteger(meta, "period", 30, 1, 365);
        var from = DateTimeOffset.UtcNow.AddDays(-days);
        var snapshot = _engine.Snapshot();
        var builder = new StringBuilder(Styles).Append("<div class=\"sp-workspace space-y-5\">");

        if (view == "overview")
            builder.Append(Overview(snapshot, from, days));
        else
            builder.Append(DetailHeader(view, days)).Append(view switch
            {
                "servers" => Servers(snapshot, from),
                "maps" => Maps(snapshot, from, days, meta),
                "activity" => Activity(snapshot, from),
                "audience" => Audience(snapshot, from),
                "chat" => Chat(snapshot, from, days, meta),
                "guidance" => Guidance(snapshot, from),
                "actions" => Actions(snapshot, from),
                "health" => Health(snapshot),
                _ => Overview(snapshot, from, days)
            });

        return builder.Append("</div>").ToString();
    }

    private string Overview(DashboardSnapshot snapshot, DateTimeOffset from, int days)
    {
        var sessions = HumanSessions(snapshot, from);
        var completed = sessions.Where(item => item.EndedAt is not null).ToList();
        var unique = sessions.Select(item => item.PlayerKey).Distinct().Count();
        var returning = sessions.GroupBy(item => item.PlayerKey).Count(group => group.Count() > 1);
        var avgMinutes = completed.Count == 0 ? 0 : completed.Average(item => item.DurationSeconds) / 60d;
        var live = LatestSamples(snapshot);
        var online = live.Sum(item => item.HumanPlayers);
        var signalMessages = ChatMessages(snapshot.ChatSignals.Where(item => item.CapturedAt >= from)).Count;
        var usableRotations = RotationRows(snapshot, from).Count(item => item.Rounds >= 3);
        var countries = HumanSessions(snapshot, from).Where(item => !string.IsNullOrWhiteSpace(item.CountryCode))
            .Select(item => item.CountryCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var openIncidents = snapshot.Incidents.Count(item => item.ResolvedAt is null);
        var actionCount = _recommendations.Build(snapshot, from).Count;
        var guidanceSignals = snapshot.PlayerGuidanceEvents.Count(item => item.CapturedAt >= from && item.EventType.Equals("Accusation", StringComparison.OrdinalIgnoreCase));

        var builder = new StringBuilder();
        builder.Append("<section class=\"rounded-xl border border-line bg-surface p-5 shadow-sm md:p-6\"><div class=\"flex flex-col gap-4 md:flex-row md:items-center md:justify-between\"><div><div class=\"text-xs font-semibold uppercase tracking-wider text-primary\">Server intelligence</div><h2 class=\"mt-1 text-2xl font-bold text-foreground\">ServerPulse</h2><p class=\"mt-1 max-w-3xl text-sm text-muted\">See where players join, what keeps them playing and what they are telling you.</p></div>")
            .Append($"<a data-enhance-nav=\"false\" href=\"{Url("overview", days)}\" class=\"inline-flex items-center justify-center gap-2 rounded-lg border border-line bg-surface-alt px-3 py-2 text-sm font-medium text-foreground hover:bg-surface-hover\"><i class=\"ph ph-arrow-clockwise\"></i>Refresh</a></div></section>")
            .Append("<section class=\"sp-status-grid\">")
            .Append(StatusCard("Online now", online, "Human players across monitored servers", "ph-users-three", "text-emerald-400", "servers", days))
            .Append(StatusCard("Unique players", unique, PeriodLabel(days), "ph-identification-card", "text-primary", "servers", days))
            .Append(StatusCard("Average session", $"{avgMinutes:N1}m", $"{completed.Count:N0} completed session(s)", "ph-clock", "text-sky-400", "activity", days))
            .Append(StatusCard("Returning players", unique == 0 ? "0%" : $"{returning * 100d / unique:N0}%", "Played more than once in this period", "ph-arrow-u-up-left", "text-violet-400", "servers", days))
            .Append("</section>")
            .Append("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"border-b border-line px-5 py-4\"><h3 class=\"font-semibold text-foreground\">Explore your network</h3><p class=\"mt-1 text-sm text-muted\">Open the question you want ServerPulse to answer.</p></div><div class=\"sp-explore-grid\">")
            .Append(ExploreCard("Traffic & retention", "Which servers bring players back?", $"{sessions.Count:N0} sessions", "ph-hard-drives", "servers", days))
            .Append(ExploreCard("Rotation performance", "Which maps gain or lose players?", $"{usableRotations:N0} reliable pairings", "ph-map-trifold", "maps", days))
            .Append(ExploreCard("Busy times & exits", "When should events run—and why do sessions end?", $"{NetworkBuckets(snapshot.PopulationSamples.Where(item => item.CapturedAt >= from)).Count:N0} samples", "ph-calendar-dots", "activity", days))
            .Append(ExploreCard("Player audience", "Where do players join from and at what time?", $"{countries:N0} represented countries", "ph-globe-hemisphere-west", "audience", days))
            .Append(ExploreCard("Community voice", "Read the complaints, requests and praise behind the counts.", $"{signalMessages:N0} matched messages", "ph-chats-circle", "chat", days))
            .Append(ExploreCard("Player guidance", "Do chat accusations become proper reports?", _config.PlayerGuidance.Enabled ? $"{guidanceSignals:N0} accusation signals" : "Optional module disabled", "ph-shield-warning", "guidance", days))
            .Append(ExploreCard("Action plan", "Prioritised opportunities backed by sample sizes.", $"{actionCount:N0} current actions", "ph-lightbulb", "actions", days))
            .Append("</div></section>")
            .Append(ActionsPreview(snapshot, from, days))
            .Append($"<section class=\"flex flex-col gap-3 rounded-xl border border-line bg-surface px-5 py-4 shadow-sm sm:flex-row sm:items-center sm:justify-between\"><div class=\"flex items-center gap-3\"><div class=\"flex h-10 w-10 items-center justify-center rounded-lg border border-line bg-surface-alt\"><i class=\"ph ph-heartbeat text-xl {(openIncidents == 0 && snapshot.LastError is null ? "text-emerald-400" : "text-amber-400")}\"></i></div><div><h3 class=\"font-semibold text-foreground\">Data health</h3><p class=\"text-sm text-muted\">{(openIncidents == 0 && snapshot.LastError is null ? "Collection and storage are healthy." : $"{openIncidents:N0} open incident(s) or storage issue(s) need attention.")}</p></div></div><a data-enhance-nav=\"false\" href=\"{Url("health", days)}\" class=\"text-sm font-medium text-primary hover:underline\">Review collection <i class=\"ph ph-arrow-right\"></i></a></section>");
        return builder.ToString();
    }

    private string Servers(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var sessions = HumanSessions(snapshot, from);
        var latest = LatestSamples(snapshot).ToDictionary(item => item.ServerId, StringComparer.OrdinalIgnoreCase);
        var serverKeys = sessions.Select(item => (item.ServerId, item.Game))
            .Concat(latest.Values.Select(item => (item.ServerId, item.Game)))
            .Distinct()
            .ToList();
        var rows = serverKeys.Select(key =>
            {
                var group = sessions.Where(item => item.ServerId.Equals(key.ServerId, StringComparison.OrdinalIgnoreCase)).ToList();
                var completed = group.Where(item => item.EndedAt is not null).ToList();
                var unique = group.Select(item => item.PlayerKey).Distinct().Count();
                var repeat = group.GroupBy(item => item.PlayerKey).Count(item => item.Count() > 1);
                var average = completed.Count == 0 ? 0 : completed.Average(item => item.DurationSeconds) / 60d;
                var shortRate = completed.Count == 0 ? 0 : completed.Count(item => item.DurationSeconds <= _config.BounceThresholdSeconds) * 100d / completed.Count;
                latest.TryGetValue(key.ServerId, out var live);
                var name = Clean(group.OrderByDescending(item => item.StartedAt).FirstOrDefault()?.ServerName ?? live?.ServerName);
                var status = completed.Count < 5 ? "Collecting data" : shortRate >= 40 ? "Needs attention" : repeat * 100d / Math.Max(1, unique) >= 25 ? "Strong retention" : "Stable";
                return new { Name = name, key.Game, Online = live?.HumanPlayers ?? 0, Sessions = group.Count, Unique = unique, Average = average, Returning = repeat * 100d / Math.Max(1, unique), Short = shortRate, Status = status };
            }).OrderByDescending(item => item.Online).ThenByDescending(item => item.Unique).ToList();
        var completedAll = sessions.Count(item => item.EndedAt is not null);
        var networkShort = completedAll == 0 ? 0 : sessions.Count(item => item.EndedAt is not null && item.DurationSeconds <= _config.BounceThresholdSeconds) * 100d / completedAll;

        var builder = new StringBuilder("<section class=\"sp-summary-grid\">")
            .Append(SummaryCard("Monitored servers", rows.Count, "ph-hard-drives", "text-primary"))
            .Append(SummaryCard("Sessions", sessions.Count, "ph-sign-in", "text-sky-400"))
            .Append(SummaryCard("Unique players", sessions.Select(item => item.PlayerKey).Distinct().Count(), "ph-users", "text-emerald-400"))
            .Append(SummaryCard("Short-session rate", $"{networkShort:N0}%", "ph-sign-out", networkShort >= 35 ? "text-amber-400" : "text-violet-400"))
            .Append("</section>");
        return builder.Append(TableSection("Server comparison", "Human-only demand and retention. 'Collecting data' means fewer than five completed sessions.",
            ["Server", "Game", "Online", "Sessions", "Unique", "Avg session", "Returning", "Short", "Assessment"],
            rows.Select(item => new[]
            {
                $"<strong>{E(item.Name)}</strong>", E(item.Game), item.Online.ToString("N0"), item.Sessions.ToString("N0"), item.Unique.ToString("N0"),
                $"{item.Average:N1}m", $"{item.Returning:N0}%", $"{item.Short:N0}%", Badge(item.Status, item.Status == "Needs attention" ? "amber" : item.Status == "Strong retention" ? "green" : "blue")
            }))).ToString();
    }

    private string Maps(DashboardSnapshot snapshot, DateTimeOffset from, int days, IDictionary<string, string> meta)
    {
        var scope = (ReadValue(meta, "scope") ?? "reliable").ToLowerInvariant();
        if (scope is not ("reliable" or "gainers" or "losers" or "all")) scope = "reliable";
        var page = ReadInteger(meta, "page", 1, 1, 10_000);
        var all = RotationRows(snapshot, from);
        var reliable = all.Where(item => item.Rounds >= 3).ToList();
        var filtered = scope switch
        {
            "gainers" => reliable.Where(item => item.Change > 0).OrderByDescending(item => item.Change).ThenByDescending(item => item.Rounds).ToList(),
            "losers" => reliable.Where(item => item.Change < 0).OrderBy(item => item.Change).ThenByDescending(item => item.Rounds).ToList(),
            "all" => all.OrderByDescending(item => item.Rounds).ThenByDescending(item => item.Change).ToList(),
            _ => reliable.OrderByDescending(item => item.Rounds).ThenByDescending(item => item.Change).ToList()
        };
        var pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)RotationPageSize));
        page = Math.Min(page, pages);
        var visible = filtered.Skip((page - 1) * RotationPageSize).Take(RotationPageSize).ToList();
        var best = reliable.OrderByDescending(item => item.Change).FirstOrDefault();
        var worst = reliable.OrderBy(item => item.Change).FirstOrDefault();

        var builder = new StringBuilder("<section class=\"sp-summary-grid\">")
            .Append(SummaryCard("Reliable pairings", reliable.Count, "ph-check-circle", "text-emerald-400"))
            .Append(SummaryCard("Still collecting", all.Count - reliable.Count, "ph-hourglass", "text-muted"))
            .Append(SummaryCard("Best population change", best is null ? "—" : $"{best.Change:+0.0;-0.0;0.0}", "ph-trend-up", "text-emerald-400"))
            .Append(SummaryCard("Largest drop", worst is null ? "—" : $"{worst.Change:+0.0;-0.0;0.0}", "ph-trend-down", "text-red-400"))
            .Append("</section>")
            .Append(FilterLinks("Rotation view", days, "scope", scope,
                [("reliable", "Reliable samples", reliable.Count), ("gainers", "Gaining players", reliable.Count(item => item.Change > 0)), ("losers", "Losing players", reliable.Count(item => item.Change < 0)), ("all", "All samples", all.Count)]));
        builder.Append(TableSection("Map and mode performance", "Population change is end minus start. The default view requires at least three completed rounds so one empty round cannot rank first.",
            ["Rotation", "Game", "Rounds", "Avg population", "Change", "Joins", "Leaves", "Sample"],
            visible.Select(item => new[]
            {
                $"<strong>{E(item.Map)}</strong><div class=\"mt-0.5 text-xs text-muted\">{E(item.Mode)}</div>", E(item.Game), item.Rounds.ToString("N0"),
                $"{item.AverageStart:N1} → {item.AverageEnd:N1}", $"<span class=\"font-semibold {(item.Change > 0 ? "text-emerald-400" : item.Change < 0 ? "text-red-400" : "text-muted")}\">{item.Change:+0.0;-0.0;0.0}</span>",
                item.Joins.ToString("N0"), item.Leaves.ToString("N0"), Badge(item.Rounds >= 10 ? "Strong" : item.Rounds >= 3 ? "Usable" : "Low", item.Rounds >= 10 ? "green" : item.Rounds >= 3 ? "blue" : "amber")
            })));
        return builder.Append(Pager("maps", days, page, pages, $"scope={WebUtility.UrlEncode(scope)}")).ToString();
    }

    private string Activity(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var buckets = NetworkBuckets(snapshot.PopulationSamples.Where(item => item.CapturedAt >= from));
        var cells = buckets.GroupBy(item => new { Day = (int)AnalyticsTime.Local(item.At).DayOfWeek, AnalyticsTime.Local(item.At).Hour })
            .ToDictionary(group => (group.Key.Day, group.Key.Hour), group => group.Average(item => item.Humans));
        var maximum = Math.Max(1, cells.Values.DefaultIfEmpty(0).Max());
        var busiest = cells.OrderByDescending(item => item.Value).FirstOrDefault();
        var sessions = HumanSessions(snapshot, from);
        var completed = sessions.Where(item => item.EndedAt is not null).ToList();
        var average = completed.Count == 0 ? 0 : completed.Average(item => item.DurationSeconds) / 60d;
        var shortRate = completed.Count == 0 ? 0 : completed.Count(item => item.DurationSeconds <= _config.BounceThresholdSeconds) * 100d / completed.Count;

        var builder = new StringBuilder("<section class=\"sp-summary-grid\">")
            .Append(SummaryCard("Peak network population", buckets.Count == 0 ? 0 : buckets.Max(item => item.Humans), "ph-trend-up", "text-emerald-400"))
            .Append(SummaryCard("Busiest time", cells.Count == 0 ? "—" : $"{((DayOfWeek)busiest.Key.Item1).ToString()[..3]} {busiest.Key.Item2:00}:00", "ph-calendar-check", "text-primary"))
            .Append(SummaryCard("Average session", $"{average:N1}m", "ph-clock", "text-sky-400"))
            .Append(SummaryCard("Short sessions", $"{shortRate:N0}%", "ph-sign-out", shortRate >= 35 ? "text-amber-400" : "text-violet-400"))
            .Append("</section>")
            .Append("<section class=\"overflow-x-auto rounded-xl border border-line bg-surface p-5 shadow-sm\"><div class=\"flex flex-col gap-1 sm:flex-row sm:items-end sm:justify-between\"><div><h3 class=\"font-semibold text-foreground\">When players are online</h3><p class=\"mt-1 text-sm text-muted\">Average network population by ")
            .Append(E(AnalyticsTime.ConfigurationLabel))
            .Append(". Dark cells are quiet; bright cells are busy.</p></div><div class=\"text-xs text-muted\">Hover a cell for its value</div></div><div class=\"mt-5 min-w-[980px] space-y-1\"><div class=\"sp-heat text-[10px] text-muted\"><span></span>");
        for (var hour = 0; hour < 24; hour++) builder.Append($"<span class=\"text-center\">{hour:00}</span>");
        builder.Append("</div>");
        foreach (var day in new[] { 1, 2, 3, 4, 5, 6, 0 })
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

        var disconnects = completed.GroupBy(item => item.DisconnectReason)
            .Select(group => new
            {
                Reason = group.Key,
                Sessions = group.Count(),
                Average = group.Average(item => item.DurationSeconds) / 60d,
                Share = group.Count() * 100d / Math.Max(1, completed.Count)
            }).OrderByDescending(item => item.Sessions);
        return builder.Append(TableSection("Why sessions ended", "IW4MAdmin supplies explicit kick and ban reasons. Ordinary departures are grouped as quit or lost connection.",
            ["Outcome", "Sessions", "Share", "Avg session"],
            disconnects.Select(item => new[] { E(item.Reason), item.Sessions.ToString("N0"), $"{item.Share:N0}%", $"{item.Average:N1}m" }))).ToString();
    }

    private string Audience(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var rows = HumanSessions(snapshot, from).Where(item => !string.IsNullOrWhiteSpace(item.CountryCode))
            .GroupBy(item => new { item.CountryCode, item.CountryName })
            .Select(group =>
            {
                var complete = group.Where(item => item.EndedAt is not null).ToList();
                return new AudienceRow(
                    group.Key.CountryCode,
                    group.Key.CountryName,
                    group.Select(item => item.PlayerKey).Distinct().Count(),
                    group.Count(),
                    complete.Count == 0 ? 0 : complete.Average(item => item.DurationSeconds) / 60d,
                    group.GroupBy(item => AnalyticsTime.Local(item.StartedAt).Hour).OrderByDescending(item => item.Count()).First().Key);
            })
            .Where(item => item.Players >= _config.MinimumCountrySampleSize)
            .OrderByDescending(item => item.Players).ToList();
        var leader = rows.FirstOrDefault();
        var totalPlayers = rows.Sum(item => item.Players);

        var builder = new StringBuilder("<section class=\"sp-summary-grid\">")
            .Append(SummaryCard("Visible countries", rows.Count, "ph-globe", "text-primary"))
            .Append(SummaryCard("Players in visible samples", totalPlayers, "ph-users", "text-emerald-400"))
            .Append(SummaryCard("Leading audience", leader is null ? "Collecting data" : $"{CountryFlag(leader.CountryCode)} {leader.CountryName}", "ph-flag", "text-sky-400"))
            .Append(SummaryCard("Leading join time", leader is null ? "—" : $"{leader.PeakHour:00}:00", "ph-clock", "text-violet-400"))
            .Append("</section>");
        return builder.Append(TableSection("Player audience", $"Countries below {_config.MinimumCountrySampleSize:N0} unique players are hidden for privacy. Popular times use {AnalyticsTime.ConfigurationLabel}, not the player's local timezone.",
            ["Country", "Code", "Unique players", "Sessions", "Avg session", $"Popular time ({AnalyticsTime.ConfigurationLabel})"],
            rows.Select(item => new[] { $"<span class=\"mr-2 text-lg\" aria-hidden=\"true\">{CountryFlag(item.CountryCode)}</span><strong>{E(item.CountryName)}</strong>", E(item.CountryCode), item.Players.ToString("N0"), item.Sessions.ToString("N0"), $"{item.Average:N1}m", $"{item.PeakHour:00}:00" }))).ToString();
    }

    private string Chat(DashboardSnapshot snapshot, DateTimeOffset from, int days, IDictionary<string, string> meta)
    {
        var allSignals = snapshot.ChatSignals.Where(item => item.CapturedAt >= from).ToList();
        var allMessages = ChatMessages(allSignals);
        var selectedCategory = ReadValue(meta, "category") ?? string.Empty;
        var selectedServer = ReadValue(meta, "server") ?? string.Empty;
        var page = ReadInteger(meta, "page", 1, 1, 10_000);
        var categories = allSignals.GroupBy(item => item.Category)
            .Select(group => new { Name = group.Key, Count = UniqueSignalCount(group) })
            .OrderByDescending(item => item.Count).ToList();
        var servers = allMessages.Select(item => new { item.ServerId, item.ServerName })
            .DistinctBy(item => item.ServerId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.ServerName).ToList();
        var messages = allMessages.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(selectedCategory))
            messages = messages.Where(item => item.Categories.Contains(selectedCategory, StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(selectedServer))
            messages = messages.Where(item => item.ServerId.Equals(selectedServer, StringComparison.OrdinalIgnoreCase));
        var filtered = messages.Where(item => !string.IsNullOrWhiteSpace(item.Excerpt)).OrderByDescending(item => item.CapturedAt).ToList();
        var pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)ChatPageSize));
        page = Math.Min(page, pages);
        var visible = filtered.Skip((page - 1) * ChatPageSize).Take(ChatPageSize).ToList();
        var positive = allMessages.Count(item => item.Categories.Contains("Positive", StringComparer.OrdinalIgnoreCase));
        var complaints = allMessages.Count(item => item.Categories.Any(category => !category.Equals("Positive", StringComparison.OrdinalIgnoreCase)));
        var humanHours = HumanSessions(snapshot, from).Where(item => item.EndedAt is not null).Sum(item => item.DurationSeconds) / 3600d;
        var complaintRate = humanHours <= 0 ? 0 : complaints * 100d / humanHours;

        var builder = new StringBuilder("<section class=\"sp-summary-grid\">")
            .Append(SummaryCard("Matched messages", allMessages.Count, "ph-chats-circle", "text-primary"))
            .Append(SummaryCard("Complaint / request signals", complaints, "ph-warning-circle", "text-amber-400"))
            .Append(SummaryCard("Positive feedback", positive, "ph-heart", "text-emerald-400"))
            .Append(SummaryCard("Signals per 100 player-hours", $"{complaintRate:N1}", "ph-gauge", "text-sky-400"))
            .Append("</section>");

        if (!_config.StoreRawChat && !_config.StoreRedactedChatExcerpts)
        {
            builder.Append("<section class=\"rounded-xl border border-amber-500/30 bg-amber-500/10 p-5\"><div class=\"flex gap-3\"><i class=\"ph ph-warning text-2xl text-amber-400\"></i><div><h3 class=\"font-semibold text-foreground\">Message excerpts are disabled</h3><p class=\"mt-1 text-sm text-muted\">Category counts still work, but ServerPulse cannot show what players said. Set <code class=\"rounded bg-black/20 px-1.5 py-0.5 text-foreground\">\"StoreRedactedChatExcerpts\": true</code> in Configuration/ServerPulse.json and restart IW4MAdmin. Only new matched messages will have excerpts.</p></div></div></section>");
        }
        else if (_config.StoreRawChat)
        {
            builder.Append("<section class=\"rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-foreground\"><strong>Privacy warning:</strong> raw chat storage is enabled. Redacted excerpts are recommended.</section>");
        }

        builder.Append("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"border-b border-line px-5 py-4\"><h3 class=\"font-semibold text-foreground\">Filter community voice</h3><p class=\"mt-1 text-sm text-muted\">Choose a topic or server. Player identities remain anonymised.</p></div><form method=\"get\" action=\"")
            .Append($"/Interaction/Render/{InteractionKey}")
            .Append("\" class=\"grid gap-3 p-5 md:grid-cols-[1fr_1fr_auto]\"><input type=\"hidden\" name=\"view\" value=\"chat\"><input type=\"hidden\" name=\"period\" value=\"").Append(days).Append("\"><label class=\"text-xs font-medium uppercase tracking-wide text-muted\">Topic<select name=\"category\" class=\"mt-1 w-full rounded-lg border border-line bg-surface-alt px-3 py-2 text-sm normal-case text-foreground\"><option value=\"\">All topics</option>");
        foreach (var category in categories)
            builder.Append($"<option value=\"{E(category.Name)}\"{(category.Name.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty)}>{E(category.Name)} ({category.Count:N0})</option>");
        builder.Append("</select></label><label class=\"text-xs font-medium uppercase tracking-wide text-muted\">Server<select name=\"server\" class=\"mt-1 w-full rounded-lg border border-line bg-surface-alt px-3 py-2 text-sm normal-case text-foreground\"><option value=\"\">All servers</option>");
        foreach (var server in servers)
            builder.Append($"<option value=\"{E(server.ServerId)}\"{(server.ServerId.Equals(selectedServer, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty)}>{E(server.ServerName)}</option>");
        builder.Append("</select></label><div class=\"flex items-end gap-2\"><button class=\"flex-1 rounded-lg bg-action-primary px-4 py-2 text-sm font-medium text-white hover:bg-action-primary-hover\" type=\"submit\"><i class=\"ph ph-funnel\"></i> Apply</button>")
            .Append($"<a data-enhance-nav=\"false\" href=\"{Url("chat", days)}\" class=\"rounded-lg border border-line bg-surface-alt px-3 py-2 text-sm text-muted hover:bg-surface-hover\">Clear</a></div></form></section>")
            .Append("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"border-b border-line px-5 py-4\"><h3 class=\"font-semibold text-foreground\">What players actually said</h3><p class=\"mt-1 text-sm text-muted\">Matched, redacted excerpts with the server and rotation context captured at the time.</p></div><div class=\"divide-y divide-line\">");
        if (visible.Count == 0)
            builder.Append("<div class=\"px-5 py-12 text-center text-sm text-muted\">No retained excerpts match these filters. Counts above may include older count-only signals.</div>");
        foreach (var message in visible)
        {
            builder.Append("<article class=\"px-5 py-4 hover:bg-surface-hover/20\"><div class=\"flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between\"><div class=\"flex flex-wrap gap-1.5\">");
            foreach (var category in message.Categories)
                builder.Append(Badge(category, category.Equals("Positive", StringComparison.OrdinalIgnoreCase) ? "green" : category.Equals("Cheating", StringComparison.OrdinalIgnoreCase) ? "red" : "amber"));
            builder.Append($"</div><time class=\"shrink-0 text-xs text-muted\">{E(AnalyticsTime.Display(message.CapturedAt))}</time></div><blockquote class=\"sp-quote mt-3 bg-surface-alt/20 px-4 py-3 text-sm leading-relaxed text-foreground\">“{E(message.Excerpt)}”</blockquote><div class=\"mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted\"><span><i class=\"ph ph-hard-drives\"></i> {E(message.ServerName)}</span><span><i class=\"ph ph-map-trifold\"></i> {E(message.Map)} · {E(message.Mode)}</span><span><i class=\"ph ph-user-circle\"></i> Player {E(ShortPlayer(message.PlayerKey))}</span>");
            if (ValidCountryCode(message.CountryCode))
                builder.Append($"<span title=\"{E(message.CountryName)}\"><span aria-hidden=\"true\">{CountryFlag(message.CountryCode)}</span> {E(message.CountryName)}</span>");
            builder.Append("</div></article>");
        }
        builder.Append("</div></section>");
        var extra = $"category={WebUtility.UrlEncode(selectedCategory)}&server={WebUtility.UrlEncode(selectedServer)}";
        return builder.Append(Pager("chat", days, page, pages, extra)).ToString();
    }

    private string Actions(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var values = _recommendations.Build(snapshot, from);
        var builder = new StringBuilder("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"border-b border-line px-5 py-4\"><h3 class=\"font-semibold text-foreground\">Prioritised action plan</h3><p class=\"mt-1 text-sm text-muted\">Every suggestion shows its evidence and sample size. Treat low-confidence items as experiments, not facts.</p></div><div class=\"grid gap-px bg-line md:grid-cols-2\">");
        if (values.Count == 0)
            builder.Append("<div class=\"bg-surface p-10 text-center text-sm text-muted md:col-span-2\">There is not enough representative traffic to make a useful recommendation yet.</div>");
        foreach (var item in values)
        {
            var color = item.Severity == "High" ? "red" : item.Severity == "Medium" ? "amber" : "green";
            var destination = ActionDestination(item);
            builder.Append($"<article class=\"bg-surface p-5\"><div class=\"flex items-center justify-between gap-3\">{Badge(item.Severity, color)}<span class=\"text-xs text-muted\">{item.Confidence:N0}% confidence · sample {item.SampleSize:N0}</span></div><h4 class=\"mt-3 text-base font-semibold text-foreground\">{E(item.Title)}</h4><p class=\"mt-2 text-sm leading-relaxed text-muted\">{E(item.Detail)}</p><div class=\"mt-4 rounded-lg border border-line bg-surface-alt/25 p-3 text-sm text-foreground\"><strong>Next step:</strong> {E(item.Action)}</div><a data-enhance-nav=\"false\" href=\"{Url(destination, DaysFrom(from))}\" class=\"mt-4 inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline\">Inspect the evidence <i class=\"ph ph-arrow-right\"></i></a></article>");
        }
        return builder.Append("</div></section>").ToString();
    }

    private string Guidance(DashboardSnapshot snapshot, DateTimeOffset from)
    {
        var events = snapshot.PlayerGuidanceEvents.Where(item => item.CapturedAt >= from).OrderByDescending(item => item.CapturedAt).ToList();
        var accusations = events.Where(item => item.EventType.Equals("Accusation", StringComparison.OrdinalIgnoreCase)).ToList();
        var reports = events.Where(item => item.EventType.Equals("Report", StringComparison.OrdinalIgnoreCase)).ToList();
        var reminders = accusations.Count(item => item.Outcome.Contains("reminder sent", StringComparison.OrdinalIgnoreCase));
        var alerts = accusations.Count(item => item.StaffAlertSent);
        var converted = reports.Count(report => accusations.Any(accusation =>
            accusation.ReporterKey.Equals(report.ReporterKey, StringComparison.OrdinalIgnoreCase) &&
            accusation.CapturedAt <= report.CapturedAt &&
            report.CapturedAt - accusation.CapturedAt <= TimeSpan.FromMinutes(15) &&
            (string.IsNullOrWhiteSpace(report.TargetKey) || string.IsNullOrWhiteSpace(accusation.TargetKey) ||
             report.TargetKey.Equals(accusation.TargetKey, StringComparison.OrdinalIgnoreCase))));
        var conversionRate = accusations.Count == 0 ? 0 : converted * 100d / accusations.Count;
        var targetRows = accusations
            .Where(item => !string.IsNullOrWhiteSpace(item.TargetKey))
            .GroupBy(item => item.TargetKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.Select(item => item.TargetName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Unknown",
                Key = group.Key,
                ClientId = group.OrderByDescending(item => item.CapturedAt).Select(item => item.TargetClientId).FirstOrDefault(value => value.HasValue),
                Signals = group.Count(),
                Accusers = group.Select(item => item.ReporterKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Categories = string.Join(", ", group.Select(item => item.Category).Distinct(StringComparer.OrdinalIgnoreCase)),
                Latest = group.Max(item => item.CapturedAt)
            })
            .OrderByDescending(item => item.Accusers).ThenByDescending(item => item.Signals).Take(25).ToList();

        var builder = new StringBuilder();
        if (!_config.PlayerGuidance.Enabled)
        {
            builder.Append("<section class=\"rounded-xl border border-primary/30 bg-primary/10 p-5\"><div class=\"flex gap-3\"><i class=\"ph ph-info text-2xl text-primary\"></i><div><h3 class=\"font-semibold text-foreground\">Player guidance is available but disabled</h3><p class=\"mt-1 text-sm leading-relaxed text-muted\">Enable <code class=\"rounded bg-black/20 px-1.5 py-0.5 text-foreground\">PlayerGuidance.Enabled</code> when you want ServerPulse to turn chat accusations into private report reminders. Do not load ChatCheatMonitor.dll at the same time or players may receive duplicate replies.</p></div></div></section>");
        }

        builder.Append("<section class=\"sp-summary-grid\">")
            .Append(SummaryCard("Accusation signals", accusations.Count, "ph-warning-circle", "text-amber-400"))
            .Append(SummaryCard("Official reports observed", reports.Count, "ph-flag", "text-primary"))
            .Append(SummaryCard("Report follow-through", $"{conversionRate:N0}%", "ph-arrow-bend-down-right", "text-emerald-400"))
            .Append(SummaryCard("Staff escalations", alerts, "ph-shield-warning", alerts > 0 ? "text-red-400" : "text-sky-400"))
            .Append("</section>")
            .Append($"<section class=\"rounded-xl border border-line bg-surface px-5 py-4 shadow-sm\"><div class=\"flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between\"><div><h3 class=\"font-semibold text-foreground\">From accusation to useful action</h3><p class=\"mt-1 text-sm text-muted\">{reminders:N0} reminder(s) were sent. {converted:N0} official report command(s) followed an accusation by the same player within 15 minutes.</p></div>{Badge(_config.PlayerGuidance.Enabled ? $"{_config.PlayerGuidance.ResponseMode} mode" : "Disabled", _config.PlayerGuidance.Enabled ? "green" : "amber")}</div><p class=\"mt-3 text-xs text-muted\">Chat accusations are community signals, not proof of cheating. Unique-accuser counts reduce spam amplification but administrators should still review reports and evidence.</p></section>");

        if (_playerGuidance.ConfigurationIssues.Count > 0)
            builder.Append($"<section class=\"rounded-xl border border-amber-500/30 bg-amber-500/10 p-5\"><h3 class=\"font-semibold text-foreground\">Guidance configuration checks</h3><ul class=\"mt-2 list-disc space-y-1 pl-5 text-sm text-muted\"><li>{string.Join("</li><li>", _playerGuidance.ConfigurationIssues.Select(item => E(item.Message)))}</li></ul></section>");

        builder.Append(TableSection("Repeatedly mentioned players", "Grouped by resolved online target. Distinct accusers matter more than raw chat volume.",
            ["Player", "Unique accusers", "Signals", "Categories", "Latest"],
            targetRows.Select(item => new[]
            {
                $"<strong>{E(item.Name)}</strong><div class=\"text-xs text-muted\">ID {E(ShortPlayer(item.Key))}{(item.ClientId.HasValue ? $" · <a href=\"/Client/Profile/{item.ClientId.Value}\" class=\"text-primary hover:underline\">Open profile</a>" : string.Empty)}</div>",
                item.Accusers.ToString("N0"), item.Signals.ToString("N0"), E(item.Categories), E(AnalyticsTime.Display(item.Latest))
            })));

        builder.Append("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"border-b border-line px-5 py-4\"><h3 class=\"font-semibold text-foreground\">Recent guidance and report events</h3><p class=\"mt-1 text-sm text-muted\">Privacy-safe context showing what triggered guidance and whether the player followed through.</p></div><div class=\"divide-y divide-line\">");
        if (events.Count == 0)
            builder.Append("<div class=\"px-5 py-12 text-center text-sm text-muted\">No player-guidance events have been retained for this period.</div>");
        foreach (var item in events.Take(50))
        {
            var isReport = item.EventType.Equals("Report", StringComparison.OrdinalIgnoreCase);
            var targetLabel = string.IsNullOrWhiteSpace(item.TargetName) ? "Target unresolved" : E(item.TargetName);
            var targetDisplay = item.TargetClientId.HasValue
                ? $"<a href=\"/Client/Profile/{item.TargetClientId.Value}\" class=\"text-sm font-semibold text-primary hover:underline\">{targetLabel}</a>"
                : $"<strong class=\"text-sm text-foreground\">{targetLabel}</strong>";
            var reviewBadge = isReport ? string.Empty : ReviewBadge(item.ReviewStatus);
            builder.Append($"<article class=\"px-5 py-4 hover:bg-surface-hover/20\"><div class=\"flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between\"><div class=\"flex flex-wrap items-center gap-2\">{Badge(isReport ? "Official report" : item.Category, isReport ? "green" : "amber")}{reviewBadge}{(item.StaffAlertSent ? Badge("Staff alerted", "red") : string.Empty)}{targetDisplay}</div><time class=\"text-xs text-muted\">{E(AnalyticsTime.Display(item.CapturedAt))}</time></div>");
            if (!string.IsNullOrWhiteSpace(item.Excerpt))
                builder.Append($"<blockquote class=\"sp-quote mt-3 bg-surface-alt/20 px-4 py-3 text-sm text-foreground\">“{E(item.Excerpt)}”</blockquote>");
            builder.Append($"<div class=\"mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted\"><span><i class=\"ph ph-hard-drives\"></i> {E(item.ServerName)}</span><span><i class=\"ph ph-map-trifold\"></i> {E(item.Map)} · {E(item.Mode)}</span><span><i class=\"ph ph-crosshair\"></i> rule: {E(item.Pattern)}</span><span><i class=\"ph ph-info\"></i> {E(item.Outcome)}</span>");
            if (ValidCountryCode(item.CountryCode)) builder.Append($"<span title=\"{E(item.CountryName)}\">{CountryFlag(item.CountryCode)} {E(item.CountryName)}</span>");
            builder.Append("</div>");
            if (!isReport && (item.ReviewStatus is "Unresolved" or "CaseFailed"))
                builder.Append(GuidanceReviewControls(item));
            else if (!string.IsNullOrWhiteSpace(item.DemosToDiscordCaseId))
                builder.Append($"<div class=\"mt-3 text-xs text-muted\">DemosToDiscord case <strong class=\"text-foreground\">{E(item.DemosToDiscordCaseId)}</strong> created by {E(item.ResolvedByName)}.</div>");
            builder.Append("</article>");
        }
        return builder.Append("</div></section>").ToString();
    }

    private string GuidanceReviewControls(PlayerGuidanceEventRecord item)
    {
        var token = AntiForgeryField();
        var builder = new StringBuilder("<details class=\"mt-4 rounded-lg border border-line bg-surface-alt/20\"><summary class=\"cursor-pointer px-4 py-3 text-sm font-semibold text-foreground\">Review unresolved signal and match context</summary><div class=\"space-y-4 border-t border-line p-4\">");
        if (!string.IsNullOrWhiteSpace(item.DemosToDiscordError))
            builder.Append($"<div class=\"rounded-lg border border-red-500/30 bg-red-500/10 p-3 text-sm text-red-300\">Case handoff failed: {E(item.DemosToDiscordError)}</div>");

        builder.Append("<div><h4 class=\"text-xs font-bold uppercase tracking-wide text-muted\">Chat around the signal</h4><div class=\"mt-2 divide-y divide-line overflow-hidden rounded-lg border border-line\">");
        if (item.ContextMessages.Count == 0)
            builder.Append("<div class=\"p-3 text-sm text-muted\">No surrounding chat was retained for this event.</div>");
        foreach (var message in item.ContextMessages.OrderBy(value => value.CapturedAt))
            builder.Append($"<div class=\"flex gap-3 px-3 py-2 text-sm\"><time class=\"shrink-0 text-xs text-muted\">{E(message.CapturedAt.ToString("HH:mm:ss"))}</time><span class=\"font-semibold text-foreground\">{E(message.PlayerName)}</span><span class=\"min-w-0 break-words text-muted\">{E(message.Message)}</span></div>");
        builder.Append("</div></div>");

        if (item.ReviewStatus == "CaseFailed" && item.TargetClientId.HasValue)
        {
            builder.Append($"<form method=\"post\" action=\"/api/serverpulse/guidance/{E(item.Id)}\" class=\"space-y-3\">{token}<input type=\"hidden\" name=\"operation\" value=\"RetryCase\"><label class=\"block text-xs font-bold uppercase tracking-wide text-muted\">Admin note<textarea name=\"notes\" maxlength=\"500\" class=\"mt-1 w-full rounded-lg border border-line bg-surface px-3 py-2 text-sm text-foreground\">{E(item.ReviewNotes)}</textarea></label><button class=\"rounded-lg bg-action-primary px-4 py-2 text-sm font-semibold text-white\" type=\"submit\">Retry DemosToDiscord case</button></form>");
            return builder.Append("</div></details>").ToString();
        }

        var candidates = item.PlayersAtCapture.Where(value => !value.IsBot &&
            !value.PlayerKey.Equals(item.ReporterKey, StringComparison.OrdinalIgnoreCase)).OrderBy(value => value.PlayerName).ToList();
        builder.Append($"<form method=\"post\" action=\"/api/serverpulse/guidance/{E(item.Id)}\" class=\"space-y-3\">{token}<label class=\"block text-xs font-bold uppercase tracking-wide text-muted\">Accused player<select name=\"targetClientId\" required class=\"mt-1 w-full rounded-lg border border-line bg-surface px-3 py-2 text-sm text-foreground\"><option value=\"\">Select a player who was in the game…</option>");
        foreach (var player in candidates)
            builder.Append($"<option value=\"{player.ClientId}\">{E(player.PlayerName)} · ID {player.ClientId}</option>");
        builder.Append("</select></label><label class=\"block text-xs font-bold uppercase tracking-wide text-muted\">Admin note<textarea name=\"notes\" maxlength=\"500\" placeholder=\"Why this player was selected, or why the signal was dismissed\" class=\"mt-1 w-full rounded-lg border border-line bg-surface px-3 py-2 text-sm text-foreground\"></textarea></label><div class=\"flex flex-wrap gap-2\"><button name=\"operation\" value=\"Resolve\" class=\"rounded-lg border border-line bg-surface px-4 py-2 text-sm font-semibold text-foreground\" type=\"submit\">Resolve only</button>");
        if (_config.PlayerGuidance.EnableDemosToDiscordEscalation)
            builder.Append("<button name=\"operation\" value=\"ResolveAndCreateCase\" class=\"rounded-lg bg-action-primary px-4 py-2 text-sm font-semibold text-white\" type=\"submit\">Resolve &amp; create review case</button>");
        builder.Append("<button name=\"operation\" value=\"Dismiss\" class=\"rounded-lg border border-red-500/30 px-4 py-2 text-sm font-semibold text-red-300\" type=\"submit\" formnovalidate>Dismiss signal</button></div></form>");
        return builder.Append("</div></details>").ToString();
    }

    private string AntiForgeryField()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return string.Empty;
        var token = _antiforgery.GetAndStoreTokens(context).RequestToken;
        return string.IsNullOrWhiteSpace(token)
            ? string.Empty
            : $"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{E(token)}\">";
    }

    private static string ReviewBadge(string status) => status switch
    {
        "AutomaticallyResolved" => Badge("Auto resolved", "green"),
        "ManuallyResolved" => Badge("Manually resolved", "green"),
        "CaseCreated" => Badge("Review case created", "green"),
        "CaseQueued" => Badge("Creating case", "amber"),
        "CaseFailed" => Badge("Case handoff failed", "red"),
        "Dismissed" => Badge("Dismissed", "blue"),
        _ => Badge("Target unresolved", "amber")
    };

    private string Health(DashboardSnapshot snapshot)
    {
        var unresolved = snapshot.Incidents.Where(item => item.ResolvedAt is null).OrderByDescending(item => item.StartedAt).ToList();
        var latest = LatestSamples(snapshot);
        var issues = ConfigurationIssues();
        var builder = new StringBuilder("<section class=\"sp-summary-grid\">")
            .Append(SummaryCard("Configuration", issues.Count == 0 ? "Valid" : $"{issues.Count:N0} issue(s)", issues.Count == 0 ? "ph-check-circle" : "ph-warning", issues.Count == 0 ? "text-emerald-400" : "text-amber-400"))
            .Append(SummaryCard("Storage", snapshot.LastError is null ? "Healthy" : "Write error", snapshot.LastError is null ? "ph-database" : "ph-warning", snapshot.LastError is null ? "text-emerald-400" : "text-red-400"))
            .Append(SummaryCard("Sessions retained", snapshot.Sessions.Count, "ph-users", "text-primary"))
            .Append(SummaryCard("Population samples", snapshot.PopulationSamples.Count, "ph-waveform", "text-sky-400"))
            .Append("</section>");
        if (issues.Count > 0)
            builder.Append($"<section class=\"rounded-xl border border-amber-500/30 bg-amber-500/10 p-5\"><h3 class=\"font-semibold text-foreground\">Configuration checks</h3><ul class=\"mt-2 list-disc space-y-1 pl-5 text-sm text-muted\"><li>{string.Join("</li><li>", issues.Select(E))}</li></ul></section>");
        builder.Append(TableSection("Live server telemetry", "Latest IW4MAdmin status and latency measurements. A dash means the host does not expose that metric.",
            ["Server", "Game", "Rotation", "Humans", "Bots", "RCON", "Event pipeline", "Captured"],
            latest.Select(item => new[]
            {
                $"<strong>{E(Clean(item.ServerName))}</strong>", E(item.Game), $"{E(item.Map)}<div class=\"text-xs text-muted\">{E(item.Mode)}</div>",
                item.HumanPlayers.ToString("N0"), item.BotPlayers.ToString("N0"), item.RconLatencyMilliseconds <= 0 ? "—" : $"{item.RconLatencyMilliseconds:N0} ms",
                item.EventLatencyMilliseconds <= 0 ? "—" : $"{item.EventLatencyMilliseconds:N0} ms", E(AnalyticsTime.Display(item.CapturedAt))
            })));
        return builder.Append(TableSection("Open incidents", "Monitoring and connectivity interruptions that have not recovered.",
            ["Server", "Incident", "Started"],
            unresolved.Select(item => new[] { E(Clean(item.ServerName)), E(item.Type), E(AnalyticsTime.Display(item.StartedAt)) }))).ToString();
    }

    private string DetailHeader(string view, int days)
    {
        var section = Sections[view];
        var builder = new StringBuilder("<section class=\"rounded-xl border border-line bg-surface p-5 shadow-sm\"><div class=\"flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between\"><div class=\"flex items-start gap-3\">")
            .Append($"<a data-enhance-nav=\"false\" href=\"{Url("overview", days)}\" class=\"flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-line bg-surface-alt text-muted hover:bg-surface-hover hover:text-foreground\" title=\"Back to overview\"><i class=\"ph ph-arrow-left\"></i></a><div><div class=\"flex items-center gap-2\"><i class=\"ph {E(section.Icon)} text-xl text-primary\"></i><h2 class=\"text-xl font-bold text-foreground\">{E(section.Title)}</h2></div><p class=\"mt-1 text-sm text-muted\">{E(section.Description)}</p></div></div><div class=\"flex flex-wrap gap-2\">");
        foreach (var value in new[] { 1, 7, 30, 90 })
            builder.Append($"<a data-enhance-nav=\"false\" href=\"{Url(view, value)}\" class=\"rounded-lg border px-3 py-2 text-sm font-medium {(days == value ? "border-primary bg-primary/10 text-primary" : "border-line bg-surface-alt text-muted hover:text-foreground")}\">{(value == 1 ? "24 hours" : $"{value} days")}</a>");
        return builder.Append("</div></div></section>").ToString();
    }

    private string ActionsPreview(DashboardSnapshot snapshot, DateTimeOffset from, int days)
    {
        var values = _recommendations.Build(snapshot, from).Take(3).ToList();
        if (values.Count == 0)
            return string.Empty;
        var builder = new StringBuilder("<section class=\"overflow-hidden rounded-xl border border-line bg-surface shadow-sm\"><div class=\"flex items-center justify-between gap-3 border-b border-line px-5 py-4\"><div><h3 class=\"font-semibold text-foreground\">What deserves attention</h3><p class=\"mt-1 text-sm text-muted\">The strongest evidence-backed actions in this period.</p></div>")
            .Append($"<a data-enhance-nav=\"false\" href=\"{Url("actions", days)}\" class=\"text-sm font-medium text-primary hover:underline\">View action plan</a></div><div class=\"sp-explore-grid\">");
        foreach (var item in values)
        {
            var color = item.Severity == "High" ? "red" : item.Severity == "Medium" ? "amber" : "green";
            builder.Append($"<a data-enhance-nav=\"false\" href=\"{Url(ActionDestination(item), days)}\" class=\"group bg-surface p-5 hover:bg-surface-hover/30\"><div class=\"flex items-center justify-between gap-3\">{Badge(item.Severity, color)}<span class=\"text-xs text-muted\">{item.Confidence:N0}%</span></div><h4 class=\"mt-3 font-semibold text-foreground group-hover:text-primary\">{E(item.Title)}</h4><p class=\"mt-2 line-clamp-2 text-sm text-muted\">{E(item.Detail)}</p></a>");
        }
        return builder.Append("</div></section>").ToString();
    }

    private IReadOnlyList<string> ConfigurationIssues()
    {
        var issues = new List<string>();
        if (!ValidTimeZone(_config.TimeZone)) issues.Add($"Unknown timezone: {_config.TimeZone}");
        foreach (var value in _config.ServerOverrides.Where(item => !string.IsNullOrWhiteSpace(item.Value.TimeZone) && !ValidTimeZone(item.Value.TimeZone)))
            issues.Add($"Unknown timezone for {value.Key}");
        if (_config.RawDataRetentionDays > _config.AggregateRetentionDays) issues.Add("Raw retention exceeds aggregate retention");
        if (_config.PopulationSnapshotSeconds is < 15 or > 900) issues.Add("Snapshot interval will be clamped to 15–900 seconds");
        if (_config.ChatCategories.Count == 0) issues.Add("No chat categories are configured");
        if (_config.StoreRawChat) issues.Add("Raw chat storage is enabled; use redacted excerpts unless raw text is essential");
        if (!_config.StoreRawChat && !_config.StoreRedactedChatExcerpts) issues.Add("Chat excerpts are disabled; Community voice can only show category counts");
        if (_config.AnonymizationSalt.Length < 16) issues.Add("Anonymization salt is too short");
        issues.AddRange(_playerGuidance.ConfigurationIssues.Select(item => $"Player guidance: {item.Message}"));
        return issues;
    }

    private static IReadOnlyList<RotationRow> RotationRows(DashboardSnapshot snapshot, DateTimeOffset from) => snapshot.MapRounds
        .Where(item => item.StartedAt >= from && item.EndedAt is not null)
        .GroupBy(item => new { item.Game, item.Map, item.Mode })
        .Select(group => new RotationRow(
            group.Key.Game,
            Friendly(group.Key.Map),
            Friendly(group.Key.Mode),
            group.Count(),
            group.Average(item => item.PlayersAtStart),
            group.Average(item => item.PlayersAtEnd),
            group.Average(item => item.PlayersAtEnd - item.PlayersAtStart),
            group.Sum(item => item.Joins),
            group.Sum(item => item.Leaves)))
        .ToList();

    private static IReadOnlyList<ChatMessage> ChatMessages(IEnumerable<ChatSignalRecord> signals) => signals
        .GroupBy(item => string.IsNullOrWhiteSpace(item.MessageId)
            ? $"{item.ServerId}|{item.PlayerKey}|{item.CapturedAt.UtcTicks}|{item.Excerpt}"
            : item.MessageId)
        .Select(group =>
        {
            var first = group.First();
            return new ChatMessage(
                first.ServerId,
                Clean(first.ServerName),
                first.Game,
                Friendly(first.Map),
                Friendly(first.Mode),
                first.CountryCode,
                first.CountryName,
                first.PlayerKey,
                first.CapturedAt,
                first.Excerpt,
                group.Select(item => item.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item).ToArray());
        })
        .ToList();

    private static int UniqueSignalCount(IEnumerable<ChatSignalRecord> signals) => signals
        .Select(item => string.IsNullOrWhiteSpace(item.MessageId)
            ? $"{item.ServerId}|{item.PlayerKey}|{item.CapturedAt.UtcTicks}|{item.Excerpt}"
            : item.MessageId)
        .Distinct(StringComparer.Ordinal)
        .Count();

    private static IReadOnlyList<NetworkBucket> NetworkBuckets(IEnumerable<PopulationSampleRecord> samples) => samples
        .GroupBy(item => item.CapturedAt.UtcTicks / TimeSpan.TicksPerMinute)
        .Select(group => new NetworkBucket(group.Min(item => item.CapturedAt), group.Sum(item => item.HumanPlayers)))
        .OrderBy(item => item.At)
        .ToList();

    private static IReadOnlyList<PlayerSessionRecord> HumanSessions(DashboardSnapshot snapshot, DateTimeOffset from) =>
        snapshot.Sessions.Where(item => item.StartedAt >= from && !item.IsBot).ToList();

    private static IReadOnlyList<PopulationSampleRecord> LatestSamples(DashboardSnapshot snapshot) => snapshot.PopulationSamples
        .GroupBy(item => item.ServerId)
        .Select(group => group.OrderByDescending(item => item.CapturedAt).First())
        .Where(item => item.CapturedAt >= DateTimeOffset.UtcNow.AddMinutes(-10))
        .ToList();

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
            builder.Append("<tr class=\"hover:bg-surface-hover/30\">");
            foreach (var cell in row) builder.Append($"<td class=\"px-5 py-3 text-sm text-foreground\">{cell}</td>");
            builder.Append("</tr>");
        }
        return builder.Append("</tbody></table></div></section>").ToString();
    }

    private static string StatusCard(string label, object value, string detail, string icon, string color, string destination, int days) => $"""
        <a data-enhance-nav="false" href="{Url(destination, days)}" class="group flex min-h-[5.25rem] items-center gap-3 rounded-xl border border-line bg-surface px-4 py-3 shadow-sm hover:border-primary/40 hover:bg-surface-hover/20">
          <span class="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-surface-alt"><i class="ph {E(icon)} text-xl {E(color)}"></i></span>
          <span class="min-w-0"><span class="block text-xl font-bold text-foreground">{E(value)}</span><span class="block text-sm font-semibold text-foreground">{E(label)}</span><span class="block truncate text-xs text-muted">{E(detail)}</span></span>
          <i class="ph ph-caret-right ml-auto text-muted group-hover:text-primary"></i>
        </a>
        """;

    private static string SummaryCard(string label, object value, string icon, string color) => $"""
        <section class="rounded-xl border border-line bg-surface px-4 py-3 shadow-sm">
          <div class="flex items-center justify-between gap-3"><div class="text-xl font-bold text-foreground">{E(value)}</div><i class="ph {E(icon)} text-xl {E(color)}"></i></div>
          <div class="mt-1 text-xs uppercase tracking-wide text-muted">{E(label)}</div>
        </section>
        """;

    private static string ExploreCard(string title, string question, string value, string icon, string destination, int days) => $"""
        <a data-enhance-nav="false" href="{Url(destination, days)}" class="group bg-surface p-5 hover:bg-surface-hover/30">
          <div class="flex items-start justify-between gap-3"><span class="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10"><i class="ph {E(icon)} text-xl text-primary"></i></span><i class="ph ph-arrow-right text-muted group-hover:text-primary"></i></div>
          <h4 class="mt-4 font-semibold text-foreground group-hover:text-primary">{E(title)}</h4><p class="mt-1 text-sm text-muted">{E(question)}</p><div class="mt-3 text-xs font-medium text-muted">{E(value)}</div>
        </a>
        """;

    private static string FilterLinks(string title, int days, string key, string active, IReadOnlyList<(string Value, string Label, int Count)> values)
    {
        var builder = new StringBuilder($"<section class=\"overflow-x-auto rounded-xl border border-line bg-surface px-4 py-3 shadow-sm\"><div class=\"flex min-w-max items-center gap-2\"><span class=\"mr-2 text-xs font-semibold uppercase tracking-wide text-muted\">{E(title)}</span>");
        foreach (var item in values)
            builder.Append($"<a data-enhance-nav=\"false\" href=\"{Url("maps", days, $"{key}={WebUtility.UrlEncode(item.Value)}")}\" class=\"rounded-lg px-3 py-2 text-sm font-medium {(active == item.Value ? "bg-action-primary text-white" : "bg-surface-alt text-muted hover:text-foreground")}\">{E(item.Label)} <span class=\"ml-1 opacity-75\">{item.Count:N0}</span></a>");
        return builder.Append("</div></section>").ToString();
    }

    private static string Pager(string view, int days, int page, int pages, string extra)
    {
        if (pages <= 1) return string.Empty;
        var builder = new StringBuilder("<nav class=\"flex items-center justify-between rounded-xl border border-line bg-surface px-4 py-3 shadow-sm\" aria-label=\"Pagination\">")
            .Append(page > 1 ? $"<a data-enhance-nav=\"false\" href=\"{Url(view, days, $"{extra}&page={page - 1}")}\" class=\"text-sm font-medium text-primary hover:underline\"><i class=\"ph ph-arrow-left\"></i> Previous</a>" : "<span></span>")
            .Append($"<span class=\"text-sm text-muted\">Page {page:N0} of {pages:N0}</span>")
            .Append(page < pages ? $"<a data-enhance-nav=\"false\" href=\"{Url(view, days, $"{extra}&page={page + 1}")}\" class=\"text-sm font-medium text-primary hover:underline\">Next <i class=\"ph ph-arrow-right\"></i></a>" : "<span></span>");
        return builder.Append("</nav>").ToString();
    }

    private static string Badge(string value, string color)
    {
        var classes = color switch
        {
            "red" => "border-red-500/40 bg-red-500/10 text-red-300",
            "amber" => "border-amber-500/40 bg-amber-500/10 text-amber-300",
            "green" => "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
            _ => "border-primary/40 bg-primary/10 text-primary"
        };
        return $"<span class=\"inline-flex rounded-full border px-2 py-0.5 text-xs font-semibold {classes}\">{E(value)}</span>";
    }

    private static string Url(string view, int days, string? extra = null) =>
        $"/Interaction/Render/{InteractionKey}?view={WebUtility.UrlEncode(view)}&period={days}" +
        (string.IsNullOrWhiteSpace(extra) ? string.Empty : $"&{extra.TrimStart('&')}");

    private static int ReadInteger(IDictionary<string, string> meta, string key, int fallback, int minimum, int maximum) =>
        meta.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;

    private static string? ReadValue(IDictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out var value) ? value : null;

    private static int DaysFrom(DateTimeOffset from) => Math.Max(1, (int)Math.Round((DateTimeOffset.UtcNow - from).TotalDays));
    private static string PeriodLabel(int days) => days == 1 ? "In the last 24 hours" : $"In the last {days:N0} days";
    private static string ShortPlayer(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value[^Math.Min(6, value.Length)..];
    private static bool ValidCountryCode(string? code) => code is { Length: 2 } && code.All(character => char.IsAsciiLetter(character));
    private static string CountryFlag(string? code)
    {
        if (!ValidCountryCode(code)) return "🌐";
        var value = code!.ToUpperInvariant();
        return char.ConvertFromUtf32(0x1F1E6 + value[0] - 'A') + char.ConvertFromUtf32(0x1F1E6 + value[1] - 'A');
    }
    private static string Clean(string? value) => AnalyticsEngine.CleanDisplayText(value);
    private static string Friendly(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    private static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? string.Empty);

    private static string ActionDestination(Recommendation item)
    {
        var text = $"{item.Title} {item.Detail}".ToLowerInvariant();
        if (text.Contains("rotation") || text.Contains("map")) return "maps";
        if (text.Contains("feedback") || text.Contains("reports") || text.Contains("message")) return "chat";
        if (text.Contains("audience") || text.Contains("country")) return "audience";
        if (text.Contains("monitoring") || text.Contains("incident")) return "health";
        return "servers";
    }

    private static string NormalizeView(string? value) => value?.ToLowerInvariant() switch
    {
        "servers" => "servers",
        "maps" => "maps",
        "activity" => "activity",
        "audience" => "audience",
        "chat" => "chat",
        "guidance" => "guidance",
        "actions" => "actions",
        "health" => "health",
        _ => "overview"
    };

    private static bool ValidTimeZone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(value); return true; }
        catch { return false; }
    }

    private static readonly IReadOnlyDictionary<string, (string Title, string Description, string Icon)> Sections =
        new Dictionary<string, (string, string, string)>
        {
            ["servers"] = ("Traffic & retention", "Compare human demand, session quality and repeat visits by server.", "ph-hard-drives"),
            ["maps"] = ("Rotation performance", "Find map and mode pairings that gain or lose players.", "ph-map-trifold"),
            ["activity"] = ("Busy times & exits", "Plan events around real activity and understand how sessions end.", "ph-calendar-dots"),
            ["audience"] = ("Player audience", "See privacy-safe regional demand in the configured dashboard timezone.", "ph-globe-hemisphere-west"),
            ["chat"] = ("Community voice", "Read the redacted complaints, requests and praise behind the category counts.", "ph-chats-circle"),
            ["guidance"] = ("Player guidance", "Measure accusations, report follow-through, repeated targets and privacy-safe staff escalations.", "ph-shield-warning"),
            ["actions"] = ("Action plan", "Prioritised experiments and warnings derived from the selected period.", "ph-lightbulb"),
            ["health"] = ("Data health", "Verify collection, storage, privacy and live server telemetry.", "ph-heartbeat")
        };

    private sealed record RotationRow(string Game, string Map, string Mode, int Rounds, double AverageStart, double AverageEnd, double Change, int Joins, int Leaves);
    private sealed record AudienceRow(string CountryCode, string CountryName, int Players, int Sessions, double Average, int PeakHour);
    private sealed record ChatMessage(string ServerId, string ServerName, string Game, string Map, string Mode, string CountryCode, string CountryName, string PlayerKey, DateTimeOffset CapturedAt, string? Excerpt, IReadOnlyList<string> Categories);
    private sealed record NetworkBucket(DateTimeOffset At, int Humans);

    private void OnConfigurationUpdated(ServerPulseConfig _) => Register();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _configurationHandler.Updated -= OnConfigurationUpdated;
        _interactions.UnregisterInteraction(InteractionKey);
    }
}
