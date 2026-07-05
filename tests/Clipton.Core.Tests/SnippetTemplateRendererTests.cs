using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class SnippetTemplateRendererTests
{
    [Fact]
    public void Render_ReturnsEmptyTemplateUnchanged()
    {
        Assert.Equal(string.Empty, SnippetTemplateRenderer.Render(string.Empty));
    }

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
    public void Render_ExpandsIsoUtcAliasesAndCalendarParts()
    {
        var now = new DateTimeOffset(2026, 5, 31, 9, 8, 7, TimeSpan.FromHours(9));

        var rendered = SnippetTemplateRenderer.Render(
            "{{now}}|{{utcdatetime}}|{{utcnow}}|{{isodate}}|{{isodatetime}}|{{isoutc}}|{{year}}|{{month}}|{{day}}|{{isoweek}}|{{weekday:yyyy}}",
            now);

        Assert.Equal(
            "2026-05-31 09:08|2026-05-31 00:08|2026-05-31 00:08|2026-05-31|2026-05-31T09:08:07.0000000+09:00|2026-05-31T00:08:07.0000000+00:00|2026|05|31|22|2026",
            rendered);
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
    public void Render_LeavesInvalidDateOffsetsUnchanged()
    {
        var now = new DateTimeOffset(2026, 5, 31, 9, 8, 7, TimeSpan.FromHours(9));

        var rendered = SnippetTemplateRenderer.Render(
            "{{adddays:x}} {{addmonths:x}} {{addyears:x}} {{addhours:x}} {{addminutes:x}}",
            now);

        Assert.Equal("{{adddays:x}} {{addmonths:x}} {{addyears:x}} {{addhours:x}} {{addminutes:x}}", rendered);
    }

    [Fact]
    public void Render_LeavesInvalidDateFormatsUnchanged()
    {
        var now = new DateTimeOffset(2026, 5, 31, 9, 8, 7, TimeSpan.FromHours(9));

        var rendered = SnippetTemplateRenderer.Render("{{date:%}} {{date:'yyyy}} {{adddays:3|%}} {{weekday:%}}", now);

        Assert.Equal("{{date:%}} {{date:'yyyy}} {{adddays:3|%}} {{weekday:%}}", rendered);
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
    public void Render_RandomFunctionsFallbackAndClampLengths()
    {
        var rendered = SnippetTemplateRenderer.Render(
            "{{randomhex:bad}} {{randomhex:0}} {{randomhex:65}} {{randomnumber:bad}} {{randomnumber:0}} {{randomnumber:10}}");
        var parts = rendered.Split(' ');

        Assert.Matches("^[0-9a-f]{8}$", parts[0]);
        Assert.Matches("^[0-9a-f]{1}$", parts[1]);
        Assert.Matches("^[0-9a-f]{64}$", parts[2]);
        Assert.Matches("^[0-9]{6}$", parts[3]);
        Assert.Matches("^[0-9]{1}$", parts[4]);
        Assert.Matches("^[0-9]{9}$", parts[5]);
    }

    [Fact]
    public void Render_ExpandsGuidUuidAndRandomAliases()
    {
        var rendered = SnippetTemplateRenderer.Render("{{guid}} {{uuid}} {{random:5}}");
        var parts = rendered.Split(' ');

        Assert.True(Guid.TryParse(parts[0], out _));
        Assert.True(Guid.TryParse(parts[1], out _));
        Assert.Matches("^[0-9a-f]{5}$", parts[2]);
    }

    [Fact]
    public void Render_ExpandsFileVariables()
    {
        var files = new[]
        {
            @"C:\Work\Report.md",
            @"C:\Work\Notes.txt"
        };

        Assert.Equal(@"C:\Work\Report.md" + Environment.NewLine + @"C:\Work\Notes.txt", SnippetTemplateRenderer.Render("{{filepaths}}", filePaths: files));
        Assert.Equal("Report.md, Notes.txt", SnippetTemplateRenderer.Render("{{filenames:\", \"}}", filePaths: files));
        Assert.Equal("Report|Notes", SnippetTemplateRenderer.Render("{{filestems:|}}", filePaths: files));
        Assert.Equal(".md/.txt", SnippetTemplateRenderer.Render("{{fileextensions:/}}", filePaths: files));
        Assert.Equal(@"C:\Work", SnippetTemplateRenderer.Render("{{filedirectory}}", filePaths: files[..1]));
        Assert.Equal("2", SnippetTemplateRenderer.Render("{{filecount}}", filePaths: files));
    }

    [Fact]
    public void Render_ExpandsSingularFileVariableAliases()
    {
        var files = new[]
        {
            @"C:\Work\Report.md"
        };

        Assert.Equal(@"C:\Work\Report.md", SnippetTemplateRenderer.Render("{{filepath}}", filePaths: files));
        Assert.Equal("Report.md", SnippetTemplateRenderer.Render("{{filename}}", filePaths: files));
        Assert.Equal("Report", SnippetTemplateRenderer.Render("{{filenamewithoutextension}}", filePaths: files));
        Assert.Equal(".md", SnippetTemplateRenderer.Render("{{fileextension}}", filePaths: files));
        Assert.Equal(@"C:\Work", SnippetTemplateRenderer.Render("{{filedirectories}}", filePaths: files));
    }

    [Fact]
    public void Render_FileVariablesHandleNoFilesAliasesAndEscapedSeparators()
    {
        var files = new[]
        {
            @"C:\Work\Report.md",
            @"C:\Work\Notes.txt"
        };

        Assert.Equal(string.Empty, SnippetTemplateRenderer.Render("{{filepath}}"));
        Assert.Equal("0", SnippetTemplateRenderer.Render("{{filecount}}"));
        Assert.Equal("Report.md" + Environment.NewLine + "Notes.txt", SnippetTemplateRenderer.Render("{{filenames:\\n}}", filePaths: files));
        Assert.Equal("Report\tNotes", SnippetTemplateRenderer.Render("{{filenamewithoutextension:\\t}}", filePaths: files));
        Assert.Equal("Report/Notes", SnippetTemplateRenderer.Render("{{filestems:/}}", filePaths: files));
        Assert.Equal("Report", SnippetTemplateRenderer.Render("{{filestem}}", filePaths: files[..1]));
        Assert.Equal(".md/.txt", SnippetTemplateRenderer.Render("{{fileextensions:/}}", filePaths: files));
        Assert.Equal(string.Empty, SnippetTemplateRenderer.Render("{{filedirectory}}", filePaths: ["relative"]));
        Assert.Equal(string.Empty, SnippetTemplateRenderer.Render("{{filedirectory}}", filePaths: [string.Empty]));
    }

    [Fact]
    public void Render_FileVariablesIgnoreInvalidPaths()
    {
        var files = new[] { "bad\0name.txt" };

        Assert.Equal(string.Empty, SnippetTemplateRenderer.Render("{{filename}}", filePaths: files));
        Assert.Equal(string.Empty, SnippetTemplateRenderer.Render("{{filedirectory}}", filePaths: files));
    }

    [Fact]
    public void Render_UsesDefaultWeekdayFormatWhenNoFormatIsProvided()
    {
        var now = new DateTimeOffset(2026, 5, 31, 9, 8, 7, TimeSpan.FromHours(9));

        Assert.False(string.IsNullOrWhiteSpace(SnippetTemplateRenderer.Render("{{weekday}}", now)));
    }

    [Fact]
    public void Render_ExpandsLineBreakAliases()
    {
        Assert.Equal("a" + Environment.NewLine + Environment.NewLine + "b", SnippetTemplateRenderer.Render("a{{br}}{{newline}}b"));
    }

    [Fact]
    public void Render_LeavesUnknownVariablesUnchanged()
    {
        Assert.Equal("Hello {{name}}", SnippetTemplateRenderer.Render("Hello {{name}}"));
    }

    [Theory]
    [InlineData("plain text", false)]
    [InlineData("{{date}} {{randomhex:8}}", false)]
    [InlineData("{{filename}}", true)]
    [InlineData("{{filepaths:\", \"}}", true)]
    [InlineData("Copied {{filecount}} file(s)", true)]
    public void RequiresFilePaths_DetectsOnlyFileVariables(string template, bool expected)
    {
        Assert.Equal(expected, SnippetTemplateRenderer.RequiresFilePaths(template));
    }
}
