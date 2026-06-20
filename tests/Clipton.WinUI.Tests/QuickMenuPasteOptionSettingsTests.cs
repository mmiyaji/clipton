using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class QuickMenuPasteOptionSettingsTests
{
    [Fact]
    public void CreateHistoryContextOptions_HidesDisabledTextOptions()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        runtime.History.Add(new ClipboardSnapshot(
            "text-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: """{"url":"https://example.com","name":"Clipton"}"""));
        runtime.SetQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.PasteLowercase, enabled: false);
        runtime.SetQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.PasteFormattedJson, enabled: false);
        runtime.SetQuickMenuPasteOptionEnabled(QuickMenuPasteOptionIds.TogglePin, enabled: false);

        var optionIds = runtime.CreateHistoryContextOptions("text-1")
            .Select(option => option.Id)
            .ToArray();

        Assert.Contains(QuickMenuPasteOptionIds.PastePlain, optionIds);
        Assert.Contains(QuickMenuPasteOptionIds.PasteExtractUrls, optionIds);
        Assert.DoesNotContain(QuickMenuPasteOptionIds.PasteLowercase, optionIds);
        Assert.DoesNotContain(QuickMenuPasteOptionIds.PasteFormattedJson, optionIds);
        Assert.DoesNotContain(QuickMenuPasteOptionIds.TogglePin, optionIds);
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-option-tests", Guid.NewGuid().ToString("N"));
    }
}
