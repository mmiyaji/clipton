namespace Clipton.Core;

public static class QuickMenuHistoryBuckets
{
    public const int BucketSize = 50;
    public const int NestedStart = 101;
    public static readonly int[] TopLevelHistoryItemOptions = [5, 10, 20, 30, 50];

    public static int NormalizeTopLevelHistoryItems(int count)
    {
        return TopLevelHistoryItemOptions.Contains(count) ? count : 5;
    }

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

public sealed record QuickMenuHistoryRange(int Start, int End, bool IsNestedParent = false)
{
    public int Count => End - Start + 1;
    public int Offset => Start - 1;
    public string Label => IsNestedParent ? $"{Start}+" : $"{Start}-{End}";
}
