using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SnippetCatalogTests
{
    [Fact]
    public void Upsert_ReplacesSnippetByNameIgnoringCase()
    {
        var catalog = new SnippetCatalog();

        catalog.Upsert(new Snippet("Email", "a@example.com"));
        catalog.Upsert(new Snippet("email", "b@example.com"));

        var snippet = Assert.Single(catalog.Snippets);
        Assert.Equal("email", snippet.Name);
        Assert.Equal("b@example.com", snippet.Text);
    }

    [Fact]
    public void Remove_DeletesSnippetByNameIgnoringCase()
    {
        var catalog = new SnippetCatalog();
        catalog.Upsert(new Snippet("Greeting", "Hello"));

        Assert.True(catalog.Remove("greeting"));
        Assert.Empty(catalog.Snippets);
    }
}
