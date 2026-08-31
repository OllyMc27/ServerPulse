using System.Text.RegularExpressions;

namespace ServerPulse;

public sealed class ChatSignalClassifier
{
    private readonly ServerPulseConfig _config;

    public ChatSignalClassifier(ServerPulseConfig config) => _config = config;

    public IReadOnlyList<string> Classify(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        var normalized = Regex.Replace(message.ToLowerInvariant(), @"\s+", " ").Trim();
        return _config.ChatCategories
            .Where(category => !IsNegated(category.Key, normalized) &&
                category.Value.Any(phrase => ContainsPhrase(normalized, phrase)))
            .Select(category => category.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string? Excerpt(string? message)
    {
        if (!_config.StoreRedactedChatExcerpts || string.IsNullOrWhiteSpace(message))
            return null;

        var redacted = Regex.Replace(message, @"\^(?:[0-9])", string.Empty);
        redacted = Regex.Replace(redacted, @"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b", "[address]");
        redacted = Regex.Replace(redacted, @"\b(?:https?://|www\.)\S+", "[link]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, @"\bdiscord(?:app)?\.(?:gg|com/invite)/\S+", "[invite]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[email]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, @"(?<!\d)\d{15,20}(?!\d)", "[id]");
        redacted = Regex.Replace(redacted, @"\s+", " ").Trim();
        var maximum = Math.Clamp(_config.ChatExcerptMaximumLength, 20, 250);
        return redacted.Length <= maximum ? redacted : redacted[..maximum] + "…";
    }

    private static bool ContainsPhrase(string message, string phrase)
    {
        var value = phrase.Trim().ToLowerInvariant();
        if (value.Length == 0)
            return false;
        if (value.Contains(' '))
            return message.Contains(value, StringComparison.OrdinalIgnoreCase);
        return Regex.IsMatch(message, $@"(?<![a-z0-9]){Regex.Escape(value)}(?![a-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool IsNegated(string category, string message)
    {
        if (category.Equals("Lag", StringComparison.OrdinalIgnoreCase))
            return ContainsAny(message, ["no lag", "zero lag", "lag free", "lag-free", "not lagging", "good ping", "low ping"]);
        if (category.Equals("Cheating", StringComparison.OrdinalIgnoreCase))
            return ContainsAny(message, ["not cheating", "not a cheater", "no cheaters", "no hackers"]);
        return false;
    }

    private static bool ContainsAny(string message, IEnumerable<string> phrases) =>
        phrases.Any(phrase => message.Contains(phrase, StringComparison.OrdinalIgnoreCase));
}
