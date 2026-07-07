namespace Clipton.Core;

/// <summary>
/// Mutable in-memory catalog of snippets indexed by folder/name and exact text.
/// </summary>
/// <remarks>
/// Folder/name lookups are case-insensitive for user convenience, while text lookups are
/// ordinal so registered snippets can mask matching history items only when the stored
/// text is exactly the same. The catalog is not internally synchronized; the runtime owns
/// it from the UI thread and rebuilds all indexes together when mutations occur.
/// </remarks>
public sealed class SnippetCatalog
{
    private readonly List<Snippet> _snippets = new();
    private readonly Dictionary<string, Snippet> _snippetsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _snippetIndexesByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Snippet> _snippetsByText = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _snippetTextCounts = new(StringComparer.Ordinal);

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
        if (_snippetIndexesByKey.TryGetValue(key, out var index))
        {
            var existing = _snippets[index];
            _snippets[index] = normalized;
            _snippetsByKey[key] = normalized;
            UpdateTextIndexForReplacement(existing, normalized, index);
            return;
        }

        _snippetIndexesByKey[key] = _snippets.Count;
        _snippets.Add(normalized);
        _snippetsByKey[key] = normalized;
        AddTextIndex(normalized);
    }

    private void UpdateTextIndexForReplacement(Snippet existing, Snippet replacement, int replacementIndex)
    {
        if (string.Equals(existing.Text, replacement.Text, StringComparison.Ordinal))
        {
            if (_snippetsByText.TryGetValue(existing.Text, out var mapped) && ReferenceEquals(mapped, existing))
            {
                _snippetsByText[replacement.Text] = replacement;
            }

            return;
        }

        RemoveTextIndex(existing.Text, existing, replacementIndex);
        AddTextIndex(replacement);
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
        _snippetIndexesByKey.Clear();
        _snippetsByText.Clear();
        _snippetTextCounts.Clear();
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
        _snippetIndexesByKey.Clear();
        _snippetsByText.Clear();
        _snippetTextCounts.Clear();
        for (var index = 0; index < _snippets.Count; index++)
        {
            var snippet = _snippets[index];
            var key = CreateKey(snippet.Folder, snippet.Name);
            _snippetsByKey[key] = snippet;
            _snippetIndexesByKey[key] = index;
            AddTextIndex(snippet);
        }
    }

    private void AddTextIndex(Snippet snippet)
    {
        if (!_snippetTextCounts.TryAdd(snippet.Text, 1))
        {
            _snippetTextCounts[snippet.Text]++;
        }

        _snippetsByText.TryAdd(snippet.Text, snippet);
    }

    private void RemoveTextIndex(string text, Snippet existing, int replacementIndex)
    {
        if (!_snippetTextCounts.TryGetValue(text, out var count))
        {
            return;
        }

        if (count <= 1)
        {
            _snippetTextCounts.Remove(text);
            _snippetsByText.Remove(text);
            return;
        }

        _snippetTextCounts[text] = count - 1;
        if (!_snippetsByText.TryGetValue(text, out var mapped) || !ReferenceEquals(mapped, existing))
        {
            return;
        }

        for (var index = 0; index < _snippets.Count; index++)
        {
            if (index == replacementIndex)
            {
                continue;
            }

            var candidate = _snippets[index];
            if (string.Equals(candidate.Text, text, StringComparison.Ordinal))
            {
                _snippetsByText[text] = candidate;
                return;
            }
        }

        _snippetsByText.Remove(text);
    }
}
