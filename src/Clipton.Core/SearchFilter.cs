namespace Clipton.Core;

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

    public string Query { get; }

    public string? Type { get; }

    public bool? Pinned { get; }

    public bool? HasUrl { get; }

    public DateTimeOffset? After { get; }

    public DateTimeOffset? Before { get; }

    public bool IsEmpty => _terms.Length == 0 && Type is null && Pinned is null && HasUrl is null && After is null && Before is null;

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
                    before = ParseDate(value)?.AddDays(1).AddTicks(-1);
                    break;
                default:
                    terms.Add(token);
                    break;
            }
        }

        return new SearchFilter(query?.Trim() ?? string.Empty, terms.ToArray(), type, pinned, hasUrl, after, before);
    }

    public bool MatchesText(Func<string?> textFactory)
    {
        if (_terms.Length == 0)
        {
            return true;
        }

        var text = textFactory() ?? string.Empty;
        return _terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public bool MatchesDate(DateTimeOffset capturedAt)
    {
        return (After is null || capturedAt >= After)
            && (Before is null || capturedAt <= Before);
    }

    public bool MatchesPinned(bool isPinned) => Pinned is null || Pinned == isPinned;

    public bool MatchesUrl(bool hasUrl) => HasUrl is null || HasUrl == hasUrl;

    public bool MatchesType(IEnumerable<ClipboardFormatKind> formats)
    {
        if (Type is null)
        {
            return true;
        }

        var values = formats.ToArray();
        return Type switch
        {
            "text" => values.Contains(ClipboardFormatKind.Text),
            "rich" => values.Contains(ClipboardFormatKind.RichText),
            "rtf" => values.Contains(ClipboardFormatKind.RichText),
            "html" => values.Contains(ClipboardFormatKind.Html),
            "image" => values.Contains(ClipboardFormatKind.Image),
            "file" => values.Contains(ClipboardFormatKind.FileDrop),
            "files" => values.Contains(ClipboardFormatKind.FileDrop),
            _ => true
        };
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
