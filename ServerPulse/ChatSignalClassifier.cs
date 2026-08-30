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
            .Where(category => category.Value.Any(phrase => ContainsPhrase(normalized, phrase)))
            .Select(category => category.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string? Excerpt(string? message)
    {
        if (!_config.StoreRedactedChatExcerpts || string.IsNullOrWhiteSpace(message))
            return null;

        var redacted = Regex.Replace(message, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[address]");
        redacted = Regex.Replace(redacted, @"https?://\S+", "[link]");
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
}
