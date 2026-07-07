using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SearchFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t  ")]
    public void Parse_NullOrWhitespaceQueryIsEmpty(string? query)
    {
        var filter = SearchFilter.Parse(query);

        Assert.Equal(string.Empty, filter.Query);
        Assert.True(filter.IsEmpty);
        Assert.True(filter.MatchesText(() => throw new InvalidOperationException("No text should be requested for an empty filter.")));
        Assert.True(filter.MatchesPinned(true));
        Assert.True(filter.MatchesPinned(false));
        Assert.True(filter.MatchesUrl(true));
        Assert.True(filter.MatchesUrl(false));
        Assert.True(filter.MatchesType([ClipboardFormatKind.Image]));
        Assert.True(filter.MatchesDate(DateTimeOffset.MinValue));
    }

    [Fact]
    public void Parse_MatchesKeywordTypePinnedUrlAndDate()
    {
        var filter = SearchFilter.Parse("invoice type:text pinned:true url:true after:2026-05-01 before:2026-05-30");

        Assert.False(filter.IsEmpty);
        Assert.True(filter.MatchesText(() => "monthly invoice https://example.com"));
        Assert.True(filter.MatchesType([ClipboardFormatKind.Text]));
        Assert.True(filter.MatchesPinned(true));
        Assert.True(filter.MatchesUrl(true));
        Assert.True(filter.MatchesDate(new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero)));
        Assert.False(filter.MatchesPinned(false));
        Assert.False(filter.MatchesType([ClipboardFormatKind.Image]));
        Assert.False(filter.MatchesDate(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("off")]
    public void Parse_FalseBooleanAliasesRestrictPinnedAndUrl(string value)
    {
        var filter = SearchFilter.Parse($"pinned:{value} url:{value}");

        Assert.False(filter.IsEmpty);
        Assert.False(filter.Pinned);
        Assert.False(filter.HasUrl);
        Assert.True(filter.MatchesPinned(false));
        Assert.False(filter.MatchesPinned(true));
        Assert.True(filter.MatchesUrl(false));
        Assert.False(filter.MatchesUrl(true));
    }

    [Fact]
    public void Parse_PinFormatAndHasUrlAliasesApplyFilters()
    {
        var filter = SearchFilter.Parse("format:rtf pin:yes hasurl:on");

        Assert.Equal("rtf", filter.Type);
        Assert.True(filter.Pinned);
        Assert.True(filter.HasUrl);
        Assert.True(filter.MatchesType([ClipboardFormatKind.RichText]));
        Assert.False(filter.MatchesType([ClipboardFormatKind.Text]));
        Assert.True(filter.MatchesPinned(true));
        Assert.False(filter.MatchesPinned(false));
        Assert.True(filter.MatchesUrl(true));
        Assert.False(filter.MatchesUrl(false));
    }

    [Fact]
    public void Parse_InvalidBooleanAndDateFiltersDoNotRestrictMatches()
    {
        var filter = SearchFilter.Parse("pinned:maybe url:sometimes after:not-a-date before:never");

        Assert.True(filter.IsEmpty);
        Assert.Null(filter.Pinned);
        Assert.Null(filter.HasUrl);
        Assert.Null(filter.After);
        Assert.Null(filter.Before);
        Assert.True(filter.MatchesPinned(true));
        Assert.True(filter.MatchesPinned(false));
        Assert.True(filter.MatchesUrl(true));
        Assert.True(filter.MatchesUrl(false));
        Assert.True(filter.MatchesDate(DateTimeOffset.MinValue));
        Assert.True(filter.MatchesDate(DateTimeOffset.MaxValue));
    }

    [Fact]
    public void Parse_UnknownFilterKeyIsMatchedAsText()
    {
        var filter = SearchFilter.Parse("project:clipton release");

        Assert.False(filter.IsEmpty);
        Assert.True(filter.MatchesText(() => "release notes for project:clipton"));
        Assert.True(filter.MatchesText(() => "release notes for project clipton"));
        Assert.False(filter.MatchesText(() => "release notes for clipton"));
        Assert.False(filter.MatchesText(() => null));
    }

    [Fact]
    public void MatchesDate_IncludesAfterAndBeforeBoundaries()
    {
        var filter = SearchFilter.Parse("after:2026-05-01 before:2026-05-30");

        Assert.True(filter.After.HasValue);
        Assert.True(filter.Before.HasValue);

        var after = filter.After.Value;
        var before = filter.Before.Value;

        Assert.True(filter.MatchesDate(after));
        Assert.False(filter.MatchesDate(after.AddTicks(-1)));
        Assert.True(filter.MatchesDate(before));
        Assert.False(filter.MatchesDate(before.AddTicks(1)));
    }

    [Fact]
    public void MatchesDate_AppliesAfterOnlyFilter()
    {
        var filter = SearchFilter.Parse("after:2026-05-01");

        Assert.True(filter.After.HasValue);
        Assert.Null(filter.Before);
        Assert.False(filter.MatchesDate(filter.After.Value.AddTicks(-1)));
        Assert.True(filter.MatchesDate(filter.After.Value));
        Assert.True(filter.MatchesDate(filter.After.Value.AddYears(1)));
    }

    [Fact]
    public void MatchesDate_AppliesBeforeOnlyFilter()
    {
        var filter = SearchFilter.Parse("before:2026-05-30");

        Assert.Null(filter.After);
        Assert.True(filter.Before.HasValue);
        Assert.True(filter.MatchesDate(filter.Before.Value.AddYears(-1)));
        Assert.True(filter.MatchesDate(filter.Before.Value));
        Assert.False(filter.MatchesDate(filter.Before.Value.AddTicks(1)));
    }

    [Fact]
    public void MatchesDate_DateOnlyBeforeIncludesWholeDayButTimeBeforeDoesNot()
    {
        var dateOnly = SearchFilter.Parse("before:2026-05-30");
        var withTime = SearchFilter.Parse("before:2026-05-30T12:30:00");

        Assert.True(dateOnly.Before.HasValue);
        Assert.True(withTime.Before.HasValue);
        Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerDay - 1), dateOnly.Before.Value.TimeOfDay);
        Assert.True(dateOnly.MatchesDate(dateOnly.Before.Value));
        Assert.False(withTime.MatchesDate(withTime.Before.Value.AddTicks(1)));
    }

    [Theory]
    [InlineData("rich", ClipboardFormatKind.RichText, ClipboardFormatKind.Text)]
    [InlineData("rtf", ClipboardFormatKind.RichText, ClipboardFormatKind.Html)]
    [InlineData("image", ClipboardFormatKind.Image, ClipboardFormatKind.Text)]
    [InlineData("file", ClipboardFormatKind.FileDrop, ClipboardFormatKind.Text)]
    [InlineData("files", ClipboardFormatKind.FileDrop, ClipboardFormatKind.Image)]
    public void MatchesType_RecognizesTypeAliases(
        string type,
        ClipboardFormatKind matchingFormat,
        ClipboardFormatKind nonMatchingFormat)
    {
        var filter = SearchFilter.Parse($"type:{type}");

        Assert.True(filter.MatchesType([matchingFormat]));
        Assert.False(filter.MatchesType([nonMatchingFormat]));
    }

    [Fact]
    public void MatchesType_UnknownTypeDoesNotRestrictFormats()
    {
        var filter = SearchFilter.Parse("type:spreadsheet");

        Assert.Equal("spreadsheet", filter.Type);
        Assert.True(filter.MatchesType([ClipboardFormatKind.Text]));
        Assert.True(filter.MatchesType([ClipboardFormatKind.Image]));
    }

    [Fact]
    public void Parse_BlankTypeDoesNotRestrictFormats()
    {
        var filter = SearchFilter.Parse("type:");

        Assert.True(filter.IsEmpty);
        Assert.Null(filter.Type);
        Assert.True(filter.MatchesType([ClipboardFormatKind.FileDrop]));
    }

    [Fact]
    public void Parse_CombinesTextAliasesAndDateFilters()
    {
        var filter = SearchFilter.Parse("\"release notes\" format:files pin:off hasurl:1 from:2026-05-01 to:2026-05-01");

        Assert.True(filter.MatchesText(() => "Clipton release notes"));
        Assert.False(filter.MatchesText(() => "Clipton release"));
        Assert.True(filter.MatchesType([ClipboardFormatKind.FileDrop]));
        Assert.False(filter.MatchesType([ClipboardFormatKind.Html]));
        Assert.True(filter.MatchesPinned(false));
        Assert.False(filter.MatchesPinned(true));
        Assert.True(filter.MatchesUrl(true));
        Assert.False(filter.MatchesUrl(false));

        Assert.True(filter.After.HasValue);
        Assert.True(filter.Before.HasValue);
        Assert.True(filter.MatchesDate(filter.After.Value));
        Assert.True(filter.MatchesDate(filter.Before.Value));
        Assert.False(filter.MatchesDate(filter.Before.Value.AddTicks(1)));
    }

    [Fact]
    public void Parse_QuotedTextKeepsSpaces()
    {
        var filter = SearchFilter.Parse("\"hello world\" type:html");

        Assert.True(filter.MatchesText(() => "Say hello world"));
        Assert.False(filter.MatchesText(() => "Say hello"));
        Assert.True(filter.MatchesType([ClipboardFormatKind.Html]));
    }
}
