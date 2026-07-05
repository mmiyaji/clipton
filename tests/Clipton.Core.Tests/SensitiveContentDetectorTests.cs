using Clipton.Core;
using System.Diagnostics;

namespace Clipton.Core.Tests;

public sealed class SensitiveContentDetectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldMask_ReturnsFalseForNullOrWhitespace(string? text)
    {
        Assert.False(SensitiveContentDetector.ShouldMask(text));
    }

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

    [Fact]
    public void ShouldMask_PhoneNumberRequiresSeparator()
    {
        var phoneOnlyRules = new MaskRuleSettings
        {
            Email = false,
            CreditCard = false,
            SecretKeyword = false,
            BearerToken = false,
            LongToken = false,
            ShortAlphanumericCode = false,
            PhoneNumber = true,
            CustomPattern = false
        };

        Assert.False(SensitiveContentDetector.ShouldMask("Order number 123456789012", rules: phoneOnlyRules));
        Assert.Null(SensitiveContentDetector.CreateMaskedPreview("Order number 123456789012", rules: phoneOnlyRules));
        Assert.True(SensitiveContentDetector.ShouldMask("Call 03-1234-5678", rules: phoneOnlyRules));
    }

    [Fact]
    public void ShouldMask_IgnoresInvalidDisabledAndBlankRules()
    {
        var rules = MaskRuleDefinitionDefaults.CreateDefaultRules();
        rules.First(rule => rule.Id == MaskRuleIds.Email).Pattern = "[";
        rules.First(rule => rule.Id == MaskRuleIds.ShortAlphanumericCode).Enabled = false;

        Assert.False(SensitiveContentDetector.ShouldMask("user@example.com alpha-123", rules, ["alpha-\\d+"], customPatternsEnabled: false));
    }

    [Fact]
    public void ShouldMask_DoesNotMaskInvalidCreditCardNumbers()
    {
        const string invalidCard = "4111 1111 1111 1112";
        var rules = new MaskRuleSettings
        {
            Email = false,
            CreditCard = true,
            SecretKeyword = false,
            BearerToken = false,
            LongToken = false,
            ShortAlphanumericCode = false,
            PhoneNumber = false,
            CustomPattern = false
        };

        Assert.False(SensitiveContentDetector.ShouldMask(invalidCard, rules: rules));
        Assert.Null(SensitiveContentDetector.CreateMaskedPreview(invalidCard, rules: rules));
    }

    [Theory]
    [InlineData("1234 5678 9012")]
    [InlineData("1234 5678 9012 3456 7890")]
    public void ShouldMask_DoesNotMaskCreditCardLengthMismatches(string value)
    {
        var rules = new MaskRuleSettings
        {
            Email = false,
            CreditCard = true,
            SecretKeyword = false,
            BearerToken = false,
            LongToken = false,
            ShortAlphanumericCode = false,
            PhoneNumber = false,
            CustomPattern = false
        };

        Assert.False(SensitiveContentDetector.ShouldMask(value, rules: rules));
    }

    [Theory]
    [InlineData("1234 5678 9012")]
    [InlineData("1234 5678 9012 3456 7890")]
    public void ShouldMask_RejectsCreditCardMatchesOutsideSupportedDigitLengthWithEditedPattern(string value)
    {
        var definitions = MaskRuleDefinitionDefaults.CreateDefaultRules(new MaskRuleSettings
        {
            Email = false,
            CreditCard = true,
            SecretKeyword = false,
            BearerToken = false,
            LongToken = false,
            ShortAlphanumericCode = false,
            PhoneNumber = false,
            CustomPattern = false
        });
        definitions.First(rule => rule.Id == MaskRuleIds.CreditCard).Pattern = @"\d+";

        Assert.False(SensitiveContentDetector.ShouldMask(value, definitions, customPatternsEnabled: false));
        Assert.Null(SensitiveContentDetector.CreateMaskedPreview(value, 3, definitions, customPatternsEnabled: false));
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
    public void CreateMaskedPreview_IsIndependentOfRuleOrderForOverlappingMatches()
    {
        const string text = "api_key=abcdefghijklmnopqrstuvwxyz123456";
        var defaultRules = MaskRuleDefinitionDefaults.CreateDefaultRules();
        var reversedRules = MaskRuleDefinitionDefaults.CreateDefaultRules();
        reversedRules.First(rule => rule.Id == MaskRuleIds.SecretKeyword).Order = 90;
        reversedRules.First(rule => rule.Id == MaskRuleIds.LongToken).Order = 10;

        var defaultPreview = SensitiveContentDetector.CreateMaskedPreview(text, 3, defaultRules);
        var reversedPreview = SensitiveContentDetector.CreateMaskedPreview(text, 3, reversedRules);

        Assert.Equal("api_key=abc\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022", defaultPreview);
        Assert.Equal(defaultPreview, reversedPreview);
    }

    [Fact]
    public void CreateMaskedPreview_DoesNotSplitTextElementsWhenMasking()
    {
        var preview = SensitiveContentDetector.CreateMaskedPreview(
            "Code: \ud83d\ude00TOKEN123",
            visiblePrefixLength: 1,
            maskRuleDefinitions: [],
            customPatterns: ["\ud83d\ude00TOKEN\\d+"],
            customPatternsEnabled: true);

        Assert.Equal("Code: \ud83d\ude00\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022", preview);
    }

    [Fact]
    public void CreateMaskedPreview_ReturnsNullForOrdinaryText()
    {
        Assert.Null(SensitiveContentDetector.CreateMaskedPreview("Hello, please review this message."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateMaskedPreview_ReturnsNullForNullOrWhitespace(string? text)
    {
        Assert.Null(SensitiveContentDetector.CreateMaskedPreview(text));
    }

    [Fact]
    public void CreateMaskedPreview_HandlesVisiblePrefixBounds()
    {
        Assert.Equal("Email \u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022", SensitiveContentDetector.CreateMaskedPreview("Email user@example.com", 0));
        Assert.Equal(
            "Email user@example.com\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022",
            SensitiveContentDetector.CreateMaskedPreview("Email user@example.com", 100));
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
    public void CreatePreviewScanText_NormalizesShortTextAndHardCutsWithoutWordBoundary()
    {
        Assert.Equal("one two", SensitiveContentDetector.CreatePreviewScanText("  one\r\ntwo  ", maxLength: 20));
        Assert.Equal(new string('x', 10), SensitiveContentDetector.CreatePreviewScanText(new string('x', 20), maxLength: 10));
    }

    [Fact]
    public void CreatePreviewScanText_DoesNotSplitSurrogatePairs()
    {
        Assert.Equal("\ud83d\ude00", SensitiveContentDetector.CreatePreviewScanText("\ud83d\ude00abc", maxLength: 2));
        Assert.Equal(string.Empty, SensitiveContentDetector.CreatePreviewScanText("\ud83d\ude00abc", maxLength: 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePreviewScanText_ReturnsNullForNullOrWhitespace(string? text)
    {
        Assert.Null(SensitiveContentDetector.CreatePreviewScanText(text));
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
    public void FindMatchedRules_ReturnsCreditCardRuleForValidLuhnNumber()
    {
        var matches = SensitiveContentDetector.FindMatchedRules(
            "Card 4111 1111 1111 1111",
            MaskRuleDefinitionDefaults.CreateDefaultRules());

        Assert.Contains(matches, match => match.RuleId == MaskRuleIds.CreditCard);
    }

    [Fact]
    public void FindMatchedRules_SkipsEnabledBuiltInRulesThatDoNotMatch()
    {
        var rules = MaskRuleDefinitionDefaults.CreateDefaultRules();

        var matches = SensitiveContentDetector.FindMatchedRules("ordinary memo", rules, customPatternsEnabled: false);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatchedRules_IgnoresInvalidCreditCardRegexRule()
    {
        var rules = MaskRuleDefinitionDefaults.CreateDefaultRules();
        rules.First(rule => rule.Id == MaskRuleIds.CreditCard).Pattern = "[";

        var matches = SensitiveContentDetector.FindMatchedRules("Card 4111 1111 1111 1111", rules);

        Assert.DoesNotContain(matches, match => match.RuleId == MaskRuleIds.CreditCard);
    }

    [Fact]
    public void FindMatchedRules_ReturnsEmptyForWhitespaceAndWhenCustomPatternsAreDisabled()
    {
        Assert.Empty(SensitiveContentDetector.FindMatchedRules("   ", MaskRuleDefinitionDefaults.CreateDefaultRules(), ["alpha-\\d+"]));
        Assert.Empty(SensitiveContentDetector.FindMatchedRules("Project alpha-123", [], ["alpha-\\d+"], customPatternsEnabled: false));
    }

    [Fact]
    public void FindMatchedRules_DeduplicatesCustomPatterns()
    {
        var matches = SensitiveContentDetector.FindMatchedRules(
            "Project alpha-123",
            [],
            [" alpha-\\d+ ", "alpha-\\d+"],
            customPatternsEnabled: true);

        var match = Assert.Single(matches);
        Assert.True(match.IsCustomPattern);
        Assert.Equal("alpha-\\d+", match.Pattern);
    }

    [Fact]
    public void FindMatchedRules_SkipsCustomPatternWhenRegexDoesNotMatch()
    {
        var matches = SensitiveContentDetector.FindMatchedRules(
            "Project beta",
            [],
            ["alpha-\\d+"],
            customPatternsEnabled: true);

        Assert.Empty(matches);
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
    public void ShouldMask_ReturnsFalseWhenValidCustomPatternDoesNotMatch()
    {
        Assert.False(SensitiveContentDetector.ShouldMask("Project code: beta", [], ["alpha-\\d+"], customPatternsEnabled: true));
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
    public void CreateMaskedPreview_UsesFallbackMaskForEditedSecretAndBearerPatternsWithoutGroups()
    {
        var rules = MaskRuleDefinitionDefaults.CreateDefaultRules();
        rules.First(rule => rule.Id == MaskRuleIds.SecretKeyword).Pattern = "secret-token";
        rules.First(rule => rule.Id == MaskRuleIds.BearerToken).Pattern = "bearer-token";

        var preview = SensitiveContentDetector.CreateMaskedPreview(
            "secret-token bearer-token",
            2,
            rules,
            customPatternsEnabled: false);

        Assert.Equal("se\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022 be\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022", preview);
    }

    [Fact]
    public void ValidateCustomPatterns_TrimsAndDropsInvalidPatterns()
    {
        var patterns = SensitiveContentDetector.ValidateCustomPatterns([" alpha-\\d+ ", "["]);

        Assert.Equal(["alpha-\\d+"], patterns);
        Assert.Equal(["["], SensitiveContentDetector.GetInvalidCustomPatterns([" alpha-\\d+ ", "["]));
    }

    [Fact]
    public void GetInvalidCustomPatterns_ReturnsEmptyForNull()
    {
        Assert.Empty(SensitiveContentDetector.GetInvalidCustomPatterns(null));
    }
}
