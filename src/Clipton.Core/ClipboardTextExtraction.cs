using System.Net;
using System.Text.RegularExpressions;

namespace Clipton.Core;

public static class ClipboardTextExtraction
{
    public static string? ExtractPlainText(string? rtf, string? html)
    {
        var fromRtf = ExtractPlainTextFromRtf(rtf);
        if (!string.IsNullOrWhiteSpace(fromRtf))
        {
            return fromRtf;
        }

        var fromHtml = ExtractPlainTextFromHtml(html);
        return string.IsNullOrWhiteSpace(fromHtml) ? null : fromHtml;
    }

    public static string? ExtractPlainTextFromRtf(string? rtf)
    {
        if (string.IsNullOrWhiteSpace(rtf))
        {
            return null;
        }

        var text = Regex.Replace(rtf, @"\\'[0-9a-fA-F]{2}", " ");
        text = Regex.Replace(text, @"\\[a-zA-Z]+\d* ?", string.Empty);
        text = Regex.Replace(text, @"[{}]", string.Empty);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    public static string? ExtractPlainTextFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var fragment = ExtractHtmlFragment(html);
        fragment = Regex.Replace(fragment, @"(?is)<\s*(br|/p|/div|/li|/tr|/h[1-6])\b[^>]*>", "\n");
        fragment = Regex.Replace(fragment, @"(?is)<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>", string.Empty);
        fragment = Regex.Replace(fragment, @"(?s)<[^>]+>", string.Empty);
        fragment = WebUtility.HtmlDecode(fragment);
        fragment = Regex.Replace(fragment, @"[ \t\f\v]+", " ");
        fragment = Regex.Replace(fragment, @"\r\n|\r", "\n");
        fragment = Regex.Replace(fragment, @"\n{3,}", "\n\n");
        return fragment.Trim();
    }

    private static string ExtractHtmlFragment(string html)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var start = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
        {
            start += startMarker.Length;
            return html[start..end];
        }

        return html;
    }
}
