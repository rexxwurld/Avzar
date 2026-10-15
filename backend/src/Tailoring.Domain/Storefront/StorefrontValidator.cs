using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tailoring.Domain.Storefront;

public sealed record ValidationError(string Path, string Message);

/// <summary>What a document references, so the service can check those things exist in THIS tenant.</summary>
public sealed class DocumentCheck
{
    public List<ValidationError> Errors { get; } = new();
    public HashSet<Guid> MediaIds { get; } = new();
    public HashSet<Guid> CollectionIds { get; } = new();
    public HashSet<Guid> CategoryIds { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// The wall between user input and the public site. Pure and deterministic. A document that passes can only contain
/// allow-listed types, variants, fonts, #RRGGBB colours, bounded numbers, https links and plain text without angle brackets.
/// </summary>
public static partial class StorefrontValidator
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex Hex();
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")] private static partial Regex PageSlug();

    public static bool IsHexColor(string? v) => v is not null && Hex().IsMatch(v);

    public static DocumentCheck Validate(StorefrontDocument doc)
    {
        var c = new DocumentCheck();
        void Err(string path, string msg) => c.Errors.Add(new ValidationError(path, msg));

        if (doc.SchemaVersion != StorefrontCatalog.CurrentSchemaVersion)
            Err("schemaVersion", $"Unsupported schema version {doc.SchemaVersion}. Current is {StorefrontCatalog.CurrentSchemaVersion}.");
        var template = StorefrontTemplates.Find(doc.TemplateKey);
        if (template is null) Err("templateKey", $"Unknown template '{doc.TemplateKey}'.");

        ValidateTheme(doc.Theme, Err);
        ValidateBranding(doc.Branding, c, Err);

        // ---- pages
        if (doc.Pages.Count == 0 || doc.Pages.Count > StorefrontCatalog.MaxPages) Err("pages", $"A site needs 1 to {StorefrontCatalog.MaxPages} pages.");
        var pageIds = new HashSet<Guid>();
        var slugs = new HashSet<string>();
        var kinds = new Dictionary<string, int>();
        foreach (var page in doc.Pages)
        {
            if (page.Id == Guid.Empty || !pageIds.Add(page.Id)) Err("pages", "Every page needs a unique id.");
            if (!StorefrontCatalog.PageKinds.Contains(page.Kind)) Err("pages", $"Unknown page kind '{page.Kind}'.");
            kinds[page.Kind] = kinds.GetValueOrDefault(page.Kind) + 1;
        }
        foreach (var k in StorefrontCatalog.SingletonPageKinds.Where(k => kinds.GetValueOrDefault(k) > 1)) Err("pages", $"There can be only one '{k}' page.");
        if (kinds.GetValueOrDefault("home") != 1) Err("pages", "A site needs exactly one home page.");

        var ids = new HashSet<Guid>();
        for (var pi = 0; pi < doc.Pages.Count; pi++)
        {
            var page = doc.Pages[pi];
            var path = $"pages[{pi}]";
            if (page.Kind == "home" ? page.Slug != "" : !(page.Slug.Length is >= 1 and <= 40 && PageSlug().IsMatch(page.Slug)))
                Err($"{path}.slug", page.Kind == "home" ? "The home page has an empty slug." : "A page slug is 1-40 lowercase letters, digits and dashes.");
            else if (StorefrontCatalog.ReservedPageSlugs.Contains(page.Slug)) Err($"{path}.slug", $"'{page.Slug}' is reserved.");
            else if (!slugs.Add(page.Slug)) Err($"{path}.slug", "Two pages share the same slug.");

            Text($"{path}.title", page.Title, 80, true, false, Err);
            Text($"{path}.seo.title", page.Seo.Title, 70, false, false, Err);
            Text($"{path}.seo.description", page.Seo.Description, 160, false, false, Err);
            Media(page.Seo.ShareImageMediaId, c);

            if (page.Sections.Count > StorefrontCatalog.MaxSectionsPerPage) Err($"{path}.sections", $"At most {StorefrontCatalog.MaxSectionsPerPage} sections per page.");
            for (var si = 0; si < page.Sections.Count; si++)
                ValidateSection($"{path}.sections[{si}]", page.Sections[si], template, pageIds, ids, c, requireTemplateSupport: true, Err);
        }

        // ---- archive (sections a template switch could not carry; they only need to be well-formed)
        if (doc.Archive.Count > StorefrontCatalog.MaxArchive) Err("archive", $"At most {StorefrontCatalog.MaxArchive} archived sections.");
        for (var ai = 0; ai < doc.Archive.Count; ai++)
            ValidateSection($"archive[{ai}]", doc.Archive[ai], template, pageIds, ids, c, requireTemplateSupport: false, Err);

        // ---- navigation
        if (doc.Navigation.Count > StorefrontCatalog.MaxNavItems) Err("navigation", $"At most {StorefrontCatalog.MaxNavItems} menu items.");
        var navPages = new HashSet<Guid>();
        for (var ni = 0; ni < doc.Navigation.Count; ni++)
        {
            var n = doc.Navigation[ni];
            Text($"navigation[{ni}].label", n.Label, 30, true, false, Err);
            if (!pageIds.Contains(n.PageId)) Err($"navigation[{ni}].pageId", "The menu item points to a page that does not exist.");
            else if (!navPages.Add(n.PageId)) Err($"navigation[{ni}].pageId", "A page can appear in the menu only once.");
        }
        return c;
    }

    /// <summary>Validates one section on its own (presets). Page links are not resolvable here, only form actions and https links.</summary>
    public static DocumentCheck ValidateSectionAlone(SectionDoc section)
    {
        var c = new DocumentCheck();
        ValidateSection("section", section, null, new HashSet<Guid>(), new HashSet<Guid>(), c, requireTemplateSupport: false, (p, m) => c.Errors.Add(new ValidationError(p, m)));
        return c;
    }

    // ------------------------------------------------------------------ theme + branding

    private static void ValidateTheme(ThemeSettings t, Action<string, string> err)
    {
        if (!StorefrontCatalog.ThemePresets.ContainsKey(t.Preset)) err("theme.preset", $"Unknown theme '{t.Preset}'.");
        foreach (var (key, value) in t.Colors)
        {
            if (!StorefrontCatalog.ThemeColorKeys.Contains(key)) err($"theme.colors.{key}", "Unknown colour name.");
            else if (!IsHexColor(value)) err($"theme.colors.{key}", "Colours must look like #1A2B3C.");
        }
        if (t.HeadingFont is not null && !StorefrontCatalog.Fonts.Contains(t.HeadingFont)) err("theme.headingFont", "That font is not available.");
        if (t.BodyFont is not null && !StorefrontCatalog.Fonts.Contains(t.BodyFont)) err("theme.bodyFont", "That font is not available.");
        if (t.HeadingStyle is not null && !StorefrontCatalog.HeadingStyles.Contains(t.HeadingStyle)) err("theme.headingStyle", "Unknown heading style.");
        if (t.ButtonShape is not null && !StorefrontCatalog.ButtonShapes.Contains(t.ButtonShape)) err("theme.buttonShape", "Unknown button shape.");
        if (t.Radius is < 0 or > 32) err("theme.radius", "Corner radius must be between 0 and 32.");
        if (t.SectionSpacing is not null && !StorefrontCatalog.Spacings.Contains(t.SectionSpacing)) err("theme.sectionSpacing", "Unknown spacing.");
    }

    private static void ValidateBranding(BrandingSettings b, DocumentCheck c, Action<string, string> err)
    {
        Media(b.LogoMediaId, c); Media(b.FaviconMediaId, c); Media(b.ShareImageMediaId, c);
        if (b.LogoWidth is < 40 or > 400) err("branding.logoWidth", "Logo width must be between 40 and 400.");
        if (!StorefrontCatalog.LogoPlacements.Contains(b.LogoPlacement)) err("branding.logoPlacement", "Unknown logo placement.");
        Text("branding.tagline", b.Tagline, 100, false, false, err);
        Text("branding.footerText", b.FooterText, 300, false, true, err);
        if (b.SocialLinks.Count > 6) err("branding.socialLinks", "At most 6 social links.");
        for (var i = 0; i < b.SocialLinks.Count; i++)
        {
            if (!StorefrontCatalog.SocialNetworks.Contains(b.SocialLinks[i].Network)) err($"branding.socialLinks[{i}].network", "Unknown network.");
            if (!IsHttpsUrl(b.SocialLinks[i].Url)) err($"branding.socialLinks[{i}].url", "Social links must be full https:// addresses.");
        }
    }

    // ------------------------------------------------------------------ sections

    private static void ValidateSection(string path, SectionDoc s, TemplateDef? template, HashSet<Guid> pageIds, HashSet<Guid> usedIds,
        DocumentCheck c, bool requireTemplateSupport, Action<string, string> err)
    {
        if (s.Id == Guid.Empty || !usedIds.Add(s.Id)) err($"{path}.id", "Every section needs a unique id.");
        if (!StorefrontCatalog.SectionTypes.TryGetValue(s.Type, out var def)) { err($"{path}.type", $"Unknown section type '{s.Type}'."); return; }

        var allowed = template?.Supported.GetValueOrDefault(s.Type);
        if (requireTemplateSupport && template is not null)
        {
            if (allowed is null) err($"{path}.type", $"The '{template.Name}' template does not support '{def.Label}' sections.");
            else if (!allowed.Contains(s.Variant)) err($"{path}.variant", $"'{s.Variant}' is not available for {def.Label} in this template.");
        }
        else if (!def.AllVariants.Contains(s.Variant)) err($"{path}.variant", $"Unknown layout '{s.Variant}'.");

        ValidateStyle($"{path}.style", s.Style, c, err);
        ValidateObject($"{path}.props", s.Props, def.Props, pageIds, c, err);
    }

    private static void ValidateStyle(string path, SectionStyle st, DocumentCheck c, Action<string, string> err)
    {
        if (st.Background is not null && !IsHexColor(st.Background)) err($"{path}.background", "Colours must look like #1A2B3C.");
        if (st.TextColor is not null && !IsHexColor(st.TextColor)) err($"{path}.textColor", "Colours must look like #1A2B3C.");
        if (!StorefrontCatalog.Aligns.Contains(st.Align)) err($"{path}.align", "Unknown alignment.");
        if (!StorefrontCatalog.Spacings.Contains(st.Spacing)) err($"{path}.spacing", "Unknown spacing.");
        if (st.ButtonShape is not null && !StorefrontCatalog.ButtonShapes.Contains(st.ButtonShape)) err($"{path}.buttonShape", "Unknown button shape.");
        Media(st.BackgroundMediaId, c);
    }

    private static void ValidateObject(string path, JsonElement obj, IReadOnlyList<PropSpec> specs, HashSet<Guid> pageIds, DocumentCheck c, Action<string, string> err)
    {
        if (obj.ValueKind != JsonValueKind.Object) { err(path, "Expected an object."); return; }
        var byName = specs.ToDictionary(p => p.Name);
        foreach (var prop in obj.EnumerateObject())
        {
            if (!byName.TryGetValue(prop.Name, out var spec)) { err($"{path}.{prop.Name}", "Unknown property."); continue; }
            ValidateValue($"{path}.{prop.Name}", spec, prop.Value, pageIds, c, err);
        }
        foreach (var spec in specs.Where(p => p.Required))
            if (!obj.TryGetProperty(spec.Name, out var v) || v.ValueKind == JsonValueKind.Null || (v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString())))
                err($"{path}.{spec.Name}", "This field is required.");
    }

