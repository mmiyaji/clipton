namespace Clipton.Core;

/// <summary>
/// Normalizes and matches user-configured app names excluded from clipboard capture.
/// </summary>
public static class ApplicationExclusionList
{
    private const int MaxPatterns = 100;
    private const int MaxPatternLength = 128;

    public static string[] Normalize(IEnumerable<string>? patterns)
    {
        return (patterns ?? [])
            .Select(NormalizePattern)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPatterns)
            .ToArray();
    }

    public static bool Matches(IEnumerable<string>? patterns, string? applicationName)
    {
        var appName = NormalizeApplicationName(applicationName);
        if (string.IsNullOrWhiteSpace(appName))
        {
            return false;
        }

        return Normalize(patterns).Any(pattern => MatchesPattern(pattern, appName));
    }

    private static string NormalizePattern(string? value)
    {
        var normalized = NormalizeApplicationName(value);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.All(c => c == '*'))
        {
            return string.Empty;
        }

        return normalized.Length <= MaxPatternLength
            ? normalized
            : normalized[..MaxPatternLength];
    }

    private static string NormalizeApplicationName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Trim('"', '\'');
        var slashIndex = normalized.LastIndexOfAny(['\\', '/']);
        if (slashIndex >= 0 && slashIndex < normalized.Length - 1)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }

    private static bool MatchesPattern(string pattern, string value)
    {
        if (!pattern.Contains('*'))
        {
            return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
        }

        var parts = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var index = 0;
        foreach (var part in parts)
        {
            var next = value.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
            if (next < 0)
            {
                return false;
            }

            index = next + part.Length;
        }

        var startsCorrectly = pattern.StartsWith('*') || value.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase);
        var endsCorrectly = pattern.EndsWith('*') || value.EndsWith(parts[^1], StringComparison.OrdinalIgnoreCase);
        return startsCorrectly && endsCorrectly;
    }
}
