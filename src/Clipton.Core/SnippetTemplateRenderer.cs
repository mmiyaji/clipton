using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Clipton.Core;

public static class SnippetTemplateRenderer
{
    private static readonly Regex TokenPattern = new(@"\{\{\s*(?<name>[a-zA-Z][a-zA-Z0-9_]*)(?:\s*:\s*(?<format>[^}]*?)|\(\s*(?<formatParen>.*?)\s*\))?\s*\}\}", RegexOptions.Compiled);

    public static string Render(string template, DateTimeOffset? now = null, IReadOnlyList<string>? filePaths = null)
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
            var format = IsFileVariable(name) ? NormalizeQuotedValue(rawFormat) : NormalizeFormat(rawFormat);
            return name switch
            {
                "date" => Format(timestamp, format, "yyyy-MM-dd"),
                "time" => Format(timestamp, format, "HH:mm"),
                "datetime" or "now" => Format(timestamp, format, "yyyy-MM-dd HH:mm"),
                "utcdate" => Format(timestamp.ToUniversalTime(), format, "yyyy-MM-dd"),
                "utctime" => Format(timestamp.ToUniversalTime(), format, "HH:mm"),
                "utcdatetime" or "utcnow" => Format(timestamp.ToUniversalTime(), format, "yyyy-MM-dd HH:mm"),
                "isodate" => timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "isodatetime" => timestamp.ToString("O", CultureInfo.InvariantCulture),
                "isoutc" => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                "year" => timestamp.ToString("yyyy", CultureInfo.InvariantCulture),
                "month" => timestamp.ToString("MM", CultureInfo.InvariantCulture),
                "day" => timestamp.ToString("dd", CultureInfo.InvariantCulture),
                "tomorrow" => Format(timestamp.AddDays(1), format, "yyyy-MM-dd"),
                "yesterday" => Format(timestamp.AddDays(-1), format, "yyyy-MM-dd"),
                "adddays" => FormatOffset(format, "yyyy-MM-dd", value => timestamp.AddDays(value)) ?? match.Value,
                "addmonths" => FormatOffset(format, "yyyy-MM-dd", value => timestamp.AddMonths((int)value)) ?? match.Value,
                "addyears" => FormatOffset(format, "yyyy-MM-dd", value => timestamp.AddYears((int)value)) ?? match.Value,
                "addhours" => FormatOffset(format, "yyyy-MM-dd HH:mm", value => timestamp.AddHours(value)) ?? match.Value,
                "addminutes" => FormatOffset(format, "yyyy-MM-dd HH:mm", value => timestamp.AddMinutes(value)) ?? match.Value,
                "quarter" => (((timestamp.Month - 1) / 3) + 1).ToString(CultureInfo.InvariantCulture),
                "week" or "isoweek" => ISOWeek.GetWeekOfYear(timestamp.Date).ToString("00", CultureInfo.InvariantCulture),
                "weekday" => timestamp.ToString(string.IsNullOrWhiteSpace(format) ? "dddd" : format, CultureInfo.CurrentCulture),
                "unix" => timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                "unixms" => timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                "timezone" => timestamp.ToString("zzz", CultureInfo.InvariantCulture),
                "guid" or "uuid" => Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                "shortuuid" => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8],
                "random" or "randomhex" => RandomHex(ParseLength(format, 8, 1, 64)),
                "randomnumber" => RandomNumber(ParseLength(format, 6, 1, 9)),
                "filepath" or "filepaths" => JoinFiles(filePaths, FileVariableKind.FullPath, format),
                "filename" or "filenames" => JoinFiles(filePaths, FileVariableKind.Name, format),
                "filenamewithoutextension" or "filestem" or "filestems" => JoinFiles(filePaths, FileVariableKind.NameWithoutExtension, format),
                "fileextension" or "fileextensions" => JoinFiles(filePaths, FileVariableKind.Extension, format),
                "filedirectory" or "filedirectories" => JoinFiles(filePaths, FileVariableKind.Directory, format),
                "filecount" => (filePaths?.Count ?? 0).ToString(CultureInfo.InvariantCulture),
                "br" or "newline" => Environment.NewLine,
                _ => match.Value
            };
        });
    }

    private static string Format(DateTimeOffset timestamp, string format, string fallback)
    {
        return timestamp.ToString(string.IsNullOrWhiteSpace(format) ? fallback : format, CultureInfo.CurrentCulture);
    }

    private static string? FormatOffset(string value, string fallbackFormat, Func<double, DateTimeOffset> add)
    {
        if (!TryParseOffset(value, out var offset, out var format))
        {
            return null;
        }

        return Format(add(offset), format, fallbackFormat);
    }

    private static string NormalizeFormat(string value)
    {
        return NormalizeQuotedValue(value.Trim());
    }

    private static string NormalizeQuotedValue(string value)
    {
        var trimmed = value;
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private static int ParseLength(string value, int fallback, int min, int max)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static bool TryParseOffset(string value, out double offset, out string format)
    {
        var parts = value.Split('|', 2, StringSplitOptions.TrimEntries);
        format = parts.Length == 2 ? NormalizeFormat(parts[1]) : string.Empty;
        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
    }

    private static string RandomHex(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes((length + 1) / 2);
        return Convert.ToHexString(bytes).ToLowerInvariant()[..length];
    }

    private static string RandomNumber(int digits)
    {
        var chars = new char[digits];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return new string(chars);
    }

    private static string JoinFiles(IReadOnlyList<string>? filePaths, FileVariableKind kind, string separator)
    {
        if (filePaths is not { Count: > 0 })
        {
            return string.Empty;
        }

        var actualSeparator = string.IsNullOrEmpty(separator)
            ? Environment.NewLine
            : separator
                .Replace(@"\n", Environment.NewLine, StringComparison.Ordinal)
                .Replace(@"\t", "\t", StringComparison.Ordinal);
        var values = filePaths.Select(path => FormatFileValue(path, kind)).Where(value => !string.IsNullOrEmpty(value));
        return string.Join(actualSeparator, values);
    }

    private static bool IsFileVariable(string name)
    {
        return name is "filepath" or "filepaths"
            or "filename" or "filenames"
            or "filenamewithoutextension" or "filestem" or "filestems"
            or "fileextension" or "fileextensions"
            or "filedirectory" or "filedirectories";
    }

    private static string FormatFileValue(string path, FileVariableKind kind)
    {
        return kind switch
        {
            FileVariableKind.FullPath => path,
            FileVariableKind.Name => Path.GetFileName(path),
            FileVariableKind.NameWithoutExtension => Path.GetFileNameWithoutExtension(path),
            FileVariableKind.Extension => Path.GetExtension(path),
            FileVariableKind.Directory => Path.GetDirectoryName(path) ?? string.Empty,
            _ => path
        };
    }

    private enum FileVariableKind
    {
        FullPath,
        Name,
        NameWithoutExtension,
        Extension,
        Directory
    }
}