    private static void ValidateValue(string path, PropSpec spec, JsonElement v, HashSet<Guid> pageIds, DocumentCheck c, Action<string, string> err)
    {
        if (v.ValueKind == JsonValueKind.Null) return; // null means "not set"
        switch (spec.Kind)
        {
            case PropKind.Text:
                if (v.ValueKind != JsonValueKind.String) err(path, "Expected text.");
                else Text(path, v.GetString(), spec.Max, false, spec.Multiline, err);
                break;
            case PropKind.Bool:
                if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) err(path, "Expected true or false.");
                break;
            case PropKind.Int:
                if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n)) err(path, "Expected a whole number.");
                else if (n < spec.Min || n > spec.Max) err(path, $"Must be between {spec.Min} and {spec.Max}.");
                break;
            case PropKind.Guid:
                if (v.ValueKind != JsonValueKind.String || !Guid.TryParse(v.GetString(), out var g)) err(path, "Expected an id.");
                else if (spec.Name.EndsWith("MediaId", StringComparison.Ordinal)) c.MediaIds.Add(g);
                else if (spec.Name == "collectionId") c.CollectionIds.Add(g);
                else if (spec.Name == "categoryId") c.CategoryIds.Add(g);
                break;
            case PropKind.Link:
                if (v.ValueKind != JsonValueKind.String) err(path, "Expected a link.");
                else
                {
                    var s = v.GetString()!;
                    var ok = StorefrontCatalog.ActionLinks.Contains(s) || (Guid.TryParse(s, out var pid) && pageIds.Contains(pid)) || IsHttpsUrl(s);
                    if (!ok) err(path, "A link must point to a page of this site, a form, or a full https:// address.");
                }
                break;
            case PropKind.List:
                if (v.ValueKind != JsonValueKind.Array) { err(path, "Expected a list."); break; }
                if (v.GetArrayLength() > spec.Max) err(path, $"At most {spec.Max} items.");
                var i = 0;
                foreach (var item in v.EnumerateArray()) ValidateObject($"{path}[{i++}]", item, spec.Fields!, pageIds, c, err);
                break;
            case PropKind.StringList:
                if (v.ValueKind != JsonValueKind.Array) { err(path, "Expected a list of text."); break; }
                if (v.GetArrayLength() > spec.Max) err(path, $"At most {spec.Max} items.");
                var j = 0;
                foreach (var item in v.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) err($"{path}[{j}]", "Expected text.");
                    else Text($"{path}[{j}]", item.GetString(), spec.ItemMax, false, false, err);
                    j++;
                }
                break;
        }
    }

    // ------------------------------------------------------------------ primitives

    /// <summary>Plain text only. Angle brackets are refused outright so nothing downstream can ever be handed markup.</summary>
    private static void Text(string path, string? value, int max, bool required, bool multiline, Action<string, string> err)
    {
        if (value is null || value.Length == 0)
        {
            if (required) err(path, "This field is required.");
            return;
        }
        if (required && string.IsNullOrWhiteSpace(value)) err(path, "This field is required.");
        if (value.Length > max) err(path, $"At most {max} characters.");
        if (value.Contains('<') || value.Contains('>')) err(path, "The characters < and > are not allowed.");
        if (value.Any(ch => char.IsControl(ch) && !(multiline && ch is '\n' or '\r'))) err(path, "Control characters are not allowed.");
    }

    private static void Media(Guid? id, DocumentCheck c) { if (id is { } g && g != Guid.Empty) c.MediaIds.Add(g); }

    public static bool IsHttpsUrl(string? s)
        => s is { Length: > 8 and <= 200 } && Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
           && string.IsNullOrEmpty(u.UserInfo) && !s.Any(char.IsControl) && !s.Contains(' ') && !s.Contains('<') && !s.Contains('>');
}
