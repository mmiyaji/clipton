using System.Collections.ObjectModel;
using System.Globalization;

namespace Clipton.Core;

/// <summary>
/// Value-style snapshot of one supported clipboard state.
/// </summary>
/// <remarks>
/// The snapshot intentionally stores normalized app metadata alongside the payload, but
/// source metadata is excluded from history fingerprinting so the same content copied
/// from different windows still de-duplicates.
/// </remarks>
public sealed class ClipboardSnapshot
{
    /// <summary>
    /// Creates a snapshot and defensively copies mutable inputs.
    /// </summary>
    /// <param name="id">Stable history identifier. Empty identifiers are rejected.</param>
    /// <param name="capturedAt">Timestamp captured by the app, usually in UTC.</param>
    /// <param name="formats">Supported clipboard formats present in this snapshot.</param>
    /// <param name="text">Plain text payload, when present or derivable.</param>
    /// <param name="rtf">Rich Text Format payload, when available.</param>
    /// <param name="html">HTML clipboard payload, when available.</param>
    /// <param name="imagePng">PNG image bytes, when available.</param>
    /// <param name="filePaths">File drop paths, when available.</param>
    /// <param name="sourceApplicationName">Best-effort application name of the copy source.</param>
    /// <param name="sourceWindowTitle">Best-effort window title of the copy source.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty.</exception>
    public ClipboardSnapshot(
        string id,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<ClipboardFormatKind> formats,
        string? text = null,
        string? rtf = null,
        string? html = null,
        byte[]? imagePng = null,
        IReadOnlyList<string>? filePaths = null,
        string? sourceApplicationName = null,
        string? sourceWindowTitle = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Id is required.", nameof(id)) : id;
        CapturedAt = capturedAt;
        Formats = new ReadOnlyCollection<ClipboardFormatKind>(formats.Distinct().OrderBy(x => x).ToArray());
        Text = text;
        Rtf = rtf;
        Html = html;
        ImagePng = imagePng is null ? null : imagePng.ToArray();
        FilePaths = new ReadOnlyCollection<string>((filePaths ?? Array.Empty<string>()).ToArray());
        SourceApplicationName = NormalizeMetadata(sourceApplicationName);
        SourceWindowTitle = NormalizeMetadata(sourceWindowTitle);
    }

    /// <summary>Stable id used by history, persistence and pinned-item settings.</summary>
    public string Id { get; }

    /// <summary>Time the clipboard state was captured.</summary>
    public DateTimeOffset CapturedAt { get; }

    /// <summary>Sorted, de-duplicated list of coarse clipboard format categories.</summary>
    public IReadOnlyList<ClipboardFormatKind> Formats { get; }

    /// <summary>Plain text payload, when available.</summary>
    public string? Text { get; }

    /// <summary>Rich Text Format payload, when available.</summary>
    public string? Rtf { get; }

    /// <summary>HTML clipboard payload, when available.</summary>
    public string? Html { get; }

    /// <summary>PNG-encoded image bytes, copied defensively by the constructor.</summary>
    public byte[]? ImagePng { get; }

    /// <summary>File paths copied from a file-drop clipboard payload.</summary>
    public IReadOnlyList<string> FilePaths { get; }

    /// <summary>Best-effort source application metadata used only for display.</summary>
    public string? SourceApplicationName { get; }

    /// <summary>Best-effort source window metadata used only for display.</summary>
    public string? SourceWindowTitle { get; }

    /// <summary>
    /// Short fallback preview used by UI paths that do not need localization.
    /// </summary>
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

    private static string? NormalizeMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= 180)
        {
            return normalized;
        }

        var indexes = StringInfo.ParseCombiningCharacters(normalized);
        var boundary = 0;
        foreach (var index in indexes)
        {
            if (index > 180)
            {
                break;
            }

            boundary = index;
        }

        return normalized[..boundary];
    }
}
