using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class ClipboardSnapshotTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsMissingId(string? id)
    {
        Assert.Throws<ArgumentException>(() => Snapshot(id: id!));
    }

    [Fact]
    public void Constructor_DeduplicatesAndSortsFormats()
    {
        var snapshot = Snapshot(
            formats:
            [
                ClipboardFormatKind.Image,
                ClipboardFormatKind.Text,
                ClipboardFormatKind.Image,
                ClipboardFormatKind.Html
            ]);

        Assert.Equal(
            [ClipboardFormatKind.Text, ClipboardFormatKind.Html, ClipboardFormatKind.Image],
            snapshot.Formats);
    }

    [Fact]
    public void Constructor_CopiesMutableInputsAndNormalizesEmptyMetadata()
    {
        var image = new byte[] { 1, 2, 3 };
        var files = new List<string> { "one.txt" };

        var snapshot = Snapshot(
            imagePng: image,
            filePaths: files,
            sourceApplicationName: "   ",
            sourceWindowTitle: null);

        image[0] = 9;
        files.Add("two.txt");

        Assert.Equal([1, 2, 3], snapshot.ImagePng);
        Assert.Equal(["one.txt"], snapshot.FilePaths);
        Assert.Null(snapshot.SourceApplicationName);
        Assert.Null(snapshot.SourceWindowTitle);
    }

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
    public void Constructor_TruncatesLongSourceMetadata()
    {
        var snapshot = Snapshot(
            sourceApplicationName: new string('a', 181),
            sourceWindowTitle: new string('b', 200));

        Assert.Equal(180, snapshot.SourceApplicationName?.Length);
        Assert.Equal(new string('a', 180), snapshot.SourceApplicationName);
        Assert.Equal(180, snapshot.SourceWindowTitle?.Length);
        Assert.Equal(new string('b', 180), snapshot.SourceWindowTitle);
    }

    [Fact]
    public void Preview_PrefersTextAndNormalizesLineEndings()
    {
        var snapshot = Snapshot(
            text: "  first\r\nsecond\nthird  ",
            rtf: @"{\rtf1 value}",
            html: "<p>value</p>",
            imagePng: [1],
            filePaths: ["file.txt"]);

        Assert.Equal("first second third", snapshot.Preview);
    }

    [Fact]
    public void Preview_UsesFileNamesWhenTextIsNullOrWhiteSpace()
    {
        var snapshot = Snapshot(
            text: "   ",
            imagePng: [1],
            filePaths: ["one.txt", "two.txt"]);

        Assert.Equal("one.txt, two.txt", snapshot.Preview);
    }

    [Fact]
    public void Preview_UsesImageWhenNoTextOrFiles()
    {
        var snapshot = Snapshot(
            text: null,
            rtf: @"{\rtf1 value}",
            html: "<p>value</p>",
            imagePng: [1]);

        Assert.Equal("Image", snapshot.Preview);
    }

    [Fact]
    public void Preview_UsesRichTextWhenImageIsEmpty()
    {
        var snapshot = Snapshot(
            text: "",
            rtf: @"{\rtf1 value}",
            html: "<p>value</p>",
            imagePng: []);

        Assert.Equal("Rich text", snapshot.Preview);
    }

    [Fact]
    public void Preview_UsesHtmlWhenRichTextIsNullOrWhiteSpace()
    {
        var snapshot = Snapshot(
            rtf: "   ",
            html: "<p>value</p>");

        Assert.Equal("HTML", snapshot.Preview);
    }

    [Fact]
    public void Preview_DefaultsForNullAndEmptyValues()
    {
        var snapshot = Snapshot(
            formats: [],
            text: null,
            rtf: "",
            html: "   ",
            imagePng: [],
            filePaths: []);

        Assert.Equal("Clipboard item", snapshot.Preview);
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

    private static ClipboardSnapshot Snapshot(
        string id = "snapshot",
        IReadOnlyCollection<ClipboardFormatKind>? formats = null,
        string? text = null,
        string? rtf = null,
        string? html = null,
        byte[]? imagePng = null,
        IReadOnlyList<string>? filePaths = null,
        string? sourceApplicationName = null,
        string? sourceWindowTitle = null)
    {
        return new ClipboardSnapshot(
            id,
            DateTimeOffset.UtcNow,
            formats ?? Array.Empty<ClipboardFormatKind>(),
            text: text,
            rtf: rtf,
            html: html,
            imagePng: imagePng,
            filePaths: filePaths,
            sourceApplicationName: sourceApplicationName,
            sourceWindowTitle: sourceWindowTitle);
    }
}
