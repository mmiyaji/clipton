namespace Clipton.Core;

/// <summary>
/// Reusable text entry that can be pasted directly or rendered as a template.
/// </summary>
/// <param name="Name">User-visible snippet name within its folder.</param>
/// <param name="Text">Snippet body. Template variables are expanded at paste time.</param>
/// <param name="Folder">Optional slash-delimited folder path.</param>
public sealed record Snippet(string Name, string Text, string Folder = "")
{
    /// <summary>Combined folder/name label for list UIs.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Folder) ? Name : $"{Folder.Trim()} / {Name}";
}
