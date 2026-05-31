using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SnippetTemplateRendererTests
{
    [Fact]
    public void Render_ExpandsDateAndTimeVariables()
    {
        var now = new DateTimeOffset(2026, 5, 31, 9, 8, 7, TimeSpan.FromHours(9));

        var rendered = SnippetTemplateRenderer.Render("{{date}} {{time}} {{datetime}}", now);

        Assert.Equal("2026-05-31 09:08 2026-05-31 09:08", rendered);
    }

    [Fact]
    public void Render_ExpandsFormattedDateFunctions()
    {
        var now = new DateTimeOffset(2026, 5, 31, 9, 8, 7, TimeSpan.FromHours(9));

        Assert.Equal("20260531", SnippetTemplateRenderer.Render("{{date:yyyyMMdd}}", now));
        Assert.Equal("2026/05/31", SnippetTemplateRenderer.Render("{{date(\"yyyy/MM/dd\")}}", now));
    }

    [Fact]
    public void Render_LeavesUnknownVariablesUnchanged()
    {
        Assert.Equal("Hello {{name}}", SnippetTemplateRenderer.Render("Hello {{name}}"));
    }
}
