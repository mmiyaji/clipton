namespace Clipton.Core;

/// <summary>
/// Describes the clipboard formats that Clipton preserves as first-class history data.
/// </summary>
/// <remarks>
/// Keep this enum coarse-grained. UI filtering, previews and persistence depend on stable
/// categories rather than every native Windows clipboard format.
/// </remarks>
public enum ClipboardFormatKind
{
    /// <summary>Plain Unicode text.</summary>
    Text,

    /// <summary>Rich Text Format payload that can be pasted back into rich editors.</summary>
    RichText,

    /// <summary>HTML clipboard payload, including any Windows clipboard fragment metadata.</summary>
    Html,

    /// <summary>Bitmap-like content normalized to PNG while stored in history.</summary>
    Image,

    /// <summary>One or more file paths from a file drop clipboard operation.</summary>
    FileDrop
}
