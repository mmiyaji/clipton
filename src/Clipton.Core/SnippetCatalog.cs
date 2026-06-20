namespace Clipton.Core;

/// <summary>
/// Mutable in-memory catalog of snippets indexed by folder/name and exact text.
/// </summary>
/// <remarks>
/// Folder/name lookups are case-insensitive for user convenience, while text lookups are
/// ordinal so registered snippets can mask matching history items only when the stored
/// text is exactly the same.
/// </remarks>
public sealed class SnippetCatalog
{
    private readonly List<Snippet> _snippets = new();
    private readonly Dictionary<string, Snippet> _snippetsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Snippet> _snippetsByText = new(StringComparer.Ordinal);

    /// <summary>Snippets in display order.</summary>
    public IReadOnlyList<Snippet> Snippets => _snippets;

    /// <summary>
    /// Inserts or replaces a snippet identified by normalized folder and name.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the snippet name is empty.</exception>
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

    /// <summary>
    /// Removes snippets with the supplied name from any folder.
    /// </summary>
    public bool Remove(string name)
    {
        var removed = _snippets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            RebuildIndexes();
        }

        return removed;
    }

    /// <summary>
    /// Removes a snippet by normalized folder and name.
    /// </summary>
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

    /// <summary>Removes every snippet and clears lookup indexes.</summary>
    public void Clear()
    {
        _snippets.Clear();
        _snippetsByKey.Clear();
        _snippetsByText.Clear();
    }

    /// <summary>Finds a snippet by normalized folder and case-insensitive name.</summary>
    public Snippet? Find(string folder, string name) => _snippetsByKey.GetValueOrDefault(CreateKey(NormalizeFolder(folder), name.Trim()));

    /// <summary>Finds the first snippet whose body exactly matches the supplied text.</summary>
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

    // Unit separator keeps folder/name keys reversible even when user text contains slashes.
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
