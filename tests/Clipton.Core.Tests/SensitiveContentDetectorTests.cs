using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SensitiveContentDetectorTests
{
    [Theory]
    [InlineData("contact me at user@example.com")]
    [InlineData("api_key=abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("4111 1111 1111 1111")]
    public void ShouldMask_ReturnsTrueForSensitiveLookingText(string text)
    {
        Assert.True(SensitiveContentDetector.ShouldMask(text));
    }

    [Fact]
    public void ShouldMask_ReturnsFalseForOrdinaryText()
    {
        Assert.False(SensitiveContentDetector.ShouldMask("Hello, please review this message."));
    }

    [Theory]
    [InlineData("contact me at user@example.com", "contact me at use\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022")]
    [InlineData("api_key=abcdefghijklmnopqrstuvwxyz123456", "api_key=abc\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022")]
    [InlineData("Authorization: bearer abcdefghijklmnopqrstuvwxyz123456", "Authorization: bearer abc\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022")]
    [InlineData("4111 1111 1111 1111", "411\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022")]
    public void CreateMaskedPreview_MasksSensitivePortionsWithPrefix(string text, string expected)
    {
        Assert.Equal(expected, SensitiveContentDetector.CreateMaskedPreview(text));
    }

    [Fact]
    public void CreateMaskedPreview_ReturnsNullForOrdinaryText()
    {
        Assert.Null(SensitiveContentDetector.CreateMaskedPreview("Hello, please review this message."));
    }

    [Fact]
    public void ShouldMask_ReturnsTrueForCustomPattern()
    {
        Assert.True(SensitiveContentDetector.ShouldMask("Project code: alpha-123", ["alpha-\\d+"]));
    }

    [Fact]
    public void CreateMaskedPreview_MasksCustomPatternsWithConfiguredPrefix()
    {
        var preview = SensitiveContentDetector.CreateMaskedPreview(
            "Project code: alpha-123",
            visiblePrefixLength: 2,
            customPatterns: ["alpha-\\d+"]);

        Assert.Equal("Project code: al\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022", preview);
    }

    [Fact]
    public void ValidateCustomPatterns_TrimsAndDropsInvalidPatterns()
    {
        var patterns = SensitiveContentDetector.ValidateCustomPatterns([" alpha-\\d+ ", "["]);

        Assert.Equal(["alpha-\\d+"], patterns);
        Assert.Equal(["["], SensitiveContentDetector.GetInvalidCustomPatterns([" alpha-\\d+ ", "["]));
    }
}
