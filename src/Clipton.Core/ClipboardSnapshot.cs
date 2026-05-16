using System.Collections.ObjectModel;

namespace Clipton.Core;

public sealed class ClipboardSnapshot
{
    public ClipboardSnapshot(
        string id,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<ClipboardFormatKind> formats,
        string? text = null,
        string? rtf = null,
        string? html = null,
        byte[]? imagePng = null,
        IReadOnlyList<string>? filePaths = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Id is required.", nameof(id)) : id;
        CapturedAt = capturedAt;
        Formats = new ReadOnlyCollection<ClipboardFormatKind>(formats.Distinct().OrderBy(x => x).ToArray());
        Text = text;
        Rtf = rtf;
        Html = html;
        ImagePng = imagePng is null ? null : imagePng.ToArray();
        FilePaths = new ReadOnlyCollection<string>((filePaths ?? Array.Empty<string>()).ToArray());
    }

    public string Id { get; }

    public DateTimeOffset CapturedAt { get; }

    public IReadOnlyList<ClipboardFormatKind> Formats { get; }

    public string? Text { get; }

    public string? Rtf { get; }

    public string? Html { get; }

    public byte[]? ImagePng { get; }

    public IReadOnlyList<string> FilePaths { get; }

    public string Preview
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Text))
            {
                return Text.ReplaceLineEndings(" ").Trim();
            }

            if (FilePaths.Count > 0)
            {
                return string.Join(", ", FilePaths.Select(Path.GetFileName));
            }

            if (ImagePng is { Length: > 0 })
            {
                return "Image";
            }

            if (!string.IsNullOrWhiteSpace(Rtf))
            {
                return "Rich text";
            }

            if (!string.IsNullOrWhiteSpace(Html))
            {
                return "HTML";
            }

            return "Clipboard item";
        }
    }
}
