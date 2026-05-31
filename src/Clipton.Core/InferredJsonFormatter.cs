using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clipton.Core;

public static class InferredJsonFormatter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Format(string text)
    {
        if (TryFormatExistingJson(text) is { } existingJson)
        {
            return existingJson;
        }

        if (TryFormatKeyValueLines(text) is { } objectJson)
        {
            return objectJson;
        }

        return JsonSerializer.Serialize(text);
    }

    private static string? TryFormatExistingJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryFormatKeyValueLines(string text)
    {
        var lines = text.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        var root = new JsonObject();
        foreach (var line in lines)
        {
            var commaCount = line.Count(character => character == ',');
            var tabCount = line.Count(character => character == '\t');
            if (commaCount + tabCount == 0)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return null;
                }

                root[line] = JsonValue.Create(string.Empty);
                continue;
            }

            if (commaCount + tabCount != 1)
            {
                return null;
            }

            var separator = commaCount == 1 ? ',' : '\t';
            var parts = line.Split(separator, 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                return null;
            }

            root[parts[0]] = InferValue(parts[1]);
        }

        return root.ToJsonString(Options);
    }

    private static JsonNode? InferValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return JsonValue.Create(string.Empty);
        }

        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return JsonValue.Create(value);
        }
    }
}
