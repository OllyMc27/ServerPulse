namespace ServerPulse;

public static class GuidanceTargetResolver
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "at", "bro", "dude", "for", "guy", "has", "have", "he", "her", "him",
        "his", "i", "is", "it", "just", "lol", "nice", "of", "on", "player", "she", "that", "the", "their",
        "them", "they", "this", "to", "use", "using", "was", "with", "you", "your"
    };

    public static int? ResolveUniqueClientId(
        string message,
        string matchedPattern,
        string category,
        IEnumerable<(int ClientId, string Name)> players,
        int minimumLength,
        bool enableLeetNormalization)
    {
        minimumLength = Math.Max(3, minimumLength);
        var normalizedMessage = PlayerGuidanceDetectionEngine.Normalize(message, enableLeetNormalization);
        var candidates = players
            .Select(player => (player.ClientId,
                Name: PlayerGuidanceDetectionEngine.Normalize(player.Name, enableLeetNormalization)))
            .Where(player => player.Name.Length >= minimumLength)
            .ToArray();

        var fullMatches = candidates
            .Where(player => PlayerGuidanceDetectionEngine.ContainsWholePhrase(normalizedMessage, player.Name))
            .Select(player => player.ClientId)
            .Distinct()
            .ToArray();
        if (fullMatches.Length == 1)
            return fullMatches[0];
        if (fullMatches.Length > 1)
            return null;

        var excluded = PlayerGuidanceDetectionEngine.Normalize($"{matchedPattern} {category}", enableLeetNormalization)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var fragments = normalizedMessage
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(value => value.Length >= minimumLength && !StopWords.Contains(value) && !excluded.Contains(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (fragments.Length == 0)
            return null;

        var partialMatches = candidates
            .Where(candidate => fragments.Any(fragment =>
                candidate.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => part.StartsWith(fragment, StringComparison.Ordinal))))
            .Select(candidate => candidate.ClientId)
            .Distinct()
            .ToArray();
        return partialMatches.Length == 1 ? partialMatches[0] : null;
    }
}
