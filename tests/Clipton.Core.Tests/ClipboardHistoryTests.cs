using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class ClipboardHistoryTests
{
    [Fact]
    public void Add_DeduplicatesConsecutiveSnapshots()
    {
        var history = new ClipboardHistory(capacity: 10);
        var first = TextSnapshot("1", "same");
        var second = TextSnapshot("2", "same");

        Assert.True(history.Add(first));
        Assert.False(history.Add(second));

        Assert.Single(history.Items);
        Assert.Equal("1", history.Items[0].Id);
    }

    [Fact]
    public void Add_MovesExistingDuplicateToTop()
    {
        var history = new ClipboardHistory(capacity: 10);

        history.Add(TextSnapshot("1", "alpha"));
        history.Add(TextSnapshot("2", "beta"));
        history.Add(TextSnapshot("3", "alpha"));

        Assert.Equal(["3", "2"], history.Items.Select(item => item.Id));
    }

    [Fact]
    public void Add_TrimsToCapacity()
    {
        var history = new ClipboardHistory(capacity: 2);

        history.Add(TextSnapshot("1", "one"));
        history.Add(TextSnapshot("2", "two"));
        history.Add(TextSnapshot("3", "three"));

        Assert.Equal(["3", "2"], history.Items.Select(item => item.Id));
    }

    [Fact]
    public void Remove_DeletesSnapshotById()
    {
        var history = new ClipboardHistory(capacity: 10);

        history.Add(TextSnapshot("1", "one"));
        history.Add(TextSnapshot("2", "two"));

        Assert.True(history.Remove("1"));
        Assert.Equal(["2"], history.Items.Select(item => item.Id));
    }

    private static ClipboardSnapshot TextSnapshot(string id, string text)
    {
        return new ClipboardSnapshot(id, DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: text);
    }
}
