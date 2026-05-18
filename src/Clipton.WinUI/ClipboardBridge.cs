using System.Collections.Specialized;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Clipton.Core;
using Forms = System.Windows.Forms;

namespace Clipton.WinUI;

public static class ClipboardBridge
{
    public static ClipboardSnapshot? Capture() => WithClipboardRetry(CaptureOnce);

    public static void Put(ClipboardSnapshot snapshot, bool asPlainText)
    {
        WithClipboardRetry(() =>
        {
            PutOnce(snapshot, asPlainText);
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
        if (asPlainText && !string.IsNullOrEmpty(snapshot.Text))
        {
            Forms.Clipboard.SetText(snapshot.Text, Forms.TextDataFormat.UnicodeText);
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

    private static T? WithClipboardRetry<T>(Func<T?> action)
    {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (ExternalException) when (attempt < maxAttempts - 1)
            {
                Thread.Sleep(75);
            }
        }

        return action();
    }
}
