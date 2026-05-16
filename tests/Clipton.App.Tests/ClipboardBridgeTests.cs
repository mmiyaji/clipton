using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clipton.Core;

namespace Clipton.App.Tests;

public sealed class ClipboardBridgeTests
{
    [Fact]
    public void Capture_ReadsTextRtfAndHtmlFormats()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetText("Plain text", TextDataFormat.UnicodeText);
            data.SetText(@"{\rtf1\ansi Plain text}", TextDataFormat.Rtf);
            data.SetText("Version:1.0\r\nStartHTML:00000097\r\nEndHTML:00000161\r\nStartFragment:00000129\r\nEndFragment:00000129\r\n<html><body><!--StartFragment--><b>Plain text</b><!--EndFragment--></body></html>", TextDataFormat.Html);
            Clipboard.SetDataObject(data, copy: true);

            var snapshot = ClipboardBridge.Capture();

            Assert.NotNull(snapshot);
            Assert.Contains(ClipboardFormatKind.Text, snapshot.Formats);
            Assert.Contains(ClipboardFormatKind.RichText, snapshot.Formats);
            Assert.Contains(ClipboardFormatKind.Html, snapshot.Formats);
            Assert.Equal("Plain text", snapshot.Text);
            Assert.Contains(@"{\rtf1", snapshot.Rtf);
            Assert.Contains("StartHTML", snapshot.Html);
        });
    }

    [Fact]
    public void Put_WritesPlainTextWhenRequested()
    {
        StaTestRunner.Run(() =>
        {
            var snapshot = new ClipboardSnapshot("text", DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: "Hello");

            ClipboardBridge.Put(snapshot, asPlainText: true);

            Assert.Equal("Hello", Clipboard.GetText(TextDataFormat.UnicodeText));
        });
    }

    [Fact]
    public void Put_WritesFileDropList()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), "clipton-test-file.txt");
            File.WriteAllText(path, "clipton");
            var snapshot = new ClipboardSnapshot("file", DateTimeOffset.UtcNow, [ClipboardFormatKind.FileDrop], filePaths: [path]);

            ClipboardBridge.Put(snapshot, asPlainText: false);

            StringCollection files = Clipboard.GetFileDropList();
            Assert.Contains(path, files.Cast<string>());
        });
    }

    [Fact]
    public void Capture_ReadsImage()
    {
        StaTestRunner.Run(() =>
        {
            byte[] pixels = [0, 128, 255, 255];
            var bitmap = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                4);
            Clipboard.SetImage(bitmap);

            var snapshot = ClipboardBridge.Capture();

            Assert.NotNull(snapshot);
            Assert.Contains(ClipboardFormatKind.Image, snapshot.Formats);
            Assert.NotNull(snapshot.ImagePng);
            Assert.NotEmpty(snapshot.ImagePng);
        });
    }
}
