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

    [Fact]
    public void Find_NormalizesFolderSeparators()
    {
        var catalog = new SnippetCatalog();
        catalog.Upsert(new Snippet("Reply", "Thanks", @"Work\Templates"));

        var snippet = catalog.Find("Work/Templates", "reply");

        Assert.NotNull(snippet);
        Assert.Equal("Work/Templates", snippet.Folder);
    }

    [Fact]
    public void Upsert_RebuildsTextIndexWhenSnippetTextChanges()
    {
        var catalog = new SnippetCatalog();

        catalog.Upsert(new Snippet("Token", "old-secret", "Secrets"));
        catalog.Upsert(new Snippet("Token", "new-secret", "Secrets"));

        Assert.Null(catalog.FindByText("old-secret"));
        Assert.Equal("new-secret", catalog.FindByText("new-secret")?.Text);
    }

    [Fact]
    public void FindAndFindByText_HandleLargeCatalog()
    {
        var catalog = new SnippetCatalog();
        for (var i = 0; i < 2_000; i++)
        {
            catalog.Upsert(new Snippet($"Name-{i}", $"Text-{i}", $"Folder-{i % 20}"));
        }

        Assert.Equal("Text-1999", catalog.Find("Folder-19", "name-1999")?.Text);
        Assert.Equal("Folder-19 / Name-1999", catalog.FindByText("Text-1999")?.DisplayName);
    }
}
