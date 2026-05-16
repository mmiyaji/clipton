using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class LocalizationCatalogTests
{
    [Fact]
    public void Translate_ReturnsJapaneseResource()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("設定", catalog.Translate("ja", "Settings"));
    }

    [Fact]
    public void Translate_FallsBackToEnglish()
    {
        var catalog = new LocalizationCatalog();

        Assert.Equal("Settings", catalog.Translate("fr", "Settings"));
    }
}
