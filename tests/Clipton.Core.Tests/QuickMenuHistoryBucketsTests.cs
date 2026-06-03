namespace Clipton.Core.Tests;

public sealed class QuickMenuHistoryBucketsTests
{
    [Fact]
    public void CreateTopLevelRanges_UsesConfiguredDirectItemCountWithoutDuplication()
    {
        var ranges = QuickMenuHistoryBuckets.CreateTopLevelRanges(historyCount: 1000, topLevelCount: 5);

        Assert.Collection(
            ranges,
            range => AssertRange(range, "6-50", 6, 50, 45, isNestedParent: false),
            range => AssertRange(range, "51-100", 51, 100, 50, isNestedParent: false),
            range => AssertRange(range, "101+", 101, 1000, 900, isNestedParent: true));
    }

    [Fact]
    public void CreateTopLevelRanges_StartsFirstFolderAfterConfiguredDirectItems()
    {
        var ranges = QuickMenuHistoryBuckets.CreateTopLevelRanges(historyCount: 1000, topLevelCount: 20);

        Assert.Collection(
            ranges,
            range => AssertRange(range, "21-50", 21, 50, 30, isNestedParent: false),
            range => AssertRange(range, "51-100", 51, 100, 50, isNestedParent: false),
            range => AssertRange(range, "101+", 101, 1000, 900, isNestedParent: true));
    }

    [Fact]
    public void CreateTopLevelRanges_ClampsPartialRanges()
    {
        var ranges = QuickMenuHistoryBuckets.CreateTopLevelRanges(historyCount: 37, topLevelCount: 5);

        var range = Assert.Single(ranges);
        AssertRange(range, "6-37", 6, 37, 32, isNestedParent: false);
    }

    [Fact]
    public void CreateNestedRanges_SplitsOlderHistoryIntoFiftyItemFolders()
    {
        var ranges = QuickMenuHistoryBuckets.CreateNestedRanges(historyCount: 1000);

        Assert.Equal("101-150", ranges[0].Label);
        Assert.Equal("151-200", ranges[1].Label);
        Assert.Equal("951-1000", ranges[^1].Label);
        Assert.All(ranges, range => Assert.InRange(range.Count, 1, 50));
        Assert.Equal(18, ranges.Count);
    }

    [Fact]
    public void NormalizeTopLevelHistoryItems_FallsBackToFive()
    {
        Assert.Equal(5, QuickMenuHistoryBuckets.NormalizeTopLevelHistoryItems(12));
        Assert.Equal(30, QuickMenuHistoryBuckets.NormalizeTopLevelHistoryItems(30));
    }

    private static void AssertRange(
        QuickMenuHistoryRange range,
        string label,
        int start,
        int end,
        int count,
        bool isNestedParent)
    {
        Assert.Equal(label, range.Label);
        Assert.Equal(start, range.Start);
        Assert.Equal(end, range.End);
        Assert.Equal(count, range.Count);
        Assert.Equal(start - 1, range.Offset);
        Assert.Equal(isNestedParent, range.IsNestedParent);
    }
}
