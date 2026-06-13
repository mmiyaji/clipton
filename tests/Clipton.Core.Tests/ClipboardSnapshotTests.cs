using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class ClipboardSnapshotTests
{
    [Fact]
    public void Constructor_NormalizesSourceMetadata()
    {
        var snapshot = new ClipboardSnapshot(
            "origin",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "value",
            sourceApplicationName: "  notepad  ",
            sourceWindowTitle: "  notes\r\nfile.txt  ");

        Assert.Equal("notepad", snapshot.SourceApplicationName);
        Assert.Equal("notes file.txt", snapshot.SourceWindowTitle);
    }

    [Fact]
    public void Fingerprint_IgnoresSourceMetadata()
    {
        var first = new ClipboardSnapshot(
            "first",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "same",
            sourceApplicationName: "notepad");
        var second = new ClipboardSnapshot(
            "second",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text],
            text: "same",
            sourceApplicationName: "code");

        Assert.Equal(ClipboardHistory.CreateFingerprint(first), ClipboardHistory.CreateFingerprint(second));
    }
}
