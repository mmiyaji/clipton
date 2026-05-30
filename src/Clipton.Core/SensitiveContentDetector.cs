using System.Text.RegularExpressions;

namespace Clipton.Core;

public static class SensitiveContentDetector
{
    private const int DefaultVisiblePrefixLength = 3;
    private const int MaskGlyphCount = 8;

    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CreditCardPattern = new(
        @"\b(?:\d[ -]*?){13,19}\b",
        RegexOptions.Compiled);

    private static readonly Regex SecretKeywordPattern = new(
        @"\b(password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|private[_-]?key|bearer)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SecretAssignmentPattern = new(
        @"\b(password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|private[_-]?key)\b(\s*[:=]\s*)([^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerTokenPattern = new(
        @"\bbearer(\s+)([A-Za-z0-9._\-]{8,})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LongTokenPattern = new(
        @"\b[A-Za-z0-9_\-]{32,}\b",
        RegexOptions.Compiled);

    private static readonly Regex PhonePattern = new(
        @"(?<!\d)(?:\+?\d{1,3}[-. ]?)?(?:\(?\d{2,4}\)?[-. ]?){2,4}\d{3,4}(?!\d)",
        RegexOptions.Compiled);

    public static bool ShouldMask(string? text, IEnumerable<string>? customPatterns = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return EmailPattern.IsMatch(text)
            || SecretKeywordPattern.IsMatch(text)
            || LongTokenPattern.IsMatch(text)
            || PhonePattern.IsMatch(text)
            || LooksLikeCreditCard(text)
            || MatchesCustomPattern(text, customPatterns);
    }

    public static string? CreateMaskedPreview(
        string? text,
        int visiblePrefixLength = DefaultVisiblePrefixLength,
        IEnumerable<string>? customPatterns = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var result = text;
        result = SecretAssignmentPattern.Replace(
            result,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}{MaskValue(match.Groups[3].Value, visiblePrefixLength)}");
        result = BearerTokenPattern.Replace(
            result,
            match => $"bearer{match.Groups[1].Value}{MaskValue(match.Groups[2].Value, visiblePrefixLength)}");
        result = ReplaceSensitiveMatches(result, EmailPattern, visiblePrefixLength, _ => true);
        result = ReplaceSensitiveMatches(result, CreditCardPattern, visiblePrefixLength, IsCreditCardMatch);
        result = ReplaceSensitiveMatches(result, LongTokenPattern, visiblePrefixLength, _ => true);
        result = ReplaceSensitiveMatches(result, PhonePattern, visiblePrefixLength, _ => true);
        result = ReplaceCustomMatches(result, customPatterns, visiblePrefixLength);

        if (!string.Equals(result, text, StringComparison.Ordinal))
        {
            return result;
        }

        return SecretKeywordPattern.IsMatch(text)
            ? MaskValue(text, visiblePrefixLength)
            : null;
    }

    public static string[] ValidateCustomPatterns(IEnumerable<string>? customPatterns)
    {
        return GetValidCustomPatterns(customPatterns);
    }

    public static string[] GetInvalidCustomPatterns(IEnumerable<string>? customPatterns)
    {
        if (customPatterns is null)
        {
            return [];
        }

        return customPatterns
            .Select(pattern => pattern.Trim())
            .Where(pattern => pattern.Length > 0)
            .Where(pattern =>
            {
                try
                {
                    _ = new Regex(pattern, RegexOptions.IgnoreCase);
                    return false;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] GetValidCustomPatterns(IEnumerable<string>? customPatterns)
    {
        if (customPatterns is null)
        {
            return [];
        }

        return customPatterns
            .Select(pattern => pattern.Trim())
            .Where(pattern => pattern.Length > 0)
            .Where(pattern =>
            {
                try
                {
                    _ = new Regex(pattern, RegexOptions.IgnoreCase);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ReplaceSensitiveMatches(
        string text,
        Regex pattern,
        int visiblePrefixLength,
        Func<Match, bool> shouldMask)
    {
        return pattern.Replace(text, match => shouldMask(match)
            ? MaskValue(match.Value, visiblePrefixLength)
            : match.Value);
    }

    private static string MaskValue(string value, int visiblePrefixLength)
    {
        var visible = value[..Math.Min(Math.Max(visiblePrefixLength, 0), value.Length)];
        return $"{visible}{new string('\u2022', MaskGlyphCount)}";
    }

    private static bool MatchesCustomPattern(string text, IEnumerable<string>? customPatterns)
    {
        foreach (var pattern in ValidateCustomPatterns(customPatterns))
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReplaceCustomMatches(string text, IEnumerable<string>? customPatterns, int visiblePrefixLength)
    {
        var result = text;
        foreach (var pattern in ValidateCustomPatterns(customPatterns))
        {
            result = Regex.Replace(
                result,
                pattern,
                match => MaskValue(match.Value, visiblePrefixLength),
                RegexOptions.IgnoreCase);
        }

        return result;
    }

    private static bool IsCreditCardMatch(Match match)
    {
        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        return digits.Length is >= 13 and <= 19 && PassesLuhn(digits);
    }

    private static bool LooksLikeCreditCard(string text)
    {
        foreach (Match match in CreditCardPattern.Matches(text))
        {
            if (IsCreditCardMatch(match))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                {
                    n -= 9;
                }
            }

            sum += n;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }
}
