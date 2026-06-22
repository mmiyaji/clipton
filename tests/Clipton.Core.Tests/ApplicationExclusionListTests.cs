using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class ApplicationExclusionListTests
{
    [Fact]
    public void Normalize_TrimsDeduplicatesAndNormalizesExeNames()
    {
        var patterns = ApplicationExclusionList.Normalize([
            "  notepad.exe  ",
            "NOTEPAD",
            "",
            "*",
            "\"C:\\Windows\\System32\\mspaint.exe\""
        ]);

        Assert.Equal(["notepad", "mspaint"], patterns);
    }

    [Fact]
    public void Matches_SupportsExactExeAndWildcardPatterns()
    {
        var patterns = new[] { "notepad.exe", "Teams*", "*secret*" };

        Assert.True(ApplicationExclusionList.Matches(patterns, "notepad"));
        Assert.True(ApplicationExclusionList.Matches(patterns, "TeamsClassic"));
        Assert.True(ApplicationExclusionList.Matches(patterns, "my-secret-tool"));
        Assert.False(ApplicationExclusionList.Matches(patterns, "chrome"));
    }

    [Fact]
    public void Matches_IgnoresBlankAppNamesAndCatchAllPattern()
    {
        Assert.False(ApplicationExclusionList.Matches(["*"], "notepad"));
        Assert.False(ApplicationExclusionList.Matches(["notepad"], null));
        Assert.False(ApplicationExclusionList.Matches(["notepad"], " "));
    }
}
