using Clipton.Core;
using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class HistoryPreviewDisplayTests
{
    [Fact]
    public void CreateHistoryItemViewModel_ReplacesLineBreaksWithPreviewMarker()
    {
        using var runtime = new CliptonRuntime(CreateTestRoot(), isSafeMode: true);
        var snapshot = new ClipboardSnapshot(
            "text-1",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "first line\r\nsecond\tline\n\nthird line");

        var item = runtime.CreateHistoryItemViewModel(snapshot, includeThumbnail: false);

        Assert.Equal("first line \u21B5 second line \u21B5 \u21B5 third line", item.Preview);
    }

    private static string CreateTestRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clipton-winui-preview-tests", Guid.NewGuid().ToString("N"));
    }
}
