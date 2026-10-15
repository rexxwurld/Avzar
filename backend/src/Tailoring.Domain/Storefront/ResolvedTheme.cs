namespace Tailoring.Domain.Storefront;

/// <summary>Preset plus the owner's overrides, fully resolved. The public renderer turns this straight into CSS variables.</summary>
public sealed record ResolvedTheme(IReadOnlyDictionary<string, string> Colors, string HeadingFont, string BodyFont,
    string HeadingStyle, string ButtonShape, int Radius, string SectionSpacing)
{
    public static ResolvedTheme From(ThemeSettings t)
    {
        var preset = StorefrontCatalog.ThemePresets.GetValueOrDefault(t.Preset) ?? StorefrontCatalog.ThemePresets["clean-professional"];
        var colors = new Dictionary<string, string>(preset.Colors);
        foreach (var (k, v) in t.Colors.Where(kv => StorefrontCatalog.ThemeColorKeys.Contains(kv.Key) && StorefrontValidator.IsHexColor(kv.Value)))
            colors[k] = v;
        return new ResolvedTheme(colors, t.HeadingFont ?? preset.HeadingFont, t.BodyFont ?? preset.BodyFont,
            t.HeadingStyle ?? preset.HeadingStyle, t.ButtonShape ?? preset.ButtonShape, t.Radius ?? preset.Radius, t.SectionSpacing ?? "comfortable");
    }
}
