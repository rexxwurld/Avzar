using System.Globalization;

namespace Tailoring.Domain.Storefront;

public sealed record ContrastWarning(string Foreground, string Background, string ForegroundHex, string BackgroundHex, double Ratio, double Required, string SuggestedForegroundHex);

/// <summary>WCAG 2.x contrast. 4.5:1 for body text and buttons, 3:1 for large headings.</summary>
public static class Contrast
{
    public const double NormalText = 4.5;
    public const double LargeText = 3.0;

    private static (double R, double G, double B) Rgb(string hex)
        => (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255.0, int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255.0, int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255.0);

    private static double Lin(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    public static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    public static double Ratio(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static string Blend(string hex, string toward, double t)
    {
        var (r1, g1, b1) = Rgb(hex); var (r2, g2, b2) = Rgb(toward);
        int Ch(double x, double y) => (int)Math.Round((x + (y - x) * t) * 255);
        return $"#{Ch(r1, r2):X2}{Ch(g1, g2):X2}{Ch(b1, b2):X2}";
    }

    /// <summary>The smallest nudge of <paramref name="fg"/> towards black or white that reaches the target. Falls back to pure black or white.</summary>
    public static string SuggestForeground(string fg, string bg, double target = NormalText)
    {
        if (Ratio(fg, bg) >= target) return fg;
        foreach (var step in Enumerable.Range(1, 20).Select(i => i * 0.05))
        {
            var candidates = new[] { Blend(fg, "#000000", step), Blend(fg, "#FFFFFF", step) }
                .Where(h => Ratio(h, bg) >= target).OrderByDescending(h => Ratio(h, bg)).ToList();
            if (candidates.Count > 0) return candidates[0];
        }
        return Ratio("#000000", bg) >= Ratio("#FFFFFF", bg) ? "#000000" : "#FFFFFF";
    }

    private static readonly (string Fg, string Bg, double Min)[] Pairs =
    [
        ("text", "background", NormalText), ("heading", "background", LargeText), ("text", "card", NormalText),
        ("buttonText", "button", NormalText), ("navText", "nav", NormalText), ("footerText", "footer", NormalText),
    ];

    /// <summary>Checks the colours a visitor will actually see (preset plus overrides). Warnings never block saving.</summary>
    public static IReadOnlyList<ContrastWarning> Check(IReadOnlyDictionary<string, string> resolvedColors)
    {
        var warnings = new List<ContrastWarning>();
        foreach (var (fg, bg, min) in Pairs)
        {
            if (!resolvedColors.TryGetValue(fg, out var f) || !resolvedColors.TryGetValue(bg, out var b)) continue;
            var ratio = Ratio(f, b);
            if (ratio < min) warnings.Add(new ContrastWarning(fg, bg, f, b, Math.Round(ratio, 2), min, SuggestForeground(f, b, min)));
        }
        return warnings;
    }
}
