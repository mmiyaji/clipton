using System.Text.RegularExpressions;

namespace Clipton.Core;

public static class SensitiveContentDetector
{
    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CreditCardPattern = new(
        @"\b(?:\d[ -]*?){13,19}\b",
        RegexOptions.Compiled);

    private static readonly Regex SecretKeywordPattern = new(
        @"\b(password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|private[_-]?key|bearer)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LongTokenPattern = new(
        @"\b[A-Za-z0-9_\-]{32,}\b",
        RegexOptions.Compiled);

    private static readonly Regex PhonePattern = new(
        @"(?<!\d)(?:\+?\d{1,3}[-. ]?)?(?:\(?\d{2,4}\)?[-. ]?){2,4}\d{3,4}(?!\d)",
        RegexOptions.Compiled);

    public static bool ShouldMask(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return EmailPattern.IsMatch(text)
            || SecretKeywordPattern.IsMatch(text)
            || LongTokenPattern.IsMatch(text)
            || PhonePattern.IsMatch(text)
            || LooksLikeCreditCard(text);
    }

    private static bool LooksLikeCreditCard(string text)
    {
        foreach (Match match in CreditCardPattern.Matches(text))
        {
            var digits = new string(match.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 13 and <= 19 && PassesLuhn(digits))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                {
                    n -= 9;
                }
            }

            sum += n;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }
}
