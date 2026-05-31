using System.Globalization;
using System.Text.RegularExpressions;

namespace Clipton.Core;

public static class SnippetTemplateRenderer
{
    private static readonly Regex TokenPattern = new(@"\{\{\s*(?<name>[a-zA-Z][a-zA-Z0-9_]*)(?:\s*:\s*(?<format>[^}]*?)|\(\s*(?<formatParen>.*?)\s*\))?\s*\}\}", RegexOptions.Compiled);

    public static string Render(string template, DateTimeOffset? now = null)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var timestamp = now ?? DateTimeOffset.Now;
        return TokenPattern.Replace(template, match =>
        {
            var name = match.Groups["name"].Value.ToLowerInvariant();
            var rawFormat = match.Groups["format"].Success
                ? match.Groups["format"].Value
                : match.Groups["formatParen"].Success ? match.Groups["formatParen"].Value : string.Empty;
            var format = NormalizeFormat(rawFormat);
            return name switch
            {
                "date" => Format(timestamp, format, "yyyy-MM-dd"),
                "time" => Format(timestamp, format, "HH:mm"),
                "datetime" or "now" => Format(timestamp, format, "yyyy-MM-dd HH:mm"),
                "isodate" => timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "isodatetime" => timestamp.ToString("O", CultureInfo.InvariantCulture),
                "year" => timestamp.ToString("yyyy", CultureInfo.InvariantCulture),
                "month" => timestamp.ToString("MM", CultureInfo.InvariantCulture),
                "day" => timestamp.ToString("dd", CultureInfo.InvariantCulture),
                "weekday" => timestamp.ToString(string.IsNullOrWhiteSpace(format) ? "dddd" : format, CultureInfo.CurrentCulture),
                "unix" => timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                "guid" or "uuid" => Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                "br" or "newline" => Environment.NewLine,
                _ => match.Value
            };
        });
    }

    private static string Format(DateTimeOffset timestamp, string format, string fallback)
    {
        return timestamp.ToString(string.IsNullOrWhiteSpace(format) ? fallback : format, CultureInfo.CurrentCulture);
    }

    private static string NormalizeFormat(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }
}
