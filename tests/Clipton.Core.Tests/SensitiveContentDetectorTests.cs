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
}
