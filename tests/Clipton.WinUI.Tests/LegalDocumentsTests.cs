using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class LegalDocumentsTests
{
    [Theory]
    [InlineData(LegalDocuments.Terms, "en")]
    [InlineData(LegalDocuments.Terms, "ja")]
    [InlineData(LegalDocuments.Privacy, "en")]
    [InlineData(LegalDocuments.Privacy, "ja")]
    [InlineData(LegalDocuments.ThirdPartyNotices, "en")]
    public void Load_ReturnsTheEmbeddedDocument(string name, string language)
    {
        var text = LegalDocuments.Load(name, language);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("zh")]
    public void Load_FallsBackToEnglishWhenTheLanguageHasNoTranslation(string language)
    {
        Assert.Equal(
            LegalDocuments.Load(LegalDocuments.Terms, "en"),
            LegalDocuments.Load(LegalDocuments.Terms, language));
    }

    [Fact]
    public void Load_ReturnsNullForUnknownDocuments()
    {
        Assert.Null(LegalDocuments.Load("MISSING", "en"));
    }

    [Fact]
    public void ThirdPartyNotices_NameEveryShippedComponentLicense()
    {
        var text = LegalDocuments.Load(LegalDocuments.ThirdPartyNotices, "en")!;

        Assert.Contains("Microsoft.WindowsAppSDK.WinUI", text);
        Assert.Contains("Microsoft.Web.WebView2", text);
        Assert.Contains("System.Drawing.Common", text);
        Assert.Contains("System.Security.Cryptography.ProtectedData", text);
        Assert.Contains("BSD 3-Clause", text);
        Assert.Contains("MIT", text);
    }

    [Fact]
    public void ThirdPartyNotices_ListEveryDirectPackageReference()
    {
        var csproj = File.ReadAllText(FindRepositoryFile("src/Clipton.WinUI/Clipton.WinUI.csproj"));
        var notices = LegalDocuments.Load(LegalDocuments.ThirdPartyNotices, "en")!;
        var packages = System.Text.RegularExpressions.Regex.Matches(csproj, "PackageReference Include=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value);

        foreach (var package in packages)
        {
            Assert.True(notices.Contains(package, StringComparison.Ordinal), $"THIRD_PARTY_NOTICES is missing {package}.");
        }
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relativePath);
    }
}
