using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SearchFilterTests
{
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

    [Fact]
    public void Parse_QuotedTextKeepsSpaces()
    {
        var filter = SearchFilter.Parse("\"hello world\" type:html");

        Assert.True(filter.MatchesText(() => "Say hello world"));
        Assert.False(filter.MatchesText(() => "Say hello"));
        Assert.True(filter.MatchesType([ClipboardFormatKind.Html]));
    }
}
