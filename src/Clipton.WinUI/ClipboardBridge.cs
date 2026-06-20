using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Clipton.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Clipton.WinUI;

/// <summary>
/// Converts between WinRT clipboard data packages and Clipton core snapshots.
/// </summary>
/// <remarks>
/// Clipboard access is inherently best-effort because other processes can hold the
/// clipboard open. Public operations therefore retry transient COM/clipboard failures and
/// return no data instead of surfacing clipboard contention to UI code.
/// </remarks>
public static class ClipboardBridge
{
    // Bound image reads before decoding so a single clipboard bitmap cannot dominate
    // memory or UI responsiveness.
    private const long MaxClipboardImageSourceBytes = 32L * 1024 * 1024;
    private const long MaxClipboardImagePixels = 32_000_000;

    /// <summary>
    /// Captures the current clipboard into a supported snapshot, or <see langword="null"/>.
    /// </summary>
    public static ClipboardSnapshot? Capture() => WithClipboardRetry(CaptureOnce);

    /// <summary>
    /// Returns plain text from a snapshot, deriving it from rich formats when needed.
    /// </summary>
    public static string? GetPlainText(ClipboardSnapshot snapshot)
    {
        return !string.IsNullOrEmpty(snapshot.Text)
            ? snapshot.Text
            : ExtractPlainText(snapshot.Rtf, snapshot.Html);
    }

    /// <summary>
    /// Places a snapshot back on the clipboard, optionally reducing it to plain text.
    /// </summary>
    public static void Put(ClipboardSnapshot snapshot, bool asPlainText)
    {
        WithClipboardRetry(() =>
        {
            PutOnce(snapshot, asPlainText);
            return true;
        });
    }

    /// <summary>Places plain text on the clipboard.</summary>
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

    /// <summary>Places PNG image bytes on the clipboard as a bitmap.</summary>
    public static void PutImagePng(byte[] imagePng)
    {
        WithClipboardRetry(() =>
        {
            PutBitmap(imagePng);
            return true;
        });
    }

    /// <summary>Converts PNG bytes to JPEG and places the result on the clipboard.</summary>
    public static void PutImageJpeg(byte[] imagePng)
    {
        WithClipboardRetry(() =>
        {
            PutBitmap(EncodeJpeg(imagePng));
            return true;
        });
    }

    /// <summary>Places one existing file path on the clipboard as a file drop.</summary>
    public static void PutFileDrop(string path)
    {
        WithClipboardRetry(() =>
        {
            PutFiles([path]);
            return true;
        });
    }

    /// <summary>
    /// Creates a text snapshot from a snippet without touching the system clipboard.
    /// </summary>
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
                try
                {
                    var bitmap = Wait(data.GetBitmapAsync);
                    image = EncodePng(ReadAllBytes(bitmap, MaxClipboardImageSourceBytes));
                    if (image.Length > 0) formats.Add(ClipboardFormatKind.Image);
                }
                catch (Exception exception) when (exception is ExternalException or COMException or UnauthorizedAccessException or IOException)
                {
                    AppDiagnostics.Log(exception, "Clipboard image capture");
                }
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

        // File drops are restored through StorageItems; text and bitmap formats can be
        // combined in one package so mixed Office payloads keep both representations.
        if (snapshot.FilePaths.Count > 0)
        {
            PutFiles(snapshot.FilePaths);
            return;
        }

        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        var hasContent = false;
        if (!string.IsNullOrEmpty(snapshot.Text))
        {
            data.SetText(snapshot.Text);
            hasContent = true;
        }

        if (!string.IsNullOrEmpty(snapshot.Rtf))
        {
            data.SetRtf(snapshot.Rtf);
            hasContent = true;
        }

        if (!string.IsNullOrEmpty(snapshot.Html))
        {
            data.SetHtmlFormat(snapshot.Html);
            hasContent = true;
        }

        if (snapshot.ImagePng is { Length: > 0 })
        {
            SetBitmap(data, snapshot.ImagePng);
            hasContent = true;
        }

        if (!hasContent)
        {
            return;
        }

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
        var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        SetBitmap(data, bytes);
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }

    private static void SetBitmap(DataPackage data, byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        stream.WriteAsync(bytes.AsBuffer()).AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
    }

    private static byte[] ReadAllBytes(RandomAccessStreamReference reference, long maxBytes)
    {
        using var randomAccessStream = Wait(reference.OpenReadAsync);
        if (randomAccessStream.Size > (ulong)maxBytes)
        {
            AppDiagnostics.Info("Clipboard", $"Skipped clipboard image because the source stream was {randomAccessStream.Size} bytes.");
            return [];
        }

        using var stream = randomAccessStream.AsStreamForRead();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > maxBytes)
            {
                AppDiagnostics.Info("Clipboard", $"Skipped clipboard image because the source stream exceeded {maxBytes} bytes.");
                return [];
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static string? ExtractPlainText(string? rtf, string? html)
    {
        return ClipboardTextExtraction.ExtractPlainText(rtf, html);
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
        if (imageBytes.Length == 0)
        {
            return [];
        }

        try
        {
            using var input = new MemoryStream(imageBytes);
            using var source = System.Drawing.Image.FromStream(input);
            if ((long)source.Width * source.Height > MaxClipboardImagePixels)
            {
                AppDiagnostics.Info("Clipboard", $"Skipped clipboard image because its dimensions were {source.Width}x{source.Height}.");
                return [];
            }

            using var output = new MemoryStream();
            source.Save(output, ImageFormat.Png);
            if (output.Length > MaxClipboardImageSourceBytes)
            {
                AppDiagnostics.Info("Clipboard", $"Skipped clipboard image because the encoded PNG was {output.Length} bytes.");
                return [];
            }

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

        // One final attempt outside the retry filter records exhausted failures while
        // still letting successful late clipboard releases complete normally.
        try
        {
            return action();
        }
        catch (Exception exception) when (exception is ExternalException or COMException or UnauthorizedAccessException)
        {
            AppDiagnostics.Log(exception, "Clipboard access retries exhausted");
            return default;
        }
    }
}
