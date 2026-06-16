using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class ClipboardHistoryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveCapacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClipboardHistory(capacity));
    }

    [Fact]
    public void Constructor_UsesDefaultCapacity()
    {
        var history = new ClipboardHistory();

        Assert.Equal(30, history.Capacity);
        Assert.Empty(history.Items);
    }

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
    public void Add_AllowsPreviouslySeenFingerprintAfterClear()
    {
        var history = new ClipboardHistory(capacity: 10);

        Assert.True(history.Add(TextSnapshot("1", "same")));
        history.Clear();

        Assert.True(history.Add(TextSnapshot("2", "same")));
        Assert.Equal(["2"], history.Items.Select(item => item.Id));
        Assert.Null(history.Find("1"));
    }

    [Fact]
    public void Add_AllowsPreviouslySeenFingerprintAfterRemovingOnlySnapshot()
    {
        var history = new ClipboardHistory(capacity: 10);

        Assert.True(history.Add(TextSnapshot("1", "same")));
        Assert.True(history.Remove("1"));

        Assert.True(history.Add(TextSnapshot("2", "same")));
        Assert.Equal(["2"], history.Items.Select(item => item.Id));
    }

    [Fact]
    public void Add_AllowsRemovedFingerprintWhenAnotherSnapshotRemains()
    {
        var history = new ClipboardHistory(capacity: 10);

        history.Add(TextSnapshot("1", "alpha"));
        history.Add(TextSnapshot("2", "beta"));
        Assert.True(history.Remove("1"));

        Assert.True(history.Add(TextSnapshot("3", "alpha")));
        Assert.Equal(["3", "2"], history.Items.Select(item => item.Id));
        Assert.Null(history.Find("1"));
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
        Assert.Null(history.Find("1"));
    }

    [Fact]
    public void Remove_ReturnsFalseForUnknownId()
    {
        var history = new ClipboardHistory(capacity: 10);

        history.Add(TextSnapshot("1", "one"));

        Assert.False(history.Remove("missing"));
        Assert.Equal(["1"], history.Items.Select(item => item.Id));
    }

    [Fact]
    public void Find_ReturnsItemAfterDuplicateMoveAndCapacityTrim()
    {
        var history = new ClipboardHistory(capacity: 2);

        history.Add(TextSnapshot("1", "alpha"));
        history.Add(TextSnapshot("2", "beta"));
        history.Add(TextSnapshot("3", "alpha"));
        history.Add(TextSnapshot("4", "gamma"));

        Assert.Null(history.Find("1"));
        Assert.Null(history.Find("2"));
        Assert.Equal("alpha", history.Find("3")?.Text);
        Assert.Equal("gamma", history.Find("4")?.Text);
        Assert.Equal(["4", "3"], history.Items.Select(item => item.Id));
    }

    [Fact]
    public void Add_HandlesLargeHistoryWithoutRepeatedFingerprintScanning()
    {
        var history = new ClipboardHistory(capacity: 2_000);

        for (var i = 0; i < 2_000; i++)
        {
            Assert.True(history.Add(TextSnapshot(i.ToString(), $"value-{i}")));
        }

        Assert.False(history.Add(TextSnapshot("duplicate", "value-1999")));
        Assert.Equal(2_000, history.Items.Count);
    }

    [Fact]
    public void AppendOlder_AddsToEndAndSkipsDuplicates()
    {
        var history = new ClipboardHistory(capacity: 10);

        history.Add(TextSnapshot("2", "newer"));
        Assert.True(history.AppendOlder(TextSnapshot("1", "older")));
        Assert.False(history.AppendOlder(TextSnapshot("duplicate", "older")));

        Assert.Equal(["2", "1"], history.Items.Select(item => item.Id));
    }

    [Fact]
    public void AppendOlder_TrimsAppendedItemWhenAtCapacity()
    {
        var history = new ClipboardHistory(capacity: 2);

        Assert.True(history.AppendOlder(TextSnapshot("1", "one")));
        Assert.True(history.AppendOlder(TextSnapshot("2", "two")));
        Assert.True(history.AppendOlder(TextSnapshot("3", "three")));

        Assert.Equal(["1", "2"], history.Items.Select(item => item.Id));
        Assert.Null(history.Find("3"));
    }

    [Fact]
    public void UnloadOlderBeyond_RejectsNegativeCount()
    {
        var history = new ClipboardHistory(capacity: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => history.UnloadOlderBeyond(-1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void UnloadOlderBeyond_ReturnsEmptyWhenCountKeepsAllItems(int count)
    {
        var history = new ClipboardHistory(capacity: 10);

        history.Add(TextSnapshot("1", "one"));
        history.Add(TextSnapshot("2", "two"));

        var removed = history.UnloadOlderBeyond(count);

        Assert.Empty(removed);
        Assert.Equal(["2", "1"], history.Items.Select(item => item.Id));
    }

    [Fact]
    public void UnloadOlderBeyond_RemovesOlderItemsWithoutBreakingLookups()
    {
        var history = new ClipboardHistory(capacity: 10);

        history.Add(TextSnapshot("1", "one"));
        history.Add(TextSnapshot("2", "two"));
        history.Add(TextSnapshot("3", "three"));

        var removed = history.UnloadOlderBeyond(2);

        Assert.Equal(["1"], removed.Select(item => item.Id));
        Assert.Equal(["3", "2"], history.Items.Select(item => item.Id));
        Assert.Null(history.Find("1"));
        Assert.Equal("two", history.Find("2")?.Text);
    }

    [Fact]
    public void UnloadOlderBeyond_ZeroRemovesAllAndResetsLastFingerprint()
    {
        var history = new ClipboardHistory(capacity: 10);

        history.Add(TextSnapshot("1", "same"));

        var removed = history.UnloadOlderBeyond(0);

        Assert.Equal(["1"], removed.Select(item => item.Id));
        Assert.Empty(history.Items);
        Assert.True(history.Add(TextSnapshot("2", "same")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetCapacity_RejectsNonPositiveCapacity(int capacity)
    {
        var history = new ClipboardHistory(capacity: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => history.SetCapacity(capacity));
        Assert.Equal(10, history.Capacity);
    }

    [Fact]
    public void SetCapacity_LeavesItemsWhenNewCapacityKeepsAllItems()
    {
        var history = new ClipboardHistory(capacity: 3);

        history.Add(TextSnapshot("1", "one"));
        history.Add(TextSnapshot("2", "two"));

        history.SetCapacity(2);

        Assert.Equal(2, history.Capacity);
        Assert.Equal(["2", "1"], history.Items.Select(item => item.Id));
    }

    [Fact]
    public void SetCapacity_TrimsAndUntracksOlderItems()
    {
        var history = new ClipboardHistory(capacity: 3);

        history.Add(TextSnapshot("1", "one"));
        history.Add(TextSnapshot("2", "two"));
        history.Add(TextSnapshot("3", "three"));

        history.SetCapacity(1);

        Assert.Equal(["3"], history.Items.Select(item => item.Id));
        Assert.Null(history.Find("1"));
        Assert.Null(history.Find("2"));
        Assert.Equal("three", history.Find("3")?.Text);
    }

    private static ClipboardSnapshot TextSnapshot(string id, string text)
    {
        return new ClipboardSnapshot(id, DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: text);
    }
}
