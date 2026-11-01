using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clipton.WinUI;

/// <summary>
/// Terms, privacy policy, and third-party notices are embedded from the repository's docs folder
/// so the About page reads the same text that is published on the website.
/// </summary>
internal static class LegalDocuments
{
    internal const string Terms = "TERMS";
    internal const string Privacy = "PRIVACY_POLICY";
    internal const string ThirdPartyNotices = "THIRD_PARTY_NOTICES";

    private static readonly Regex Link = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string? Load(string name, string language)
    {
        var assembly = typeof(LegalDocuments).Assembly;
        foreach (var candidate in new[] { language, "en" })
        {
            using var stream = assembly.GetManifestResourceStream($"Legal.{name}.{candidate}.md");
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        return null;
    }

    internal static UIElement BuildView(string markdown)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryHeading(line, out var level, out var headingText))
            {
                var heading = CreateText(headingText, level switch { 1 => 20, 2 => 16, _ => 14 }, FontWeights.SemiBold);
                if (level > 1)
                {
                    heading.Margin = new Thickness(0, 8, 0, 0);
                }

                panel.Children.Add(heading);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var bullet = CreateText("• " + line[2..], 14, FontWeights.Normal);
                bullet.Margin = new Thickness(12, 0, 0, 0);
                panel.Children.Add(bullet);
            }
            else
            {
                panel.Children.Add(CreateText(line, 14, FontWeights.Normal));
            }
        }

        return panel;
    }

    private static bool TryHeading(string line, out int level, out string text)
    {
        level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        if (level is > 0 and <= 6 && level < line.Length && line[level] == ' ')
        {
            text = line[(level + 1)..];
            return true;
        }

        level = 0;
        text = line;
        return false;
    }

    private static TextBlock CreateText(string markdownText, double fontSize, Windows.UI.Text.FontWeight weight)
    {
        var text = Link.Replace(markdownText, "$1").Replace("**", string.Empty).Replace("`", string.Empty);
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
    }
}
