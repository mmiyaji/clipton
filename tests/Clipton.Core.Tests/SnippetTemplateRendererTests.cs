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
    public void Render_ExpandsAdditionalDateAndTimeFunctions()
    {
        var now = new DateTimeOffset(2026, 5, 31, 9, 8, 7, TimeSpan.FromHours(9));

        var rendered = SnippetTemplateRenderer.Render(
            "{{tomorrow}} {{yesterday}} {{quarter}} {{week}} {{timezone}} {{utcdate}} {{utctime}} {{unix}} {{unixms}}",
            now);

        Assert.Equal("2026-06-01 2026-05-30 2 22 +09:00 2026-05-31 00:08 1780186087 1780186087000", rendered);
    }

    [Fact]
    public void Render_ExpandsDateOffsetFunctions()
    {
        var now = new DateTimeOffset(2026, 5, 31, 9, 8, 7, TimeSpan.FromHours(9));

        var rendered = SnippetTemplateRenderer.Render(
            "{{adddays:3}} {{adddays:-7|yyyy/MM/dd}} {{addmonths:1}} {{addyears:-1}} {{addhours:2}} {{addminutes:30|HH:mm}}",
            now);

        Assert.Equal("2026-06-03 2026/05/24 2026-06-30 2025-05-31 2026-05-31 11:08 09:38", rendered);
    }

    [Fact]
    public void Render_ExpandsRandomFunctionsWithRequestedLength()
    {
        var rendered = SnippetTemplateRenderer.Render("{{shortuuid}} {{randomhex:12}} {{randomnumber:4}}");
        var parts = rendered.Split(' ');

        Assert.Matches("^[0-9a-f]{8}$", parts[0]);
        Assert.Matches("^[0-9a-f]{12}$", parts[1]);
        Assert.Matches("^[0-9]{4}$", parts[2]);
    }

    [Fact]
    public void Render_LeavesUnknownVariablesUnchanged()
    {
        Assert.Equal("Hello {{name}}", SnippetTemplateRenderer.Render("Hello {{name}}"));
    }
}
