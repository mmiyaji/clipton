namespace Clipton.Core;

public sealed record Snippet(string Name, string Text, string Folder = "")
{
    public string DisplayName => string.IsNullOrWhiteSpace(Folder) ? Name : $"{Folder.Trim()} / {Name}";
}
