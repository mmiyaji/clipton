using System.Diagnostics;
using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SnippetCatalogTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Upsert_RejectsMissingName(string name)
    {
        var catalog = new SnippetCatalog();

        Assert.Throws<ArgumentException>(() => catalog.Upsert(new Snippet(name, "text")));
    }

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
    public void Upsert_TrimsNameAndNormalizesFolder()
    {
        var catalog = new SnippetCatalog();

        catalog.Upsert(new Snippet("  Reply  ", "Thanks", @" Work \ Templates "));

        var snippet = Assert.Single(catalog.Snippets);
        Assert.Equal("Reply", snippet.Name);
        Assert.Equal("Work/Templates", snippet.Folder);
    }

    [Fact]
    public void Upsert_NormalizesNullFolderToRoot()
    {
        var catalog = new SnippetCatalog();

        catalog.Upsert(new Snippet("Root", "Text", null!));

        var snippet = Assert.Single(catalog.Snippets);
        Assert.Equal(string.Empty, snippet.Folder);
        Assert.Equal("Root", snippet.DisplayName);
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
    public void Remove_ReturnsFalseWhenNameDoesNotExist()
    {
        var catalog = new SnippetCatalog();
        catalog.Upsert(new Snippet("Greeting", "Hello"));

        Assert.False(catalog.Remove("Missing"));
        Assert.Single(catalog.Snippets);
    }

    [Fact]
    public void Remove_DeletesSnippetByFolderAndName()
    {
        var catalog = new SnippetCatalog();
        catalog.Upsert(new Snippet("Greeting", "Hello", @"Work\Templates"));
        catalog.Upsert(new Snippet("Greeting", "Hi", "Private"));

        Assert.True(catalog.Remove("Work/Templates", "greeting"));
        Assert.Null(catalog.Find("Work/Templates", "Greeting"));
        Assert.Equal("Hi", catalog.Find("Private", "Greeting")?.Text);
        Assert.False(catalog.Remove("Work/Templates", "Greeting"));
    }

    [Fact]
    public void Remove_ByFolderAndNameKeepsDifferentNamesInSameFolder()
    {
        var catalog = new SnippetCatalog();
        catalog.Upsert(new Snippet("Greeting", "Hello", "Work"));
        catalog.Upsert(new Snippet("Signature", "Regards", "Work"));

        Assert.True(catalog.Remove("Work", "greeting"));

        var snippet = Assert.Single(catalog.Snippets);
        Assert.Equal("Signature", snippet.Name);
        Assert.Null(catalog.Find("Work", "Greeting"));
    }

    [Fact]
    public void Clear_RemovesSnippetsAndLookupIndexes()
    {
        var catalog = new SnippetCatalog();
        catalog.Upsert(new Snippet("Token", "secret-token", "Secrets"));
        catalog.Upsert(new Snippet("Greeting", "Hello", "Work"));

        catalog.Clear();

        Assert.Empty(catalog.Snippets);
        Assert.Null(catalog.Find("Secrets", "Token"));
        Assert.Null(catalog.FindByText("secret-token"));
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FindByText_ReturnsNullForMissingText(string? text)
    {
        var catalog = new SnippetCatalog();

        Assert.Null(catalog.FindByText(text));
    }

    [Fact]
    public void FindByText_KeepsFirstSnippetForDuplicateText()
    {
        var catalog = new SnippetCatalog();

        catalog.Upsert(new Snippet("First", "shared", "A"));
        catalog.Upsert(new Snippet("Second", "shared", "B"));

        Assert.Equal("A / First", catalog.FindByText("shared")?.DisplayName);
    }

    [Fact]
    public void FindByText_PromotesNextDuplicateWhenFirstTextChanges()
    {
        var catalog = new SnippetCatalog();

        catalog.Upsert(new Snippet("First", "shared", "A"));
        catalog.Upsert(new Snippet("Second", "shared", "B"));
        catalog.Upsert(new Snippet("First", "changed", "A"));

        Assert.Equal("B / Second", catalog.FindByText("shared")?.DisplayName);
        Assert.Equal("A / First", catalog.FindByText("changed")?.DisplayName);
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

    [Fact]
    public void Upsert_ReplacesLargeCatalogWithinBudget()
    {
        var catalog = new SnippetCatalog();
        const int count = 3_000;
        for (var i = 0; i < count; i++)
        {
            catalog.Upsert(new Snippet($"Name-{i}", $"Text-{i}", $"Folder-{i % 20}"));
        }

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            catalog.Upsert(new Snippet($"Name-{i}", $"Updated-{i}", $"Folder-{i % 20}"));
        }

        stopwatch.Stop();

        Assert.Equal($"Updated-{count - 1}", catalog.Find("Folder-19", $"Name-{count - 1}")?.Text);
        Assert.True(stopwatch.ElapsedMilliseconds < 250, $"Replacing {count} snippets took {stopwatch.ElapsedMilliseconds} ms.");
    }
}
