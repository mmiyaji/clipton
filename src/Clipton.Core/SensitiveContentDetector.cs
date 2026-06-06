using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Clipton.Core;

public static class SensitiveContentDetector
{
    private const int DefaultVisiblePrefixLength = 3;
    public const int DefaultPreviewScanLength = 1000;
    private const int MaskGlyphCount = 8;
    private static readonly TimeSpan CustomPatternTimeout = TimeSpan.FromMilliseconds(120);
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);

    public static bool ShouldMask(string? text, IEnumerable<string>? customPatterns = null, MaskRuleSettings? rules = null)
    {
        rules ??= new MaskRuleSettings();
        return ShouldMask(
            text,
            MaskRuleDefinitionDefaults.CreateDefaultRules(rules),
            customPatterns,
            rules.CustomPattern);
    }

    public static bool ShouldMask(
        string? text,
        IEnumerable<MaskRuleDefinition>? maskRuleDefinitions,
        IEnumerable<string>? customPatterns = null,
        bool customPatternsEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var rule in MaskRuleDefinitionDefaults.Normalize(maskRuleDefinitions))
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }

            if (rule.Id == MaskRuleIds.CreditCard)
            {
                if (LooksLikeCreditCard(text, rule.Pattern))
                {
                    return true;
                }

                continue;
            }

            if (TryGetRegex(rule.Pattern, out var regex) && regex.IsMatch(text))
            {
                return true;
            }
        }

        return customPatternsEnabled && MatchesCustomPattern(text, customPatterns);
    }

    public static MaskRuleMatch[] FindMatchedRules(
        string? text,
        IEnumerable<MaskRuleDefinition>? maskRuleDefinitions,
        IEnumerable<string>? customPatterns = null,
        bool customPatternsEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var matches = new List<MaskRuleMatch>();
        foreach (var rule in MaskRuleDefinitionDefaults.Normalize(maskRuleDefinitions))
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }

            var matched = rule.Id == MaskRuleIds.CreditCard
                ? LooksLikeCreditCard(text, rule.Pattern)
                : TryGetRegex(rule.Pattern, out var regex) && regex.IsMatch(text);
            if (matched)
            {
                matches.Add(new MaskRuleMatch(rule.Id, rule.NameKey, rule.Pattern, IsCustomPattern: false));
            }
        }

        if (customPatternsEnabled)
        {
            foreach (var pattern in ValidateCustomPatterns(customPatterns))
            {
                if (TryGetRegex(pattern, out var regex) && regex.IsMatch(text))
                {
                    matches.Add(new MaskRuleMatch("custom", "MaskRuleCustomPattern", pattern, IsCustomPattern: true));
                }
            }
        }

        return matches
            .DistinctBy(match => (match.RuleId, match.Pattern))
            .ToArray();
    }

    public static string? CreateMaskedPreview(
        string? text,
        int visiblePrefixLength = DefaultVisiblePrefixLength,
        IEnumerable<string>? customPatterns = null,
        MaskRuleSettings? rules = null)
    {
        rules ??= new MaskRuleSettings();
        return CreateMaskedPreview(
            text,
            visiblePrefixLength,
            MaskRuleDefinitionDefaults.CreateDefaultRules(rules),
            customPatterns,
            rules.CustomPattern);
    }

    public static string? CreatePreviewScanText(string? text, int maxLength = DefaultPreviewScanLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.ReplaceLineEndings(" ").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var splitAt = normalized.LastIndexOf(' ', maxLength - 1, maxLength);
        if (splitAt < maxLength / 2)
        {
            splitAt = maxLength;
        }

        return normalized[..splitAt].TrimEnd();
    }

    public static string? CreateMaskedPreview(
        string? text,
        int visiblePrefixLength,
        IEnumerable<MaskRuleDefinition>? maskRuleDefinitions,
        IEnumerable<string>? customPatterns = null,
        bool customPatternsEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var result = text;
        foreach (var rule in MaskRuleDefinitionDefaults.Normalize(maskRuleDefinitions))
        {
            if (!rule.Enabled || !TryGetRegex(rule.Pattern, out var regex))
            {
                continue;
            }

            result = rule.Id switch
            {
                MaskRuleIds.SecretKeyword => regex.Replace(
                    result,
                    match => match.Groups.Count >= 4
                        ? $"{match.Groups[1].Value}{match.Groups[2].Value}{MaskValue(match.Groups[3].Value, visiblePrefixLength)}"
                        : MaskValue(match.Value, visiblePrefixLength)),
                MaskRuleIds.BearerToken => regex.Replace(
                    result,
                    match => match.Groups.Count >= 3
                        ? $"bearer{match.Groups[1].Value}{MaskValue(match.Groups[2].Value, visiblePrefixLength)}"
                        : MaskValue(match.Value, visiblePrefixLength)),
                MaskRuleIds.CreditCard => ReplaceSensitiveMatches(result, regex, visiblePrefixLength, IsCreditCardMatch),
                _ => ReplaceSensitiveMatches(result, regex, visiblePrefixLength, _ => true)
            };
        }

        if (customPatternsEnabled)
        {
            result = ReplaceCustomMatches(result, customPatterns, visiblePrefixLength);
        }

        if (!string.Equals(result, text, StringComparison.Ordinal))
        {
            return result;
        }

        return null;
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
                    _ = CreateRegex(pattern);
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
                    _ = CreateRegex(pattern);
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
            if (TryGetRegex(pattern, out var regex) && regex.IsMatch(text))
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
            if (TryGetRegex(pattern, out var regex))
            {
                result = regex.Replace(
                    result,
                    match => MaskValue(match.Value, visiblePrefixLength));
            }
        }

        return result;
    }

    private static bool IsCreditCardMatch(Match match)
    {
        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        return digits.Length is >= 13 and <= 19 && PassesLuhn(digits);
    }

    private static bool LooksLikeCreditCard(string text, string pattern)
    {
        if (!TryGetRegex(pattern, out var regex))
        {
            return false;
        }

        foreach (Match match in regex.Matches(text))
        {
            if (IsCreditCardMatch(match))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetRegex(string pattern, out Regex regex)
    {
        try
        {
            regex = RegexCache.GetOrAdd(pattern, static key => CreateRegex(key));
            return true;
        }
        catch (ArgumentException)
        {
            regex = null!;
            return false;
        }
    }

    private static Regex CreateRegex(string pattern)
    {
        return new Regex(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
            CustomPatternTimeout);
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

public sealed record MaskRuleMatch(string RuleId, string NameKey, string Pattern, bool IsCustomPattern);
