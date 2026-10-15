using Tailoring.Domain.Billing;

namespace Tailoring.Domain.Storefront;

/// <summary>Code is "category" or "category:detail" (for example "theme_color:background"), so adding a SECOND advanced item is a new violation.</summary>
public sealed record PolicyViolation(string Code, string Message);

/// <summary>
/// What the owner's PLAN allows a document to contain. Pure: give it a document and the tenant's effective entitlements.
/// Used on every save and publish (the server is the gate; hiding buttons is only a courtesy).
/// </summary>
public static class StorefrontPolicy
{
    private static readonly string[] BasicColors = ["primary", "accent"];

    public static IReadOnlyList<PolicyViolation> Violations(StorefrontDocument doc, EffectiveEntitlements ent)
    {
        var v = new List<PolicyViolation>();
        void Add(string code, string msg) => v.Add(new PolicyViolation(code, msg));

        var template = StorefrontTemplates.Find(doc.TemplateKey);
        if (template is not null && !ent.TierAtLeast(EntitlementKeys.StorefrontTemplateTier, template.Tier))
            Add("template_tier", $"The '{template.Name}' template is not included in your plan.");

        // ---- pages
        var maxPages = ent.Limit(EntitlementKeys.StorefrontPagesMax);
        if (!maxPages.IsUnlimited && doc.Pages.Count > maxPages.Max)
            Add("pages_max", $"Your plan allows {maxPages.Max} page(s); this site has {doc.Pages.Count}.");
        if (doc.Pages.Any(p => p.Kind == "custom") && !ent.Flag(EntitlementKeys.StorefrontCustomPages))
            Add("custom_pages", "Custom pages are not included in your plan.");

        // ---- sections
        var advancedSections = ent.TierAtLeast(EntitlementKeys.StorefrontSectionLevel, "advanced");
        var fullSections = ent.TierAtLeast(EntitlementKeys.StorefrontSectionLevel, "full");
        foreach (var s in doc.Pages.SelectMany(p => p.Sections))
        {
            if (!advancedSections && StorefrontCatalog.SectionTypes.TryGetValue(s.Type, out var def) && def.MinLevel == "advanced")
                Add($"section:{s.Type}", $"'{def.Label}' sections are not included in your plan.");
            if (!advancedSections && (s.Style.Background is not null || s.Style.TextColor is not null || s.Style.BackgroundMediaId is not null))
                Add("section_style", "Section colours and backgrounds are not included in your plan.");
            if (!fullSections && s.Style.ButtonShape is not null)
                Add("section_button_shape", "Per-section button styles are not included in your plan.");
        }

        // ---- theme: none = the template's own look; basic = preset + main/accent colours; advanced = all colours + fonts; full = shape and spacing too
        var t = doc.Theme;
        var themeLevel = StorefrontCatalog.LevelIndex(ent.Tier(EntitlementKeys.StorefrontThemeLevel));
        if (themeLevel < 1 && template is not null && t.Preset != template.DefaultPreset)
            Add("theme_preset", "Choosing a different theme is not included in your plan.");
        foreach (var key in t.Colors.Keys.Where(k => themeLevel < 1 || (themeLevel < 2 && !BasicColors.Contains(k))))
            Add($"theme_color:{key}", themeLevel < 1 ? "Changing theme colours is not included in your plan."
                : "Your plan allows changing the main and accent colours only. Full colour control needs an upgrade.");
        if (themeLevel < 2 && t.HeadingFont is not null) Add("theme_font:heading", "Choosing fonts is not included in your plan.");
        if (themeLevel < 2 && t.BodyFont is not null) Add("theme_font:body", "Choosing fonts is not included in your plan.");
        if (themeLevel < 3)
        {
            if (t.HeadingStyle is not null) Add("theme_style:headingStyle", "Heading styles need a higher plan.");
            if (t.ButtonShape is not null) Add("theme_style:buttonShape", "Button shapes need a higher plan.");
            if (t.Radius is not null) Add("theme_style:radius", "Corner styles need a higher plan.");
            if (t.SectionSpacing is not null) Add("theme_style:sectionSpacing", "Section spacing needs a higher plan.");
        }

        // ---- branding: none = nothing; basic = logo, favicon, footer text; advanced = + share images, social links, logo sizing; full = + tagline
        var b = doc.Branding;
        var brand = StorefrontCatalog.LevelIndex(ent.Tier(EntitlementKeys.StorefrontBrandingLevel));
        if (brand < 1)
        {
            if (b.LogoMediaId is not null) Add("branding_logo", "Branding is not included in your plan.");
            if (b.FaviconMediaId is not null) Add("branding_favicon", "Branding is not included in your plan.");
            if (b.FooterText is not null) Add("branding_footer", "Branding is not included in your plan.");
        }
        if (brand < 2)
        {
            if (b.ShareImageMediaId is not null || doc.Pages.Any(p => p.Seo.ShareImageMediaId is not null)) Add("branding_share", "Share images are not included in your plan.");
            if (b.SocialLinks.Count > 0) Add("branding_social", "Social links are not included in your plan.");
            if (b.LogoWidth != 120) Add("branding_logo_size", "Logo sizing is not included in your plan.");
            if (b.LogoPlacement != "left") Add("branding_logo_placement", "Logo placement is not included in your plan.");
        }
        if (brand < 3 && b.Tagline is not null) Add("branding_tagline", "A tagline is not included in your plan.");

        return v;
    }

    /// <summary>
    /// Violations the new document has that the old one did not. Lets someone on a lower plan still REMOVE advanced content
    /// (and keep what they already have) without being able to ADD more.
    /// </summary>
    public static IReadOnlyList<PolicyViolation> NewViolations(StorefrontDocument next, StorefrontDocument? previous, EffectiveEntitlements ent)
    {
        var after = Violations(next, ent);
        if (previous is null) return after;
        var before = Violations(previous, ent).Select(x => x.Code).ToHashSet();
        return after.Where(x => !before.Contains(x.Code)
                || (x.Code == "pages_max" && next.Pages.Count > previous.Pages.Count)
                || (x.Code == "custom_pages" && next.Pages.Count(p => p.Kind == "custom") > previous.Pages.Count(p => p.Kind == "custom")))
            .ToList();
    }

    /// <summary>Public workflows the owner switched on must stay reachable from the site. Returns the workflows with no visible entry point.</summary>
    public static IReadOnlyList<string> MissingEntryPoints(StorefrontDocument doc, IEnumerable<string> enabledWorkflows)
    {
        var present = doc.Pages.Where(p => p.Visible).SelectMany(p => p.Sections).Where(s => s.Visible).Select(s => s.Type).ToHashSet();
        return enabledWorkflows.Where(w => StorefrontCatalog.WorkflowEntryPoints.TryGetValue(w, out var types) && !types.Any(present.Contains)).ToList();
    }
}
