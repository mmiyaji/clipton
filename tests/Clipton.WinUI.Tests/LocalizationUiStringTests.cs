namespace Clipton.WinUI.Tests;

public sealed class LocalizationUiStringTests
{
    [Fact]
    public void QuickMenuWindows_DoNotContainKnownHardcodedEnglishUiText()
    {
        var root = FindRepositoryRoot();
        Assert.DoesNotContain("\"Open with default app\"", Read(root, "src", "Clipton.WinUI", "QuickMenuWindow.cs"));
        Assert.DoesNotContain("\"Copy and remove\"", Read(root, "src", "Clipton.WinUI", "QuickMenuWindow.cs"));
        Assert.DoesNotContain("\"Loading...\"", Read(root, "src", "Clipton.WinUI", "QuickMenuWindow.cs"));
        Assert.DoesNotContain("\"No items\"", Read(root, "src", "Clipton.WinUI", "QuickMenuWindow.cs"));

        var richQuickMenu = Read(root, "src", "Clipton.WinUI", "RichQuickMenuWindow.cs");
        Assert.DoesNotContain("\"Pinned\", HeaderFilter.Pinned", richQuickMenu);
        Assert.DoesNotContain("\"Text\", HeaderFilter.Text", richQuickMenu);
        Assert.DoesNotContain("IconButton(\"\\uE72B\", \"Back\")", richQuickMenu);
        Assert.DoesNotContain("\"Plain text\", InvokeSelectedPlainText", richQuickMenu);
        Assert.DoesNotContain("\"Just now\"", richQuickMenu);
        Assert.DoesNotContain("s ago", richQuickMenu);

        Assert.DoesNotContain("Enter paste  T text  Left/Right", Read(root, "src", "Clipton.App", "QuickMenuWindow.xaml"));
    }

    private static string Read(string root, params string[] parts)
    {
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Clipton.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
