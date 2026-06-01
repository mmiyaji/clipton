using Clipton.Core;

namespace Clipton.Core.Tests;

public sealed class InferredJsonFormatterTests
{
    [Fact]
    public void Format_ConvertsSingleCommaPerLineToObject()
    {
        var json = InferredJsonFormatter.Format("name,Clipton\ncount,3");

        Assert.Equal(Normalize("""
{
  "name": "Clipton",
  "count": 3
}
"""), Normalize(json));
    }

    [Fact]
    public void Format_ConvertsSingleTabPerLineToObject()
    {
        var json = InferredJsonFormatter.Format("enabled\ttrue\nnote\thello");

        Assert.Equal(Normalize("""
{
  "enabled": true,
  "note": "hello"
}
"""), Normalize(json));
    }

    [Fact]
    public void Format_FallsBackToJsonStringWhenLineHasMultipleSeparators()
    {
        var json = InferredJsonFormatter.Format("a,b,c");

        Assert.Equal("\"a,b,c\"", json);
    }

    [Fact]
    public void Format_ConvertsPlainLinesToEmptyStringValues()
    {
        var json = InferredJsonFormatter.Format("alpha\nbeta");

        Assert.Equal(Normalize("""
{
  "alpha": "",
  "beta": ""
}
"""), Normalize(json));
    }

    [Fact]
    public void Format_AllowsMixedPlainAndKeyValueLines()
    {
        var json = InferredJsonFormatter.Format("name,Clipton\nmemo");

        Assert.Equal(Normalize("""
{
  "name": "Clipton",
  "memo": ""
}
"""), Normalize(json));
    }

    [Fact]
    public void Format_FormatsExistingJson()
    {
        var json = InferredJsonFormatter.Format("""{"name":"Clipton"}""");

        Assert.Equal(Normalize("""
{
  "name": "Clipton"
}
"""), Normalize(json));
    }

    private static string Normalize(string text) => text.Trim().ReplaceLineEndings("\n");
}
