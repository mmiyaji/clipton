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
    public void GetPlainText_PrefersExistingTextBeforeExtracting()
    {
        var snapshot = new ClipboardSnapshot(
            "mixed",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text, ClipboardFormatKind.Html],
            text: "Plain",
            html: "Version:1.0\r\nStartHTML:00000097\r\nEndHTML:00000160\r\nStartFragment:00000129\r\nEndFragment:00000129\r\n<html><body><!--StartFragment-->HTML<!--EndFragment--></body></html>");

        Assert.Equal("Plain", ClipboardBridge.GetPlainText(snapshot));
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
    public void CaptureFrom_ReadsFileDropList()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-file-drop.txt");
        var files = new System.Collections.Specialized.StringCollection { path };
        var data = new DataObject();
        data.SetFileDropList(files);

        var snapshot = ClipboardBridge.CaptureFrom(data);

        Assert.NotNull(snapshot);
        Assert.Contains(ClipboardFormatKind.FileDrop, snapshot.Formats);
        Assert.Equal([path], snapshot.FilePaths);
    }

    [Fact]
    public void CaptureFrom_EmptyFileDropListDoesNotAddFileDropFormat()
    {
        var data = new DataObject();
        data.SetText("Text", TextDataFormat.UnicodeText);
        data.SetData(DataFormats.FileDrop, Array.Empty<string>());

        var snapshot = ClipboardBridge.CaptureFrom(data);

        Assert.NotNull(snapshot);
        Assert.Contains(ClipboardFormatKind.Text, snapshot.Formats);
        Assert.DoesNotContain(ClipboardFormatKind.FileDrop, snapshot.Formats);
    }

    [Fact]
    public void CaptureFrom_IgnoresFileDropFormatWhenDataReturnsNullFileArray()
    {
        var snapshot = ClipboardBridge.CaptureFrom(new NullFileDropDataObject());

        Assert.Null(snapshot);
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
    public void CreateDataObject_EmptySnapshotWritesNoFormats()
    {
        var snapshot = new ClipboardSnapshot("empty", DateTimeOffset.UtcNow, []);

        var data = ClipboardBridge.CreateDataObject(snapshot, asPlainText: false);

        Assert.False(data.GetFormats().Any());
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
    public void CreateDataObject_FileDropTakesPrecedenceOverImageAndText()
    {
        var path = Path.Combine(Path.GetTempPath(), "clipton-test-file.txt");
        var image = CreatePngBytes();
        var snapshot = new ClipboardSnapshot(
            "file-first",
            DateTimeOffset.UtcNow,
            [ClipboardFormatKind.Text, ClipboardFormatKind.Image, ClipboardFormatKind.FileDrop],
            text: "Text",
            imagePng: image,
            filePaths: [path]);

        var data = ClipboardBridge.CreateDataObject(snapshot, asPlainText: false);

        Assert.Contains(path, data.GetFileDropList().Cast<string>());
        Assert.False(data.ContainsText());
        Assert.Null(data.GetImage());
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
    public void CaptureFrom_IgnoresBitmapFormatWhenDataIsNotBitmapSource()
    {
        var data = new DataObject();
        data.SetText("Text", TextDataFormat.UnicodeText);
        data.SetData(DataFormats.Bitmap, "not a bitmap");

        var snapshot = ClipboardBridge.CaptureFrom(data);

        Assert.NotNull(snapshot);
        Assert.Contains(ClipboardFormatKind.Text, snapshot.Formats);
        Assert.DoesNotContain(ClipboardFormatKind.Image, snapshot.Formats);
    }

    [Fact]
    public void CaptureFrom_IgnoresBitmapAbovePixelLimit()
    {
        const int width = 8_000;
        const int height = 5_000;
        var stride = (width + 7) / 8;
        var pixels = new byte[stride * height];
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.BlackWhite, null, pixels, stride);
        var data = new DataObject();
        data.SetImage(bitmap);

        var snapshot = ClipboardBridge.CaptureFrom(data);

        Assert.Null(snapshot);
    }

    [Fact]
    public void CreateDataObject_WritesImage()
    {
        var snapshot = new ClipboardSnapshot("image", DateTimeOffset.UtcNow, [ClipboardFormatKind.Image], imagePng: CreatePngBytes());

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

    private sealed class NullFileDropDataObject : System.Windows.IDataObject
    {
        public object? GetData(string format) => null;

        public object? GetData(Type format) => null;

        public object? GetData(string format, bool autoConvert) => null;

        public bool GetDataPresent(string format) => format == DataFormats.FileDrop;

        public bool GetDataPresent(Type format) => false;

        public bool GetDataPresent(string format, bool autoConvert) => format == DataFormats.FileDrop;

        public string[] GetFormats() => [DataFormats.FileDrop];

        public string[] GetFormats(bool autoConvert) => [DataFormats.FileDrop];

        public void SetData(string format, object data)
        {
        }

        public void SetData(Type format, object data)
        {
        }

        public void SetData(string format, object data, bool autoConvert)
        {
        }

        public void SetData(object data)
        {
        }
    }

    private static byte[] CreatePngBytes()
    {
        byte[] pixels = [0, 128, 255, 255];
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
