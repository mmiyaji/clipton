using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clipton.Core;

namespace Clipton.App.Tests;

// These tests exercise CaptureFrom/CreateDataObject against in-memory
// DataObject instances. They intentionally avoid the real system clipboard:
// clipboard listeners (resident Clipton, Windows clipboard history, ...) race
// with OpenClipboard and made the previous versions flaky.
public sealed class ClipboardBridgeTests
{
    [Fact]
    public void CaptureFrom_ReadsTextRtfAndHtmlFormats()
    {
        var data = new DataObject();
        data.SetText("Plain text", TextDataFormat.UnicodeText);
        data.SetText(@"{\rtf1\ansi Plain text}", TextDataFormat.Rtf);
        data.SetText("Version:1.0\r\nStartHTML:00000097\r\nEndHTML:00000161\r\nStartFragment:00000129\r\nEndFragment:00000129\r\n<html><body><!--StartFragment--><b>Plain text</b><!--EndFragment--></body></html>", TextDataFormat.Html);

        var snapshot = ClipboardBridge.CaptureFrom(data);

        Assert.NotNull(snapshot);
        Assert.Contains(ClipboardFormatKind.Text, snapshot.Formats);
        Assert.Contains(ClipboardFormatKind.RichText, snapshot.Formats);
        Assert.Contains(ClipboardFormatKind.Html, snapshot.Formats);
        Assert.Equal("Plain text", snapshot.Text);
        Assert.Contains(@"{\rtf1", snapshot.Rtf);
        Assert.Contains("StartHTML", snapshot.Html);
    }

    [Fact]
    public void CaptureFrom_ExtractsPlainTextFromHtmlWhenUnicodeTextIsMissing()
    {
        var data = new DataObject();
        data.SetText("Version:1.0\r\nStartHTML:00000097\r\nEndHTML:00000162\r\nStartFragment:00000129\r\nEndFragment:00000130\r\n<html><body><!--StartFragment--><p>Hello<br>World</p><!--EndFragment--></body></html>", TextDataFormat.Html);

        var snapshot = ClipboardBridge.CaptureFrom(data);

        Assert.NotNull(snapshot);
        Assert.Contains(ClipboardFormatKind.Text, snapshot.Formats);
        Assert.Contains(ClipboardFormatKind.Html, snapshot.Formats);
        Assert.Equal("Hello\nWorld", snapshot.Text);
    }

    [Fact]
    public void CaptureFrom_ExtractsPlainTextFromRtfWhenUnicodeTextIsMissing()
    {
        var data = new DataObject();
        data.SetText(@"{\rtf1\ansi Rtf only}", TextDataFormat.Rtf);

        var snapshot = ClipboardBridge.CaptureFrom(data);

        Assert.NotNull(snapshot);
        Assert.Contains(ClipboardFormatKind.Text, snapshot.Formats);
        Assert.Contains(ClipboardFormatKind.RichText, snapshot.Formats);
        Assert.Equal("Rtf only", snapshot.Text);
    }

    [Fact]
    public void CaptureFrom_ReturnsNullForEmptyDataObject()
    {
        Assert.Null(ClipboardBridge.CaptureFrom(new DataObject()));
        Assert.Null(ClipboardBridge.CaptureFrom(null));
    }

    [Fact]
    public void CreateDataObject_WritesPlainTextWhenRequested()
    {
        var snapshot = new ClipboardSnapshot("text", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: "Hello");

        var data = ClipboardBridge.CreateDataObject(snapshot, asPlainText: true);

        Assert.Equal("Hello", data.GetText(TextDataFormat.UnicodeText));
        Assert.False(data.ContainsText(TextDataFormat.Rtf));
    }

    [Fact]
    public void CreateDataObject_WritesExtractedPlainTextWhenRequested()
    {
        var snapshot = new ClipboardSnapshot(
            "html",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Html],
            html: "Version:1.0\r\nStartHTML:00000097\r\nEndHTML:00000162\r\nStartFragment:00000129\r\nEndFragment:00000130\r\n<html><body><!--StartFragment--><b>HTML only</b><!--EndFragment--></body></html>");

        var data = ClipboardBridge.CreateDataObject(snapshot, asPlainText: true);

        Assert.Equal("HTML only", data.GetText(TextDataFormat.UnicodeText));
    }

    [Fact]
    public void CreateDataObject_WritesOriginalRichTextAndHtmlFormats()
    {
        var snapshot = new ClipboardSnapshot(
            "rich",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text, ClipboardFormatKind.RichText, ClipboardFormatKind.Html],
            text: "Rich text",
            rtf: @"{\rtf1\ansi Rich text}",
            html: "Version:1.0\r\nStartHTML:00000097\r\nEndHTML:00000159\r\nStartFragment:00000129\r\nEndFragment:00000129\r\n<html><body><!--StartFragment--><b>Rich text</b><!--EndFragment--></body></html>");

        var data = ClipboardBridge.CreateDataObject(snapshot, asPlainText: false);

        Assert.True(data.ContainsText(TextDataFormat.UnicodeText));
        Assert.True(data.ContainsText(TextDataFormat.Rtf));
        Assert.True(data.ContainsText(TextDataFormat.Html));
        Assert.Equal("Rich text", data.GetText(TextDataFormat.UnicodeText));
        Assert.Contains(@"{\rtf1", data.GetText(TextDataFormat.Rtf));
    }

    [Fact]
    public void CreateDataObject_WritesFileDropList()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-test-file.txt");
        var snapshot = new ClipboardSnapshot("file", DateTimeOffset.UtcNow, [ClipboardFormatKind.FileDrop], filePaths: [path]);

        var data = ClipboardBridge.CreateDataObject(snapshot, asPlainText: false);

        Assert.Contains(path, data.GetFileDropList().Cast<string>());
    }

    [Fact]
    public void CaptureFrom_ReadsImage()
    {
        byte[] pixels = [0, 128, 255, 255];
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        var data = new DataObject();
        data.SetImage(bitmap);

        var snapshot = ClipboardBridge.CaptureFrom(data);

        Assert.NotNull(snapshot);
        Assert.Contains(ClipboardFormatKind.Image, snapshot.Formats);
        Assert.NotNull(snapshot.ImagePng);
        Assert.NotEmpty(snapshot.ImagePng);
    }

    [Fact]
    public void CreateDataObject_WritesImage()
    {
        byte[] pixels = [0, 128, 255, 255];
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var snapshot = new ClipboardSnapshot("image", DateTimeOffset.UtcNow, [ClipboardFormatKind.Image], imagePng: stream.ToArray());

        var data = ClipboardBridge.CreateDataObject(snapshot, asPlainText: false);

        Assert.NotNull(data.GetImage());
    }

    [Fact]
    public void RoundTrip_PreservesTextFormatsThroughDataObject()
    {
        var original = new ClipboardSnapshot(
            "roundtrip",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text, ClipboardFormatKind.RichText],
            text: "Round trip",
            rtf: @"{\rtf1\ansi Round trip}");

        var captured = ClipboardBridge.CaptureFrom(ClipboardBridge.CreateDataObject(original, asPlainText: false));

        Assert.NotNull(captured);
        Assert.Equal(original.Text, captured.Text);
        Assert.Equal(original.Rtf, captured.Rtf);
    }
}
