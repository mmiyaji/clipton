namespace Clipton.Core;

public sealed class SnippetCatalog
{
    private readonly List<Snippet> _snippets = new();
    private readonly Dictionary<string, Snippet> _snippetsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Snippet> _snippetsByText = new(StringComparer.Ordinal);

    public IReadOnlyList<Snippet> Snippets => _snippets;

    public void Upsert(Snippet snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet.Name))
        {
            throw new ArgumentException("Snippet name is required.", nameof(snippet));
        }

        var normalized = Normalize(snippet);
        var key = CreateKey(normalized.Folder, normalized.Name);
        var index = _snippetsByKey.TryGetValue(key, out var existing)
            ? _snippets.IndexOf(existing)
            : -1;
        if (index >= 0)
        {
            _snippets[index] = normalized;
            RebuildIndexes();
            return;
        }

        _snippets.Add(normalized);
        _snippetsByKey[key] = normalized;
        if (!_snippetsByText.ContainsKey(normalized.Text))
        {
            _snippetsByText[normalized.Text] = normalized;
        }
    }

    public bool Remove(string name)
    {
        var removed = _snippets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            RebuildIndexes();
        }

        return removed;
    }

    public bool Remove(string folder, string name)
    {
        var removed = _snippets.RemoveAll(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeFolder(item.Folder), NormalizeFolder(folder), StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            RebuildIndexes();
        }

        return removed;
    }

    public Snippet? Find(string folder, string name) => _snippetsByKey.GetValueOrDefault(CreateKey(NormalizeFolder(folder), name.Trim()));

    public Snippet? FindByText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return _snippetsByText.GetValueOrDefault(text);
    }

    private static Snippet Normalize(Snippet snippet)
    {
        return snippet with
        {
            Name = snippet.Name.Trim(),
            Folder = NormalizeFolder(snippet.Folder)
        };
    }

    private static string NormalizeFolder(string? folder)
    {
        return string.Join(
            "/",
            (folder ?? string.Empty)
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string CreateKey(string folder, string name) => $"{folder}\u001F{name}";

    private void RebuildIndexes()
    {
        _snippetsByKey.Clear();
        _snippetsByText.Clear();
        foreach (var snippet in _snippets)
        {
            _snippetsByKey[CreateKey(snippet.Folder, snippet.Name)] = snippet;
            if (!_snippetsByText.ContainsKey(snippet.Text))
            {
                _snippetsByText[snippet.Text] = snippet;
            }
        }
    }
}
