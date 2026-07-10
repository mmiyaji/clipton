using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Clipton.Core;

/// <summary>
/// Detects and masks sensitive-looking text for history previews.
/// </summary>
/// <remarks>
/// Detection is preview-only: the original clipboard payload remains unchanged so paste
/// fidelity is preserved. Custom patterns are compiled with a timeout and invalid
/// patterns are ignored by matching APIs. A match timeout is treated as sensitive so
/// user-supplied regular expressions cannot expose preview text or crash history rendering.
/// </remarks>
public static class SensitiveContentDetector
{
    private const int DefaultVisiblePrefixLength = 3;
    public const int DefaultPreviewScanLength = 1000;
    private const int MaskGlyphCount = 8;
    private static readonly TimeSpan CustomPatternTimeout = TimeSpan.FromMilliseconds(120);
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns whether text matches any enabled built-in or custom masking rule.
    /// </summary>
    public static bool ShouldMask(string? text, IEnumerable<string>? customPatterns = null, MaskRuleSettings? rules = null)
    {
        rules ??= new MaskRuleSettings();
        return ShouldMask(
            text,
            MaskRuleDefinitionDefaults.CreateDefaultRules(rules),
            customPatterns,
            rules.CustomPattern);
    }

    /// <summary>
    /// Returns whether text matches any supplied masking rule definition.
    /// </summary>
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

        try
        {
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
        catch (RegexMatchTimeoutException)
        {
            // Preview masking is privacy-sensitive. An indeterminate match is treated as
            // sensitive instead of exposing content or crashing the history surface.
            return true;
        }
    }

    /// <summary>
    /// Returns the enabled rules that match the supplied text.
    /// </summary>
    /// <remarks>
    /// Duplicate rule/pattern pairs are collapsed so the UI can show a concise explanation.
    /// </remarks>
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

            bool matched;
            try
            {
                matched = rule.Id == MaskRuleIds.CreditCard
                    ? LooksLikeCreditCard(text, rule.Pattern)
                    : TryGetRegex(rule.Pattern, out var regex) && regex.IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                matches.Add(new MaskRuleMatch(rule.Id, rule.NameKey, rule.Pattern, IsCustomPattern: false));
                return DistinctMatches(matches);
            }

