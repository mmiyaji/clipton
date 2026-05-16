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

        var index = _snippets.FindIndex(item => string.Equals(item.Name, snippet.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _snippets[index] = snippet;
            return;
        }

        _snippets.Add(snippet);
    }

    public bool Remove(string name) => _snippets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
}
