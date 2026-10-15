namespace Tailoring.Domain.Storefront;

public enum PropKind { Text, Bool, Int, Guid, Link, List, StringList }

/// <param name="Max">Text: max length. Int: max value. List/StringList: max items.</param>
/// <param name="ItemMax">StringList: max length of each string.</param>
/// <param name="Fields">List: the fields of each item object.</param>
public sealed record PropSpec(string Name, PropKind Kind, int Max = 0, int Min = 0, bool Required = false,
    bool Multiline = false, IReadOnlyList<PropSpec>? Fields = null, int ItemMax = 0);

/// <param name="MinLevel">"basic" sections are core to every plan. "advanced" ones need storefront.section_level of at least advanced.</param>
public sealed record SectionTypeDef(string Type, string Label, string MinLevel, IReadOnlyList<string> AllVariants, IReadOnlyList<PropSpec> Props);

public sealed record ThemePreset(string Key, string Name, IReadOnlyDictionary<string, string> Colors, string HeadingFont, string BodyFont,
    string HeadingStyle, string ButtonShape, int Radius);

/// <summary>Everything a storefront document may contain. Anything not listed here is rejected.</summary>
public static class StorefrontCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxPages = 30;
    public const int MaxSectionsPerPage = 40;
    public const int MaxArchive = 100;
    public const int MaxNavItems = 12;

    /// <summary>Plan levels, lowest first. Used for theme, branding and section entitlements.</summary>
    public static readonly IReadOnlyList<string> Levels = ["none", "basic", "advanced", "full"];

    public static readonly IReadOnlyList<string> ThemeColorKeys =
        ["primary", "secondary", "accent", "background", "text", "heading", "button", "buttonText", "nav", "navText", "footer", "footerText", "card", "border"];

    public static readonly IReadOnlyList<string> Fonts =
    [
        "Playfair Display", "Cormorant Garamond", "DM Serif Display", "Cinzel", "Libre Baskerville", "Abril Fatface", "Lora", "Merriweather",
        "Montserrat", "Poppins", "Inter", "Raleway", "Josefin Sans", "Work Sans", "Nunito", "Lato", "Open Sans", "DM Sans",
        "Source Sans 3", "Bebas Neue", "Oswald", "Space Grotesk",
    ];

    public static readonly IReadOnlyList<string> HeadingStyles = ["normal", "uppercase", "italic"];
    public static readonly IReadOnlyList<string> ButtonShapes = ["square", "rounded", "pill"];
    public static readonly IReadOnlyList<string> Spacings = ["compact", "comfortable", "spacious"];
    public static readonly IReadOnlyList<string> Aligns = ["left", "center", "right"];
    public static readonly IReadOnlyList<string> LogoPlacements = ["left", "center"];
    public static readonly IReadOnlyList<string> SocialNetworks = ["instagram", "facebook", "tiktok", "x", "youtube", "pinterest", "whatsapp", "linkedin"];

    public static readonly IReadOnlyList<string> PageKinds = ["home", "catalog", "design", "about", "services", "contact", "custom"];
    /// <summary>Each of these may appear once. Only "custom" can repeat.</summary>
    public static readonly IReadOnlyList<string> SingletonPageKinds = ["home", "catalog", "design", "about", "services", "contact"];
    public static readonly IReadOnlyList<string> ReservedPageSlugs = ["collection", "api", "public", "_next", "sitemap", "robots", "inquiry", "admin"];
    public static readonly IReadOnlyList<string> ActionLinks = ["action:inquiry", "action:booking", "action:custom-order", "action:contact"];

    private static PropSpec T(string n, int max, bool req = false, bool multi = false) => new(n, PropKind.Text, max, 0, req, multi);
    private static PropSpec B(string n) => new(n, PropKind.Bool);
    private static PropSpec I(string n, int min, int max) => new(n, PropKind.Int, max, min);
    private static PropSpec G(string n) => new(n, PropKind.Guid);
    private static PropSpec Lk(string n) => new(n, PropKind.Link);
    private static PropSpec L(string n, int max, params PropSpec[] fields) => new(n, PropKind.List, max, Fields: fields);
    private static PropSpec SL(string n, int max, int itemMax) => new(n, PropKind.StringList, max, ItemMax: itemMax);

    public static readonly IReadOnlyDictionary<string, SectionTypeDef> SectionTypes = new SectionTypeDef[]
    {
        new("hero", "Hero banner", "basic", ["centered", "split-left", "split-right", "fullbleed", "minimal"],
            [T("headline", 120, true), T("subheadline", 300), G("imageMediaId"), T("buttonLabel", 30), Lk("buttonTarget")]),
        new("designList", "Design listing", "basic", ["grid", "dense-grid", "list", "editorial"],
            [T("heading", 80), G("categoryId"), B("featuredOnly"), I("limit", 1, 48), B("showPrices")]),
        new("about", "About", "basic", ["simple", "split"],
            [T("heading", 80), T("body", 3000, false, true), G("imageMediaId")]),
        new("contactInfo", "Contact information", "basic", ["simple", "split"],
            [T("heading", 80), B("showAddress"), B("showPhone"), B("showEmail"), B("showHours"), T("note", 300)]),
        new("inquiryForm", "Inquiry form", "basic", ["minimal", "split"],
            [T("heading", 80), T("intro", 400, false, true)]),
        new("richText", "Text block", "basic", ["simple", "narrow"],
            [T("heading", 80), T("body", 4000, false, true)]),

        new("gallery", "Image gallery", "advanced", ["grid", "masonry", "carousel", "filmstrip"],
            [T("heading", 80), I("columns", 2, 5), L("images", 24, G("mediaId"), T("alt", 200), T("caption", 100))]),
        new("collection", "Collection", "advanced", ["mosaic", "strip", "lookbook", "cards"],
            [T("heading", 80), G("collectionId"), I("limit", 1, 24), B("showPrices")]),
        new("featuredDesigns", "Featured designs", "advanced", ["editorial", "row", "spotlight"],
            [T("heading", 80), I("limit", 1, 12), B("showPrices")]),
        new("services", "Services", "advanced", ["cards", "list", "columns"],
            [T("heading", 80), L("items", 12, T("title", 60, true), T("description", 300), T("priceText", 40))]),
        new("designerProfile", "Designer profile", "advanced", ["split", "centered", "large"],
            [T("name", 80, true), T("role", 80), T("bio", 1500, false, true), G("imageMediaId")]),
        new("testimonials", "Testimonials", "advanced", ["cards", "quotes", "slider"],
            [T("heading", 80), L("items", 12, T("quote", 400, true), T("author", 60), T("role", 60))]),
        new("pricing", "Pricing", "advanced", ["table", "cards"],
            [T("heading", 80), L("tiers", 6, T("name", 60, true), T("price", 40), SL("features", 8, 80), B("highlight"))]),
        new("faq", "FAQ", "advanced", ["accordion", "two-column"],
            [T("heading", 80), L("items", 20, T("question", 200, true), T("answer", 1000, true, true))]),
        new("booking", "Appointment booking", "advanced", ["simple", "split"],
            [T("heading", 80), T("intro", 400, false, true)]),
        new("customOrderForm", "Custom-order form", "advanced", ["simple", "split"],
            [T("heading", 80), T("intro", 400, false, true)]),
    }.ToDictionary(d => d.Type);

    /// <summary>The section that must be present for a public workflow to be reachable from the site.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> WorkflowEntryPoints = new Dictionary<string, string[]>
    {
        ["inquiry"] = ["inquiryForm"],
        ["booking"] = ["booking"],
        ["customOrder"] = ["customOrderForm"],
        ["contact"] = ["contactInfo", "inquiryForm"],
    };

    private static ThemePreset P(string key, string name, string heading, string body, string hs, string bs, int radius, params (string K, string V)[] colors)
        => new(key, name, colors.ToDictionary(c => c.K, c => c.V), heading, body, hs, bs, radius);

    public static readonly IReadOnlyDictionary<string, ThemePreset> ThemePresets = new[]
    {
        P("modern-luxury", "Modern luxury", "Playfair Display", "Inter", "normal", "square", 0,
            ("primary", "#C9A961"), ("secondary", "#1A1A1D"), ("accent", "#8C6D2F"), ("background", "#0E0E10"), ("text", "#E8E6E1"), ("heading", "#F5F0E6"),
            ("button", "#C9A961"), ("buttonText", "#0E0E10"), ("nav", "#0E0E10"), ("navText", "#F5F0E6"), ("footer", "#151517"), ("footerText", "#CFCBC2"),
            ("card", "#17171A"), ("border", "#2C2C30")),
        P("minimalist", "Minimalist", "Inter", "Inter", "normal", "square", 2,
            ("primary", "#111111"), ("secondary", "#F4F4F4"), ("accent", "#6B6B6B"), ("background", "#FFFFFF"), ("text", "#222222"), ("heading", "#111111"),
            ("button", "#111111"), ("buttonText", "#FFFFFF"), ("nav", "#FFFFFF"), ("navText", "#111111"), ("footer", "#F4F4F4"), ("footerText", "#333333"),
            ("card", "#FAFAFA"), ("border", "#E2E2E2")),
        P("elegant", "Elegant", "Cormorant Garamond", "Lato", "normal", "rounded", 6,
            ("primary", "#7A2E3A"), ("secondary", "#EFE6DA"), ("accent", "#B08D57"), ("background", "#FBF8F3"), ("text", "#3A3330"), ("heading", "#2B2220"),
            ("button", "#7A2E3A"), ("buttonText", "#FFFFFF"), ("nav", "#FBF8F3"), ("navText", "#2B2220"), ("footer", "#2B2220"), ("footerText", "#EFE6DA"),
            ("card", "#FFFFFF"), ("border", "#E4D8C8")),
        P("bold-contemporary", "Bold contemporary", "Oswald", "Work Sans", "uppercase", "square", 0,
            ("primary", "#FF3B30"), ("secondary", "#F2F2F2"), ("accent", "#FFD60A"), ("background", "#FFFFFF"), ("text", "#1B1B1B"), ("heading", "#000000"),
            ("button", "#000000"), ("buttonText", "#FFFFFF"), ("nav", "#000000"), ("navText", "#FFFFFF"), ("footer", "#000000"), ("footerText", "#E6E6E6"),
            ("card", "#F7F7F7"), ("border", "#1B1B1B")),
        P("african-heritage", "Traditional African fashion", "DM Serif Display", "Nunito", "normal", "rounded", 10,
            ("primary", "#B3411B"), ("secondary", "#F3E2C1"), ("accent", "#1F7A5C"), ("background", "#FFF8EC"), ("text", "#3B2A1A"), ("heading", "#5A1E0C"),
            ("button", "#B3411B"), ("buttonText", "#FFFFFF"), ("nav", "#5A1E0C"), ("navText", "#FFF8EC"), ("footer", "#3B2A1A"), ("footerText", "#F3E2C1"),
            ("card", "#FFFFFF"), ("border", "#E5CFA6")),
        P("clean-professional", "Clean professional", "Montserrat", "Open Sans", "normal", "rounded", 8,
            ("primary", "#2B6CB0"), ("secondary", "#EDF2F7"), ("accent", "#38A169"), ("background", "#FFFFFF"), ("text", "#2D3748"), ("heading", "#1A202C"),
            ("button", "#2B6CB0"), ("buttonText", "#FFFFFF"), ("nav", "#FFFFFF"), ("navText", "#1A202C"), ("footer", "#1A202C"), ("footerText", "#E2E8F0"),
            ("card", "#F7FAFC"), ("border", "#E2E8F0")),
    }.ToDictionary(p => p.Key);

    public static int LevelIndex(string level) => Levels.ToList().IndexOf(level);
}
