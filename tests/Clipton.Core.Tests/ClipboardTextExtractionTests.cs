using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class ClipboardTextExtractionTests
{
    [Fact]
    public void ExtractPlainText_PrefersRtfOverHtml()
    {
        var text = ClipboardTextExtraction.ExtractPlainText(
            @"{\rtf1\ansi From rtf}",
            "<html><body><b>From html</b></body></html>");

        Assert.Equal("From rtf", text);
    }

    [Fact]
    public void ExtractPlainText_FallsBackToHtmlWhenRtfIsEmpty()
    {
        var text = ClipboardTextExtraction.ExtractPlainText(null, "<html><body><b>From html</b></body></html>");

        Assert.Equal("From html", text);
    }

    [Fact]
    public void ExtractPlainText_ReturnsNullWhenBothMissing()
    {
        Assert.Null(ClipboardTextExtraction.ExtractPlainText(null, null));
        Assert.Null(ClipboardTextExtraction.ExtractPlainText(" ", " "));
    }

    [Fact]
    public void ExtractPlainTextFromRtf_StripsControlWordsAndBraces()
    {
        var text = ClipboardTextExtraction.ExtractPlainTextFromRtf(@"{\rtf1\ansi\deff0 Plain text}");

        Assert.Equal("Plain text", text);
    }

    [Fact]
    public void ExtractPlainTextFromRtf_ReplacesHexEscapes()
    {
        var text = ClipboardTextExtraction.ExtractPlainTextFromRtf(@"{\rtf1 Caf\'e9 mocha}");

        Assert.Equal("Caf mocha", text);
    }

    [Fact]
    public void ExtractPlainTextFromHtml_UsesFragmentMarkers()
    {
        var html = "Version:1.0\r\nStartHTML:00000097\r\nEndHTML:00000161\r\n"
            + "<html><body>ignored<!--StartFragment--><b>Fragment only</b><!--EndFragment-->ignored</body></html>";

        var text = ClipboardTextExtraction.ExtractPlainTextFromHtml(html);

        Assert.Equal("Fragment only", text);
    }

    [Fact]
    public void ExtractPlainTextFromHtml_ConvertsBlockEndingsToNewLines()
    {
        var text = ClipboardTextExtraction.ExtractPlainTextFromHtml("<p>Hello<br>World</p><p>Next</p>");

        Assert.Equal("Hello\nWorld\nNext", text);
    }

    [Fact]
    public void ExtractPlainTextFromHtml_RemovesScriptAndStyleBlocks()
    {
        var text = ClipboardTextExtraction.ExtractPlainTextFromHtml(
            "<div>Visible</div><script>alert(1)</script><style>body{color:red}</style>");

        Assert.Equal("Visible", text);
    }

    [Fact]
    public void ExtractPlainTextFromHtml_DecodesEntities()
    {
        var text = ClipboardTextExtraction.ExtractPlainTextFromHtml("<span>A &amp; B &lt;C&gt;</span>");

        Assert.Equal("A & B <C>", text);
    }

    [Fact]
    public void ExtractPlainTextFromHtml_CollapsesExcessBlankLines()
    {
        var text = ClipboardTextExtraction.ExtractPlainTextFromHtml("Line1<br><br><br><br>Line2");

        Assert.Equal("Line1\n\nLine2", text);
    }
}
