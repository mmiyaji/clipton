using System.Collections.Specialized;
using System.Drawing.Imaging;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Clipton.Core;
using Forms = System.Windows.Forms;

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
            Forms.Clipboard.SetText(text, Forms.TextDataFormat.UnicodeText);
            return true;
        });
    }

    public static void PutImagePng(byte[] imagePng)
    {
        WithClipboardRetry(() =>
        {
            using var stream = new MemoryStream(imagePng);
            using var image = System.Drawing.Image.FromStream(stream);
            var data = new Forms.DataObject();
            data.SetData("PNG", false, new MemoryStream(imagePng));
            data.SetImage(new System.Drawing.Bitmap(image));
            Forms.Clipboard.SetDataObject(data, copy: true);
            return true;
        });
    }

    public static void PutImageJpeg(byte[] imagePng)
    {
        WithClipboardRetry(() =>
        {
            var jpeg = EncodeJpeg(imagePng);
            using var stream = new MemoryStream(jpeg);
            using var image = System.Drawing.Image.FromStream(stream);
            var data = new Forms.DataObject();
            data.SetData("JFIF", false, new MemoryStream(jpeg));
            data.SetData("JPEG", false, new MemoryStream(jpeg));
            data.SetImage(new System.Drawing.Bitmap(image));
            Forms.Clipboard.SetDataObject(data, copy: true);
            return true;
        });
    }

    public static void PutFileDrop(string path)
    {
        WithClipboardRetry(() =>
        {
            var files = new StringCollection();
            files.Add(path);
            Forms.Clipboard.SetFileDropList(files);
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
            if (!Forms.Clipboard.ContainsData(Forms.DataFormats.UnicodeText)
                && !Forms.Clipboard.ContainsData(Forms.DataFormats.Rtf)
                && !Forms.Clipboard.ContainsData(Forms.DataFormats.Html)
                && !Forms.Clipboard.ContainsFileDropList()
                && !Forms.Clipboard.ContainsImage())
            {
                return null;
            }

            var formats = new List<ClipboardFormatKind>();
            string? text = null;
            string? rtf = null;
            string? html = null;
            byte[]? image = null;
            IReadOnlyList<string>? filePaths = null;

            if (Forms.Clipboard.ContainsText(Forms.TextDataFormat.UnicodeText))
            {
                text = Forms.Clipboard.GetText(Forms.TextDataFormat.UnicodeText);
                if (!string.IsNullOrEmpty(text)) formats.Add(ClipboardFormatKind.Text);
            }

            if (Forms.Clipboard.ContainsText(Forms.TextDataFormat.Rtf))
            {
                rtf = Forms.Clipboard.GetText(Forms.TextDataFormat.Rtf);
                if (!string.IsNullOrEmpty(rtf)) formats.Add(ClipboardFormatKind.RichText);
            }

            if (Forms.Clipboard.ContainsText(Forms.TextDataFormat.Html))
            {
                html = Forms.Clipboard.GetText(Forms.TextDataFormat.Html);
                if (!string.IsNullOrEmpty(html)) formats.Add(ClipboardFormatKind.Html);
            }

            if (string.IsNullOrEmpty(text))
            {
                text = ExtractPlainText(rtf, html);
                if (!string.IsNullOrEmpty(text)) formats.Add(ClipboardFormatKind.Text);
            }

            if (Forms.Clipboard.ContainsFileDropList())
            {
                filePaths = Forms.Clipboard.GetFileDropList().Cast<string>().ToArray();
                if (filePaths.Count > 0) formats.Add(ClipboardFormatKind.FileDrop);
            }

            if (Forms.Clipboard.ContainsImage())
            {
                var bitmap = Forms.Clipboard.GetImage();
                if (bitmap is not null)
                {
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, ImageFormat.Png);
                    image = stream.ToArray();
                    formats.Add(ClipboardFormatKind.Image);
                }
            }

            return formats.Count == 0
                ? null
                : new ClipboardSnapshot(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, formats, text, rtf, html, image, filePaths);
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static void PutOnce(ClipboardSnapshot snapshot, bool asPlainText)
    {
        var plainText = GetPlainText(snapshot);
        if (asPlainText && !string.IsNullOrEmpty(plainText))
        {
            Forms.Clipboard.SetText(plainText, Forms.TextDataFormat.UnicodeText);
            return;
        }

        if (snapshot.FilePaths.Count > 0)
        {
            var files = new StringCollection();
            files.AddRange(snapshot.FilePaths.ToArray());
            Forms.Clipboard.SetFileDropList(files);
            return;
        }

        if (snapshot.ImagePng is { Length: > 0 })
        {
            using var stream = new MemoryStream(snapshot.ImagePng);
            Forms.Clipboard.SetImage(System.Drawing.Image.FromStream(stream));
            return;
        }

        var data = new Forms.DataObject();
        if (!string.IsNullOrEmpty(snapshot.Text)) data.SetText(snapshot.Text, Forms.TextDataFormat.UnicodeText);
        if (!string.IsNullOrEmpty(snapshot.Rtf)) data.SetText(snapshot.Rtf, Forms.TextDataFormat.Rtf);
        if (!string.IsNullOrEmpty(snapshot.Html)) data.SetText(snapshot.Html, Forms.TextDataFormat.Html);
        Forms.Clipboard.SetDataObject(data, copy: true);
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

        try
        {
            using var box = new Forms.RichTextBox();
            box.Rtf = rtf;
            return box.Text;
        }
        catch (ArgumentException)
        {
            return null;
        }
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
        using var flattened = new System.Drawing.Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
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
}
