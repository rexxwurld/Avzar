using System.Globalization;
using System.Text;

namespace Tailoring.Domain.Common;

public static class Slug
{
    /// <summary>"Ankara Wrap Dress (2024)" -> "ankara-wrap-dress-2024". Accents are stripped; the result is URL-safe and never empty.</summary>
    public static string From(string? input, string fallback = "item", int maxLength = 60)
    {
        var sb = new StringBuilder();
        var pendingDash = false;
        foreach (var ch in (input ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToLowerInvariant(ch);
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && sb.Length > 0) sb.Append('-');
                pendingDash = false;
                sb.Append(c);
            }
            else pendingDash = true;
        }
        var slug = sb.Length > maxLength ? sb.ToString(0, maxLength).TrimEnd('-') : sb.ToString();
        return slug.Length == 0 ? fallback : slug;
    }
}
