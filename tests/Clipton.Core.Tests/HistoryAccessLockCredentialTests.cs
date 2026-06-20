using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class HistoryAccessLockCredentialTests
{
    [Theory]
    [InlineData("1234")]
    [InlineData("123456789012")]
    public void IsValidPin_AcceptsFourToTwelveDigits(string pin)
    {
        Assert.True(HistoryAccessLockCredential.IsValidPin(pin));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567890123")]
    [InlineData("12a4")]
    public void IsValidPin_RejectsInvalidPins(string pin)
    {
        Assert.False(HistoryAccessLockCredential.IsValidPin(pin));
    }

    [Fact]
    public void Verify_ReturnsTrueOnlyForMatchingPin()
    {
        var credential = HistoryAccessLockCredential.Create("2468");

        Assert.True(HistoryAccessLockCredential.Verify("2468", credential.Salt, credential.Hash));
        Assert.False(HistoryAccessLockCredential.Verify("1357", credential.Salt, credential.Hash));
    }

    [Fact]
    public void Create_UsesDifferentSaltAndHashForSamePin()
    {
        var first = HistoryAccessLockCredential.Create("2468");
        var second = HistoryAccessLockCredential.Create("2468");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Verify_ReturnsFalseForMalformedCredential()
    {
        Assert.False(HistoryAccessLockCredential.Verify("1234", "bad", "bad"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void NormalizeTimeoutMinutes_PreservesAllowedValues(int minutes)
    {
        Assert.Equal(minutes, HistoryAccessLockCredential.NormalizeTimeoutMinutes(minutes));
    }

    [Fact]
    public void NormalizeTimeoutMinutes_FallsBackToDefault()
    {
        Assert.Equal(5, HistoryAccessLockCredential.NormalizeTimeoutMinutes(999));
    }
}
