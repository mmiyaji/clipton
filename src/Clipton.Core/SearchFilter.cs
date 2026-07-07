namespace Clipton.Core;

/// <summary>
/// Parsed clipboard-history search query.
/// </summary>
/// <remarks>
/// The filter supports free-text terms plus small key/value operators such as
/// <c>type:image</c>, <c>pinned:true</c>, <c>url:false</c>, <c>after:2026-06-01</c>
/// and <c>before:2026-06-30</c>. Unknown operators are treated as text terms so
/// older app versions and user typos degrade to normal search instead of hiding results.
/// </remarks>
public sealed class SearchFilter
{
    private readonly string[] _terms;

    private SearchFilter(
        string query,
        string[] terms,
        string? type,
        bool? pinned,
        bool? hasUrl,
        DateTimeOffset? after,
        DateTimeOffset? before)
    {
        Query = query;
        _terms = terms;
        Type = type;
        Pinned = pinned;
        HasUrl = hasUrl;
        After = after;
        Before = before;
    }

    /// <summary>Original query text after outer whitespace is trimmed.</summary>
    public string Query { get; }

    /// <summary>Normalized format/type operator, when present.</summary>
    public string? Type { get; }

    /// <summary>Optional pinned-state operator.</summary>
    public bool? Pinned { get; }

    /// <summary>Optional URL-presence operator.</summary>
    public bool? HasUrl { get; }

    /// <summary>Inclusive lower timestamp bound.</summary>
    public DateTimeOffset? After { get; }

    /// <summary>Inclusive upper timestamp bound.</summary>
    public DateTimeOffset? Before { get; }

    /// <summary>True when the query has no text terms and no active operators.</summary>
    public bool IsEmpty => _terms.Length == 0 && Type is null && Pinned is null && HasUrl is null && After is null && Before is null;

    /// <summary>
    /// Parses user-entered search text into a reusable filter object.
    /// </summary>
    public static SearchFilter Parse(string? query)
    {
        var tokens = Tokenize(query ?? string.Empty);
        var terms = new List<string>();
        string? type = null;
        bool? pinned = null;
        bool? hasUrl = null;
        DateTimeOffset? after = null;
        DateTimeOffset? before = null;

        foreach (var token in tokens)
        {
            var separator = token.IndexOf(':');
            if (separator <= 0)
            {
                terms.Add(token);
                continue;
            }

            var key = token[..separator].ToLowerInvariant();
            var value = token[(separator + 1)..].Trim();
            switch (key)
            {
                case "type":
                case "format":
                    type = NormalizeType(value);
                    break;
                case "pinned":
                case "pin":
                    pinned = ParseBool(value);
                    break;
                case "url":
                case "hasurl":
                    hasUrl = ParseBool(value);
                    break;
                case "after":
                case "from":
                    after = ParseDate(value);
                    break;
                case "before":
                case "to":
                    // "before" is a user-facing inclusive upper bound. Most users enter
                    // dates, so keep the whole day instead of stopping at midnight.
                    if (TryParseDate(value, out var parsedBefore, out var dateOnlyBefore))
                    {
                        before = dateOnlyBefore ? parsedBefore.AddDays(1).AddTicks(-1) : parsedBefore;
                    }

                    break;
                default:
                    AddUnknownOperatorTerms(terms, key, value);
                    break;
            }
        }

        return new SearchFilter(query?.Trim() ?? string.Empty, terms.ToArray(), type, pinned, hasUrl, after, before);
    }

    private static void AddUnknownOperatorTerms(List<string> terms, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            terms.Add(key);
        }

        foreach (var term in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            terms.Add(term);
        }
    }

    /// <summary>
    /// Tests free-text terms against lazily produced searchable text.
    /// </summary>
    /// <remarks>
    /// The factory avoids building expensive rich previews when the query has no text terms.
    /// </remarks>
    public bool MatchesText(Func<string?> textFactory)
    {
        if (_terms.Length == 0)
        {
            return true;
        }

        var text = textFactory() ?? string.Empty;
        return _terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tests the captured timestamp against parsed bounds.</summary>
    public bool MatchesDate(DateTimeOffset capturedAt)
    {
        return (After is null || capturedAt >= After)
            && (Before is null || capturedAt <= Before);
    }

    /// <summary>Tests an item's pinned state against the optional pinned operator.</summary>
    public bool MatchesPinned(bool isPinned) => Pinned is null || Pinned == isPinned;

    /// <summary>Tests URL presence against the optional URL operator.</summary>
    public bool MatchesUrl(bool hasUrl) => HasUrl is null || HasUrl == hasUrl;

    /// <summary>Tests clipboard formats against the optional type/format operator.</summary>
    public bool MatchesType(IEnumerable<ClipboardFormatKind> formats)
    {
        if (Type is null)
        {
            return true;
        }

        return Type switch
        {
            "text" => ContainsFormat(formats, ClipboardFormatKind.Text),
            "rich" => ContainsFormat(formats, ClipboardFormatKind.RichText),
            "rtf" => ContainsFormat(formats, ClipboardFormatKind.RichText),
            "html" => ContainsFormat(formats, ClipboardFormatKind.Html),
            "image" => ContainsFormat(formats, ClipboardFormatKind.Image),
            "file" => ContainsFormat(formats, ClipboardFormatKind.FileDrop),
            "files" => ContainsFormat(formats, ClipboardFormatKind.FileDrop),
            _ => true
        };
    }

    private static bool ContainsFormat(IEnumerable<ClipboardFormatKind> formats, ClipboardFormatKind expected)
    {
        foreach (var format in formats)
        {
            if (format == expected)
            {
                return true;
            }
        }

        return false;
    }

    private static string? NormalizeType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool? ParseBool(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => null
        };
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static bool TryParseDate(string value, out DateTimeOffset parsed, out bool dateOnly)
    {
        dateOnly = !value.Any(char.IsWhiteSpace) && value.All(ch => ch != 'T' && ch != ':');
        return DateTimeOffset.TryParse(value, out parsed);
    }

    private static string[] Tokenize(string query)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var quoted = false;
        foreach (var ch in query)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !quoted)
            {
                AddCurrent();
                continue;
            }

            current.Add(ch);
        }

        AddCurrent();
        return tokens.ToArray();

        void AddCurrent()
        {
            if (current.Count == 0)
            {
                return;
            }

            var token = new string(current.ToArray()).Trim();
            if (token.Length > 0)
            {
                tokens.Add(token);
            }

            current.Clear();
        }
    }
}
