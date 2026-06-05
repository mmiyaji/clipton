using System.Net;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Clipton.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Clipton.WinUI;

public static class ClipboardBridge
{
    public static ClipboardSnapshot? Capture() => WithClipboardRetry(CaptureOnce);

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
            var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            data.SetText(text);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            return true;
        });
    }

    public static void PutImagePng(byte[] imagePng)
    {
        WithClipboardRetry(() =>
        {
            PutBitmap(imagePng);
            return true;
        });
    }

    public static void PutImageJpeg(byte[] imagePng)
    {
        WithClipboardRetry(() =>
        {
            PutBitmap(EncodeJpeg(imagePng));
            return true;
        });
    }

    public static void PutFileDrop(string path)
    {
        WithClipboardRetry(() =>
        {
            PutFiles([path]);
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
            var data = Clipboard.GetContent();
            if (!ContainsAnySupportedFormat(data))
            {
                return null;
            }

            var formats = new List<ClipboardFormatKind>();
            string? text = null;
            string? rtf = null;
            string? html = null;
            byte[]? image = null;
            IReadOnlyList<string>? filePaths = null;

            if (data.Contains(StandardDataFormats.Text))
            {
                text = Wait(data.GetTextAsync);
                if (!string.IsNullOrEmpty(text)) formats.Add(ClipboardFormatKind.Text);
            }

            if (data.Contains(StandardDataFormats.Rtf))
            {
                rtf = Wait(data.GetRtfAsync);
                if (!string.IsNullOrEmpty(rtf)) formats.Add(ClipboardFormatKind.RichText);
            }

            if (data.Contains(StandardDataFormats.Html))
            {
                html = Wait(data.GetHtmlFormatAsync);
                if (!string.IsNullOrEmpty(html)) formats.Add(ClipboardFormatKind.Html);
            }

            if (string.IsNullOrEmpty(text))
            {
                text = ExtractPlainText(rtf, html);
                if (!string.IsNullOrEmpty(text)) formats.Add(ClipboardFormatKind.Text);
            }

            if (data.Contains(StandardDataFormats.StorageItems))
            {
                var storageItems = Wait(data.GetStorageItemsAsync);
                filePaths = storageItems
                    .OfType<StorageFile>()
                    .Select(file => file.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray();
                if (filePaths.Count > 0) formats.Add(ClipboardFormatKind.FileDrop);
            }

            if (data.Contains(StandardDataFormats.Bitmap))
            {
                var bitmap = Wait(data.GetBitmapAsync);
                image = EncodePng(ReadAllBytes(bitmap));
                if (image.Length > 0) formats.Add(ClipboardFormatKind.Image);
            }

            return formats.Count == 0
                ? null
                : new ClipboardSnapshot(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, formats, text, rtf, html, image, filePaths);
        }
        catch (Exception exception) when (exception is ExternalException or COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ContainsAnySupportedFormat(DataPackageView data)
    {
        return data.Contains(StandardDataFormats.Text)
            || data.Contains(StandardDataFormats.Rtf)
            || data.Contains(StandardDataFormats.Html)
            || data.Contains(StandardDataFormats.StorageItems)
            || data.Contains(StandardDataFormats.Bitmap);
    }

    private static void PutOnce(ClipboardSnapshot snapshot, bool asPlainText)
    {
        var plainText = GetPlainText(snapshot);
        if (asPlainText && !string.IsNullOrEmpty(plainText))
        {
            PutText(plainText);
            return;
        }

        if (snapshot.FilePaths.Count > 0)
        {
            PutFiles(snapshot.FilePaths);
            return;
        }

        if (snapshot.ImagePng is { Length: > 0 })
        {
            PutBitmap(snapshot.ImagePng);
            return;
        }

        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        if (!string.IsNullOrEmpty(snapshot.Text)) data.SetText(snapshot.Text);
        if (!string.IsNullOrEmpty(snapshot.Rtf)) data.SetRtf(snapshot.Rtf);
        if (!string.IsNullOrEmpty(snapshot.Html)) data.SetHtmlFormat(snapshot.Html);
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }

    private static void PutFiles(IReadOnlyList<string> paths)
    {
        var files = paths
            .Where(File.Exists)
            .Select(path => Wait(() => StorageFile.GetFileFromPathAsync(path)))
            .ToArray();
        if (files.Length == 0)
        {
            return;
        }

        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.SetStorageItems(files);
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }

    private static void PutBitmap(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        stream.WriteAsync(bytes.AsBuffer()).AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }

    private static byte[] ReadAllBytes(RandomAccessStreamReference reference)
    {
        using var randomAccessStream = Wait(reference.OpenReadAsync);
        using var stream = randomAccessStream.AsStreamForRead();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static string? ExtractPlainText(string? rtf, string? html)
    {
        var fromRtf = ExtractPlainTextFromRtf(rtf);
        if (!string.IsNullOrWhiteSpace(fromRtf))
        {
            return fromRtf;
        }

        var fromHtml = ExtractPlainTextFromHtml(html);
        return string.IsNullOrWhiteSpace(fromHtml) ? null : fromHtml;
    }

    private static string? ExtractPlainTextFromRtf(string? rtf)
    {
        if (string.IsNullOrWhiteSpace(rtf))
        {
            return null;
        }

        var text = Regex.Replace(rtf, @"\\'[0-9a-fA-F]{2}", " ");
        text = Regex.Replace(text, @"\\[a-zA-Z]+\d* ?", string.Empty);
        text = Regex.Replace(text, @"[{}]", string.Empty);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    private static string? ExtractPlainTextFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var fragment = ExtractHtmlFragment(html);
        fragment = Regex.Replace(fragment, @"(?is)<\s*(br|/p|/div|/li|/tr|/h[1-6])\b[^>]*>", "\n");
        fragment = Regex.Replace(fragment, @"(?is)<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>", string.Empty);
        fragment = Regex.Replace(fragment, @"(?s)<[^>]+>", string.Empty);
        fragment = WebUtility.HtmlDecode(fragment);
        fragment = Regex.Replace(fragment, @"[ \t\f\v]+", " ");
        fragment = Regex.Replace(fragment, @"\r\n|\r", "\n");
        fragment = Regex.Replace(fragment, @"\n{3,}", "\n\n");
        return fragment.Trim();
    }

    private static string ExtractHtmlFragment(string html)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var start = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
        {
            start += startMarker.Length;
            return html[start..end];
        }

        return html;
    }

    private static byte[] EncodeJpeg(byte[] imagePng)
    {
        using var input = new MemoryStream(imagePng);
        using var source = System.Drawing.Image.FromStream(input);
        using var flattened = new System.Drawing.Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var graphics = System.Drawing.Graphics.FromImage(flattened))
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        using var output = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(x => x.FormatID == ImageFormat.Jpeg.Guid);
        if (codec is null)
        {
            flattened.Save(output, ImageFormat.Jpeg);
        }
        else
        {
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
            flattened.Save(output, codec, parameters);
        }

        return output.ToArray();
    }

    private static byte[] EncodePng(byte[] imageBytes)
    {
        try
        {
            using var input = new MemoryStream(imageBytes);
            using var source = System.Drawing.Image.FromStream(input);
            using var output = new MemoryStream();
            source.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException)
        {
            return imageBytes;
        }
    }

    private static T Wait<T>(Func<IAsyncOperation<T>> action)
    {
        return action().AsTask().GetAwaiter().GetResult();
    }

    private static void Wait(Func<IAsyncAction> action)
    {
        action().AsTask().GetAwaiter().GetResult();
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
            catch (Exception exception) when (attempt < maxAttempts - 1 && exception is ExternalException or COMException or UnauthorizedAccessException)
            {
                Thread.Sleep(75);
            }
        }

        return action();
    }
}
