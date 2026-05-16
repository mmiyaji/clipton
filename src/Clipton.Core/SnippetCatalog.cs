namespace Clipton.Core;

public sealed class SnippetCatalog
{
    private readonly List<Snippet> _snippets = new();

    public IReadOnlyList<Snippet> Snippets => _snippets;

    public void Upsert(Snippet snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet.Name))
        {
            throw new ArgumentException("Snippet name is required.", nameof(snippet));
        }

        var normalized = Normalize(snippet);
        var index = _snippets.FindIndex(item => HasSameKey(item, normalized));
        if (index >= 0)
        {
            _snippets[index] = normalized;
            return;
        }

        _snippets.Add(normalized);
    }

    public bool Remove(string name) => _snippets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

    public bool Remove(string folder, string name) => _snippets.RemoveAll(item =>
        string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(NormalizeFolder(item.Folder), NormalizeFolder(folder), StringComparison.OrdinalIgnoreCase)) > 0;

    public Snippet? Find(string folder, string name) => _snippets.FirstOrDefault(item =>
        string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(NormalizeFolder(item.Folder), NormalizeFolder(folder), StringComparison.OrdinalIgnoreCase));

    public Snippet? FindByText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return _snippets.FirstOrDefault(item => string.Equals(item.Text, text, StringComparison.Ordinal));
    }

    private static bool HasSameKey(Snippet left, Snippet right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Folder, right.Folder, StringComparison.OrdinalIgnoreCase);
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
}