            if (matched)
            {
                matches.Add(new MaskRuleMatch(rule.Id, rule.NameKey, rule.Pattern, IsCustomPattern: false));
            }
        }

        if (customPatternsEnabled)
        {
            foreach (var pattern in ValidateCustomPatterns(customPatterns))
            {
                try
                {
                    if (TryGetRegex(pattern, out var regex) && regex.IsMatch(text))
                    {
                        matches.Add(new MaskRuleMatch("custom", "MaskRuleCustomPattern", pattern, IsCustomPattern: true));
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    matches.Add(new MaskRuleMatch("custom", "MaskRuleCustomPattern", pattern, IsCustomPattern: true));
                    return DistinctMatches(matches);
                }
            }
        }

        return DistinctMatches(matches);
    }

    /// <summary>
    /// Creates a masked preview using the settings-shaped rule model.
    /// </summary>
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

    /// <summary>
    /// Normalizes and truncates text before preview scanning.
    /// </summary>
    /// <remarks>
    /// Scanning only the preview-sized prefix bounds regex cost and avoids masking rules
    /// being driven by text the user will not see in the list.
    /// </remarks>
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

        splitAt = GetTextElementBoundaryAtOrBefore(normalized, splitAt);
        return normalized[..splitAt].TrimEnd();
    }

    /// <summary>
    /// Creates a masked preview using explicit rule definitions.
    /// </summary>
    /// <returns>
    /// A masked string when at least one replacement changed the text; otherwise <see langword="null"/>.
    /// </returns>
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

        try
        {
            var replacements = new List<MaskReplacement>();
            foreach (var rule in MaskRuleDefinitionDefaults.Normalize(maskRuleDefinitions))
            {
                if (!rule.Enabled || !TryGetRegex(rule.Pattern, out var regex))
                {
                    continue;
                }

                replacements.AddRange(CreateReplacements(text, rule.Id, regex, visiblePrefixLength));
            }

            if (customPatternsEnabled)
            {
                replacements.AddRange(CreateCustomReplacements(text, customPatterns, visiblePrefixLength));
            }

            var result = ApplyReplacements(text, replacements);
            if (!string.Equals(result, text, StringComparison.Ordinal))
            {
                return result;
            }

            return null;
        }
        catch (RegexMatchTimeoutException)
        {
            // Do not retain even the configured visible prefix when the match result is unknown.
            return MaskValue(text, visiblePrefixLength: 0);
        }
    }

    /// <summary>
    /// Returns valid custom regular expressions after trimming blanks and duplicates.
    /// </summary>
    public static string[] ValidateCustomPatterns(IEnumerable<string>? customPatterns)
    {
        return GetValidCustomPatterns(customPatterns);
    }

    /// <summary>
    /// Returns custom regular expressions that cannot be compiled by the detector.
    /// </summary>
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

    private static IEnumerable<MaskReplacement> CreateReplacements(
        string text,
        string ruleId,
        Regex pattern,
        int visiblePrefixLength)
    {
        foreach (Match match in pattern.Matches(text))
        {
            if (ruleId == MaskRuleIds.CreditCard && !IsCreditCardMatch(match))
            {
                continue;
            }

            var masked = ruleId switch
            {
                MaskRuleIds.SecretKeyword when match.Groups.Count >= 4 =>
                    $"{match.Groups[1].Value}{match.Groups[2].Value}{MaskValue(match.Groups[3].Value, visiblePrefixLength)}",
                MaskRuleIds.BearerToken when match.Groups.Count >= 3 =>
                    $"bearer{match.Groups[1].Value}{MaskValue(match.Groups[2].Value, visiblePrefixLength)}",
                _ => MaskValue(match.Value, visiblePrefixLength)
            };
            yield return new MaskReplacement(match.Index, match.Length, masked);
        }
    }

    private static string MaskValue(string value, int visiblePrefixLength)
    {
        var indexes = StringInfo.ParseCombiningCharacters(value);
        var visibleTextElements = Math.Min(Math.Max(visiblePrefixLength, 0), indexes.Length);
        var visibleEnd = visibleTextElements == 0
            ? 0
            : visibleTextElements >= indexes.Length ? value.Length : indexes[visibleTextElements];
        var visible = value[..visibleEnd];
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

    private static int GetTextElementBoundaryAtOrBefore(string text, int maxLength)
    {
        if (maxLength >= text.Length)
        {
            return text.Length;
        }

        if (maxLength <= 0)
        {
            return 0;
        }

        var indexes = StringInfo.ParseCombiningCharacters(text);
        var boundary = 0;
        foreach (var index in indexes)
        {
            if (index > maxLength)
            {
                break;
            }

            boundary = index;
        }

        return boundary == maxLength ? maxLength : boundary;
    }

    private static IEnumerable<MaskReplacement> CreateCustomReplacements(string text, IEnumerable<string>? customPatterns, int visiblePrefixLength)
    {
        foreach (var pattern in ValidateCustomPatterns(customPatterns))
        {
            if (TryGetRegex(pattern, out var regex))
            {
                foreach (Match match in regex.Matches(text))
                {
                    yield return new MaskReplacement(match.Index, match.Length, MaskValue(match.Value, visiblePrefixLength));
                }
            }
        }
    }

    private static string ApplyReplacements(string text, IEnumerable<MaskReplacement> replacements)
    {
        var ordered = replacements
            .Where(replacement => replacement.Length > 0)
            .OrderBy(replacement => replacement.Start)
            .ThenByDescending(replacement => replacement.Length)
            .ToArray();
        if (ordered.Length == 0)
        {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        var cursor = 0;
        foreach (var replacement in ordered)
        {
            if (replacement.Start < cursor)
            {
                continue;
            }

            result.Append(text, cursor, replacement.Start - cursor);
            result.Append(replacement.Text);
            cursor = replacement.Start + replacement.Length;
        }

        result.Append(text, cursor, text.Length - cursor);
        return result.ToString();
    }

    private static MaskRuleMatch[] DistinctMatches(IEnumerable<MaskRuleMatch> matches)
    {
        return matches
            .DistinctBy(match => (match.RuleId, match.Pattern))
            .ToArray();
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
            // Cache compiled regexes because history lists can re-evaluate the same rules
            // for many items during scrolling and search.
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

/// <summary>
/// Describes one rule that matched sensitive-looking preview text.
/// </summary>
public sealed record MaskRuleMatch(string RuleId, string NameKey, string Pattern, bool IsCustomPattern);

internal readonly record struct MaskReplacement(int Start, int Length, string Text);
