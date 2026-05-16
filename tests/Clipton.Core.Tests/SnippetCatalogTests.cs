using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SnippetCatalogTests
{
    [Fact]
    public void Upsert_ReplacesSnippetByNameIgnoringCase()
    {
        var catalog = new SnippetCatalog();

        catalog.Upsert(new Snippet("Email", "a@example.com", "Work"));
        catalog.Upsert(new Snippet("email", "b@example.com", "work"));

        var snippet = Assert.Single(catalog.Snippets);
        Assert.Equal("email", snippet.Name);
        Assert.Equal("b@example.com", snippet.Text);
        Assert.Equal("work", snippet.Folder);
    }

    [Fact]
    public void Upsert_AllowsSameNameInDifferentFolders()
    {
        var catalog = new SnippetCatalog();

        catalog.Upsert(new Snippet("Greeting", "Hello", "Work"));
        catalog.Upsert(new Snippet("Greeting", "Hi", "Private"));

        Assert.Equal(2, catalog.Snippets.Count);
    }

    [Fact]
    public void Remove_DeletesSnippetByNameIgnoringCase()
    {
        var catalog = new SnippetCatalog();
        catalog.Upsert(new Snippet("Greeting", "Hello"));

        Assert.True(catalog.Remove("greeting"));
        Assert.Empty(catalog.Snippets);
    }

    [Fact]
    public void FindByText_ReturnsRegisteredSnippet()
    {
        var catalog = new SnippetCatalog();
        catalog.Upsert(new Snippet("ApiKey", "secret-token", "Secrets"));

        var snippet = catalog.FindByText("secret-token");

        Assert.NotNull(snippet);
        Assert.Equal("Secrets / ApiKey", snippet.DisplayName);
    }
}
