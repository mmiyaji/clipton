using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Clipton.Core;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using TextDataFormat = System.Windows.TextDataFormat;

namespace Clipton.App;

public static class ClipboardBridge
{
    private const long MaxClipboardImageBytes = 32L * 1024 * 1024;
    private const long MaxClipboardImagePixels = 32_000_000;

    public static ClipboardSnapshot? Capture()
    {
        return WithClipboardRetry(CaptureOnce);
    }

    public static string? GetPlainText(ClipboardSnapshot snapshot)
    {
        return !string.IsNullOrEmpty(snapshot.Text)
            ? snapshot.Text
            : ExtractPlainText(snapshot.Rtf, snapshot.Html);
    }

    public static void Put(ClipboardSnapshot snapshot, bool asPlainText)
    {
        WithClipboardRetry(() =>
        {
            PutOnce(snapshot, asPlainText);
            return true;
        });
    }

    public static void PutText(string text)
    {
        WithClipboardRetry(() =>
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
            return true;
        });
    }

    public static ClipboardSnapshot FromSnippet(Snippet snippet)
    {
        return new ClipboardSnapshot(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, [ClipboardFormatKind.Text], text: snippet.Text);
    }

    private static ClipboardSnapshot? CaptureOnce()
    {
        try
        {
            return CaptureFrom(Clipboard.GetDataObject());
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    // Pure transformation from an in-memory data object; unit-testable without
    // touching the shared system clipboard.
    public static ClipboardSnapshot? CaptureFrom(System.Windows.IDataObject? data)
    {
        if (data is null)
        {
            return null;
        }

        var formats = new List<ClipboardFormatKind>();
        string? text = null;
        string? rtf = null;
        string? html = null;
        byte[]? image = null;
        IReadOnlyList<string>? filePaths = null;

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            text = data.GetData(DataFormats.UnicodeText) as string;
            if (!string.IsNullOrEmpty(text))
            {
                formats.Add(ClipboardFormatKind.Text);
            }
        }

        if (data.GetDataPresent(DataFormats.Rtf))
        {
            rtf = data.GetData(DataFormats.Rtf) as string;
            if (!string.IsNullOrEmpty(rtf))
            {
                formats.Add(ClipboardFormatKind.RichText);
            }
        }

        if (data.GetDataPresent(DataFormats.Html))
        {
            html = data.GetData(DataFormats.Html) as string;
            if (!string.IsNullOrEmpty(html))
            {
                formats.Add(ClipboardFormatKind.Html);
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            text = ExtractPlainText(rtf, html);
            if (!string.IsNullOrEmpty(text))
            {
                formats.Add(ClipboardFormatKind.Text);
            }
        }

        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            filePaths = data.GetData(DataFormats.FileDrop) as string[] ?? [];
            if (filePaths.Count > 0)
            {
                formats.Add(ClipboardFormatKind.FileDrop);
            }
        }

        if (data.GetDataPresent(DataFormats.Bitmap))
        {
            if (data.GetData(DataFormats.Bitmap, autoConvert: true) is BitmapSource bitmap)
            {
                image = EncodePng(bitmap);
                if (image.Length > 0)
                {
                    formats.Add(ClipboardFormatKind.Image);
                }
            }
        }

        if (formats.Count == 0)
        {
            return null;
        }

        return new ClipboardSnapshot(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, formats, text, rtf, html, image, filePaths);
    }

    private static void PutOnce(ClipboardSnapshot snapshot, bool asPlainText)
    {
        Clipboard.SetDataObject(CreateDataObject(snapshot, asPlainText), copy: true);
    }

    // Pure transformation to an in-memory data object; unit-testable without
    // touching the shared system clipboard.
    public static DataObject CreateDataObject(ClipboardSnapshot snapshot, bool asPlainText)
    {
        var data = new DataObject();
        var plainText = GetPlainText(snapshot);
        if (asPlainText && !string.IsNullOrEmpty(plainText))
        {
            data.SetText(plainText, TextDataFormat.UnicodeText);
            return data;
        }

        if (snapshot.FilePaths.Count > 0)
        {
            var collection = new StringCollection();
            collection.AddRange(snapshot.FilePaths.ToArray());
            data.SetFileDropList(collection);
            return data;
        }

        if (snapshot.ImagePng is { Length: > 0 })
        {
            data.SetImage(DecodePng(snapshot.ImagePng));
            return data;
        }

        if (!string.IsNullOrEmpty(snapshot.Text))
        {
            data.SetText(snapshot.Text, TextDataFormat.UnicodeText);
        }

        if (!string.IsNullOrEmpty(snapshot.Rtf))
        {
            data.SetText(snapshot.Rtf, TextDataFormat.Rtf);
        }

        if (!string.IsNullOrEmpty(snapshot.Html))
        {
            data.SetText(snapshot.Html, TextDataFormat.Html);
        }

        return data;
    }

    private static string? ExtractPlainText(string? rtf, string? html)
    {
        return ClipboardTextExtraction.ExtractPlainText(rtf, html);
    }

    private static T? WithClipboardRetry<T>(Func<T?> action)
    {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception exception) when (attempt < maxAttempts - 1 && exception is ExternalException or COMException)
            {
                Thread.Sleep(75);
            }
        }

        return action();
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        if ((long)bitmap.PixelWidth * bitmap.PixelHeight > MaxClipboardImagePixels)
        {
            return [];
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        if (stream.Length > MaxClipboardImageBytes)
        {
            return [];
        }

        return stream.ToArray();
    }

    private static BitmapSource DecodePng(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }
}
