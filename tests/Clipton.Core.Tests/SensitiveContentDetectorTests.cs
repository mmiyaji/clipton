using Clipton.Core;
using System.Diagnostics;

namespace Clipton.Core.Tests;

public sealed class SensitiveContentDetectorTests
{
    [Theory]
    [InlineData("contact me at user@example.com")]
    [InlineData("api_key=abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("Store ID: 9NSS9P4F6S5M")]
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
    [InlineData("Store ID: 9NSS9P4F6S5M", "Store ID: 9NS\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022")]
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
    public void CreateMaskedPreview_RespectsDisabledShortAlphanumericCodeRule()
    {
        var rules = new MaskRuleSettings { ShortAlphanumericCode = false };

        Assert.Null(SensitiveContentDetector.CreateMaskedPreview("Store ID: 9NSS9P4F6S5M", rules: rules));
    }

    [Fact]
    public void ShouldMask_RespectsBearerTokenRule()
    {
        const string text = "Authorization: bearer abc.def-123";

        Assert.True(SensitiveContentDetector.ShouldMask(text));
        Assert.False(SensitiveContentDetector.ShouldMask(text, rules: new MaskRuleSettings { BearerToken = false }));
    }

    [Fact]
    public void CreateMaskedPreview_UsesEditedDefaultRulePattern()
    {
        var rules = MaskRuleDefinitionDefaults.CreateDefaultRules();
        var shortCodeRule = rules.First(rule => rule.Id == MaskRuleIds.ShortAlphanumericCode);
        shortCodeRule.Pattern = @"\bSTORE-[A-Z0-9]{4}\b";

        var preview = SensitiveContentDetector.CreateMaskedPreview(
            "Store ID: 9NSS9P4F6S5M and STORE-AB12",
            3,
            rules);

        Assert.Equal("Store ID: 9NSS9P4F6S5M and STO\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022", preview);
    }

    [Fact]
    public void CreateMaskedPreview_RespectsDisabledEditedRule()
    {
        var rules = MaskRuleDefinitionDefaults.CreateDefaultRules();
        var shortCodeRule = rules.First(rule => rule.Id == MaskRuleIds.ShortAlphanumericCode);
        shortCodeRule.Enabled = false;

        Assert.Null(SensitiveContentDetector.CreateMaskedPreview("Store ID: 9NSS9P4F6S5M", 3, rules));
    }

    [Fact]
    public void CreatePreviewScanText_TruncatesLongTextAtWordBoundary()
    {
        var text = $"{new string('a', 10)} {new string('b', 10)} {new string('c', 10)}";

        var scanText = SensitiveContentDetector.CreatePreviewScanText(text, maxLength: 18);

        Assert.Equal(new string('a', 10), scanText);
    }

    [Fact]
    public void CreatePreviewScanText_LimitsInvisibleTailBeforeMasking()
    {
        var text = $"{string.Concat(Enumerable.Repeat("note ", 260))} Store ID: 9NSS9P4F6S5M";
        var scanText = SensitiveContentDetector.CreatePreviewScanText(text);

        Assert.DoesNotContain("9NSS9P4F6S5M", scanText);
        Assert.Null(SensitiveContentDetector.CreateMaskedPreview(scanText));
    }

    [Fact]
    public void FindMatchedRules_ReturnsBuiltInAndCustomRuleNames()
    {
        var rules = MaskRuleDefinitionDefaults.CreateDefaultRules();

        var matches = SensitiveContentDetector.FindMatchedRules(
            "Email user@example.com project alpha-123 store 9NSS9P4F6S5M",
            rules,
            ["alpha-\\d+"],
            customPatternsEnabled: true);

        Assert.Contains(matches, match => match.RuleId == MaskRuleIds.Email && match.NameKey == "MaskRuleEmail");
        Assert.Contains(matches, match => match.RuleId == MaskRuleIds.ShortAlphanumericCode && match.NameKey == "MaskRuleShortAlphanumericCode");
        Assert.Contains(matches, match => match.IsCustomPattern && match.NameKey == "MaskRuleCustomPattern");
    }

    [Fact]
    public void FindMatchedRules_RespectsDisabledRules()
    {
        var rules = MaskRuleDefinitionDefaults.CreateDefaultRules();
        rules.First(rule => rule.Id == MaskRuleIds.ShortAlphanumericCode).Enabled = false;

        var matches = SensitiveContentDetector.FindMatchedRules("Store ID: 9NSS9P4F6S5M", rules);

        Assert.DoesNotContain(matches, match => match.RuleId == MaskRuleIds.ShortAlphanumericCode);
    }

    [Fact]
    public void CreateMaskedPreview_ProcessesThousandItemsWithinBudget()
    {
        var rules = MaskRuleDefinitionDefaults.CreateDefaultRules();
        var samples = Enumerable.Range(0, 1000)
            .Select(index => $"Item {index}: user{index}@example.com token=abcdefghijklmnopqrstuvwxyz{index:D6} store 9NSS9P4F6S5M")
            .ToArray();

        _ = SensitiveContentDetector.CreateMaskedPreview(samples[0], 3, rules);
        var stopwatch = Stopwatch.StartNew();
        var masked = 0;
        foreach (var sample in samples)
        {
            if (SensitiveContentDetector.CreateMaskedPreview(sample, 3, rules) is not null)
            {
                masked++;
            }
        }

        stopwatch.Stop();

        Assert.Equal(samples.Length, masked);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Masking 1000 items took {stopwatch.ElapsedMilliseconds} ms.");
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
