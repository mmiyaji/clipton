namespace Clipton.Core;

/// <summary>
/// Builds stable range buckets for older quick-menu history items.
/// </summary>
/// <remarks>
/// The newest items stay directly visible. Older items are grouped into predictable
/// ranges so large histories remain navigable without materializing every item at the
/// top level of the menu.
/// </remarks>
public static class QuickMenuHistoryBuckets
{
    /// <summary>Number of history items represented by each non-top-level range.</summary>
    public const int BucketSize = 50;

    /// <summary>First 1-based history index represented by nested range folders.</summary>
    public const int NestedStart = 101;

    /// <summary>Allowed counts for direct top-level history items.</summary>
    public static readonly int[] TopLevelHistoryItemOptions = [5, 10, 20, 30, 50];

    /// <summary>Returns a supported top-level count or the product default.</summary>
    public static int NormalizeTopLevelHistoryItems(int count)
    {
        return TopLevelHistoryItemOptions.Contains(count) ? count : 5;
    }

    /// <summary>
    /// Creates the top-level range folders that follow the directly visible history items.
    /// </summary>
    public static IReadOnlyList<QuickMenuHistoryRange> CreateTopLevelRanges(int historyCount, int topLevelCount)
    {
        var normalizedTopLevelCount = NormalizeTopLevelHistoryItems(topLevelCount);
        var ranges = new List<QuickMenuHistoryRange>();

        AddRange(ranges, historyCount, normalizedTopLevelCount + 1, BucketSize);
        AddRange(ranges, historyCount, BucketSize + 1, BucketSize * 2);

        if (historyCount >= NestedStart)
        {
            ranges.Add(new QuickMenuHistoryRange(NestedStart, historyCount, true));
        }

        return ranges;
    }

    /// <summary>
    /// Creates child ranges shown when the nested "101+" parent is opened.
    /// </summary>
    public static IReadOnlyList<QuickMenuHistoryRange> CreateNestedRanges(int historyCount)
    {
        var ranges = new List<QuickMenuHistoryRange>();
        for (var rangeStart = NestedStart; rangeStart <= historyCount; rangeStart += BucketSize)
        {
            AddRange(ranges, historyCount, rangeStart, rangeStart + BucketSize - 1);
        }

        return ranges;
    }

    private static void AddRange(ICollection<QuickMenuHistoryRange> ranges, int historyCount, int start, int end)
    {
        var clampedEnd = Math.Min(end, historyCount);
        if (clampedEnd < start)
        {
            return;
        }

        ranges.Add(new QuickMenuHistoryRange(start, clampedEnd));
    }
}

/// <summary>
/// A 1-based inclusive history range shown as a quick-menu folder.
/// </summary>
public sealed record QuickMenuHistoryRange(int Start, int End, bool IsNestedParent = false)
{
    /// <summary>Number of items represented by the range.</summary>
    public int Count => End - Start + 1;

    /// <summary>Zero-based offset used when loading this range from persisted history.</summary>
    public int Offset => Start - 1;

    /// <summary>User-visible range label.</summary>
    public string Label => IsNestedParent ? $"{Start}+" : $"{Start}-{End}";
}
