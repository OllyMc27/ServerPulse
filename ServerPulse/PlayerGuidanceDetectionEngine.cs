using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SharedLibraryCore;

namespace ServerPulse;

public sealed record GuidanceDetectionMatch(string Category, string Pattern, bool IsRegex);

public sealed class PlayerGuidanceDetectionEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private volatile DetectionSnapshot _snapshot = DetectionSnapshot.Empty;

    public IReadOnlyList<GuidanceConfigurationIssue> Issues => _snapshot.Issues;

    public void Reload(PlayerGuidanceConfig config)
    {
        var issues = Validate(config);
        var categories = new List<CompiledCategory>();

        foreach (var category in config.Categories.Where(item => item.Enabled))
        {
            var sources = category.Phrases.Select(phrase => (Phrase: phrase, Mode: category.MatchMode));
            if (category.Name.Equals("cheating", StringComparison.OrdinalIgnoreCase))
                sources = sources.Concat(config.CommunityReportPhrases.Select(phrase => (phrase, GuidancePhraseMatchMode.WholeWord)));

            var phrases = sources
                .Where(source => !string.IsNullOrWhiteSpace(source.Phrase))
                .Select(source => new CompiledPhrase(source.Phrase, Normalize(source.Phrase, config.EnableLeetNormalization), source.Mode))
                .Where(phrase => phrase.Normalized.Length > 0)
                .DistinctBy(phrase => (phrase.Normalized, phrase.Mode))
                .ToArray();
            var regexes = new List<CompiledRegex>();
            foreach (var pattern in category.RegexPatterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)))
            {
                try
                {
                    regexes.Add(new CompiledRegex(pattern, new Regex(pattern,
                        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        RegexTimeout)));
                }
                catch (ArgumentException)
                {
                    // Validation reports the invalid pattern; valid rules remain active.
                }
            }
            categories.Add(new CompiledCategory(category.Name, phrases, regexes.ToArray()));
        }

        _snapshot = new DetectionSnapshot(
            config.EnableLeetNormalization,
            categories.ToArray(),
            NormalizeMany(config.ExcludedPhrases.Concat(config.CommunityReportExclusions), config.EnableLeetNormalization),
            issues.ToArray());
    }

    public GuidanceDetectionMatch? Detect(string message, IEnumerable<string>? additionalExclusions = null)
    {
        var snapshot = _snapshot;
        var clean = (message ?? string.Empty).StripColors().Normalize(NormalizationForm.FormKC);
        var normalized = Normalize(clean, snapshot.EnableLeetNormalization);
        if (normalized.Length == 0)
            return null;

        if (snapshot.Exclusions.Any(exclusion => ContainsWholePhrase(normalized, exclusion)) ||
            NormalizeMany(additionalExclusions, snapshot.EnableLeetNormalization)
                .Any(exclusion => ContainsWholePhrase(normalized, exclusion)))
            return null;

        foreach (var category in snapshot.Categories)
        {
            foreach (var phrase in category.Phrases)
            {
                var matched = phrase.Mode == GuidancePhraseMatchMode.Substring
                    ? normalized.Contains(phrase.Normalized, StringComparison.Ordinal)
                    : ContainsWholePhrase(normalized, phrase.Normalized);
                if (matched)
                    return new GuidanceDetectionMatch(category.Name, phrase.Original, false);
            }

            foreach (var regex in category.RegexPatterns)
            {
                try
                {
                    if (regex.Regex.IsMatch(clean))
                        return new GuidanceDetectionMatch(category.Name, regex.Original, true);
                }
                catch (RegexMatchTimeoutException)
                {
                    // Expensive custom rules are skipped for this message.
                }
            }
        }
        return null;
    }

    public static string Normalize(string value, bool leet)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var source = value.StripColors().Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(source.Length);
        var previousWasSpace = true;
        foreach (var raw in source)
        {
            var character = char.ToLower(raw, CultureInfo.InvariantCulture);
            if (leet)
            {
                character = character switch
                {
                    '0' => 'o',
                    '1' or '|' => 'i',
                    '3' => 'e',
                    '4' or '@' => 'a',
                    '5' or '$' => 's',
                    '7' => 't',
                    '9' => 'g',
                    _ => character
                };
            }
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }
        return CollapseSeparatedLetters(builder.ToString().Trim());
    }

    public static bool ContainsWholePhrase(string message, string phrase)
    {
        if (message.Length == 0 || phrase.Length == 0) return false;
        var start = 0;
        while ((start = message.IndexOf(phrase, start, StringComparison.Ordinal)) >= 0)
        {
            var before = start == 0 || !char.IsLetterOrDigit(message[start - 1]);
            var end = start + phrase.Length;
            var after = end == message.Length || !char.IsLetterOrDigit(message[end]);
            if (before && after) return true;
            start++;
        }
        return false;
    }

    private static string CollapseSeparatedLetters(string value)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3) return value;
        var result = new List<string>(tokens.Length);
        for (var index = 0; index < tokens.Length;)
        {
            var end = index;
            while (end < tokens.Length && tokens[end].Length == 1 && char.IsLetter(tokens[end][0])) end++;
            if (end - index >= 3)
            {
                result.Add(string.Concat(tokens[index..end]));
                index = end;
            }
            else
            {
                result.Add(tokens[index]);
                index++;
            }
        }
        return string.Join(' ', result);
    }

    private static IReadOnlyList<GuidanceConfigurationIssue> Validate(PlayerGuidanceConfig config)
    {
        var issues = new List<GuidanceConfigurationIssue>();
        if (string.IsNullOrWhiteSpace(config.ReportCommand)) issues.Add(Error("ReportCommand cannot be empty."));
        if (config.PlayerCooldownSeconds < 0) issues.Add(Error("PlayerCooldownSeconds cannot be negative."));
        if (config.ServerCooldownSeconds < 0) issues.Add(Error("ServerCooldownSeconds cannot be negative."));
        if (config.MinimumTargetNameLength is < 2 or > 32) issues.Add(Error("MinimumTargetNameLength must be between 2 and 32."));
        if (config.MaxMessageLength is < 40 or > 1000) issues.Add(Error("MaxMessageLength must be between 40 and 1000."));
        if (config.StaffAlertThreshold < 1) issues.Add(Error("StaffAlertThreshold must be at least 1."));
        if (config.StaffAlertWindowSeconds < 1) issues.Add(Error("StaffAlertWindowSeconds must be at least 1."));
        if (config.Categories.Count == 0) issues.Add(Error("At least one guidance category is required."));
        if (config.ReminderMessages.Count == 0) issues.Add(Error("At least one reminder message is required."));

        var categoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var phrases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in config.Categories)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
            {
                issues.Add(Error("A guidance category has an empty name."));
                continue;
            }
            if (!categoryNames.Add(category.Name)) issues.Add(Error($"Guidance category '{category.Name}' is duplicated."));
            if (category.Enabled && category.Phrases.Count == 0 && category.RegexPatterns.Count == 0)
                issues.Add(Warning($"Enabled guidance category '{category.Name}' has no rules."));
            foreach (var phrase in category.Phrases.Where(phrase => !string.IsNullOrWhiteSpace(phrase)))
            {
                var normalized = Normalize(phrase, config.EnableLeetNormalization);
                if (phrases.TryGetValue(normalized, out var existing))
                    issues.Add(Warning($"Phrase '{phrase}' in '{category.Name}' duplicates a rule in '{existing}'."));
                else
                    phrases[normalized] = category.Name;
            }
            foreach (var pattern in category.RegexPatterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)))
            {
                try { _ = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout); }
                catch (ArgumentException exception) { issues.Add(Error($"Invalid regex in '{category.Name}': {exception.Message}")); }
            }
        }
        return issues;
    }

    private static string[] NormalizeMany(IEnumerable<string>? values, bool leet) => values?
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => Normalize(value, leet))
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToArray() ?? [];

    private static GuidanceConfigurationIssue Error(string message) => new(GuidanceConfigurationIssueSeverity.Error, message);
    private static GuidanceConfigurationIssue Warning(string message) => new(GuidanceConfigurationIssueSeverity.Warning, message);
    private sealed record CompiledPhrase(string Original, string Normalized, GuidancePhraseMatchMode Mode);
    private sealed record CompiledRegex(string Original, Regex Regex);
    private sealed record CompiledCategory(string Name, CompiledPhrase[] Phrases, CompiledRegex[] RegexPatterns);
    private sealed record DetectionSnapshot(bool EnableLeetNormalization, CompiledCategory[] Categories, string[] Exclusions, GuidanceConfigurationIssue[] Issues)
    {
        public static readonly DetectionSnapshot Empty = new(false, [], [], []);
    }
}
