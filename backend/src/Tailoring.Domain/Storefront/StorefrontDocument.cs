using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tailoring.Domain.Storefront;

/// <summary>
/// The whole storefront as DATA: theme, branding, navigation, pages, sections. It is the only thing the public renderer
/// reads. There is no HTML, CSS or script anywhere in it: every value is validated against <see cref="StorefrontCatalog"/>.
/// </summary>
public sealed class StorefrontDocument
{
    public int SchemaVersion { get; set; } = StorefrontCatalog.CurrentSchemaVersion;
    public string TemplateKey { get; set; } = "standard";
    public ThemeSettings Theme { get; set; } = new();
    public BrandingSettings Branding { get; set; } = new();
    public List<NavItem> Navigation { get; set; } = new();
    public List<PageDoc> Pages { get; set; } = new();
    /// <summary>Sections a template switch could not carry over. Kept, never deleted, and restorable.</summary>
    public List<SectionDoc> Archive { get; set; } = new();
}

public sealed class ThemeSettings
{
    public string Preset { get; set; } = "clean-professional";
    /// <summary>Overrides on top of the preset. Keys from <see cref="StorefrontCatalog.ThemeColorKeys"/>, values #RRGGBB.</summary>
    public Dictionary<string, string> Colors { get; set; } = new();
    /// <summary>Null means "use the preset's". The same goes for the four settings below.</summary>
    public string? HeadingFont { get; set; }
    public string? BodyFont { get; set; }
    public string? HeadingStyle { get; set; }
    public string? ButtonShape { get; set; }
    public int? Radius { get; set; }
    public string? SectionSpacing { get; set; }
}

public sealed class BrandingSettings
{
    public Guid? LogoMediaId { get; set; }
    public int LogoWidth { get; set; } = 120;
    public string LogoPlacement { get; set; } = "left";
    public Guid? FaviconMediaId { get; set; }
    public Guid? ShareImageMediaId { get; set; }
    public string? Tagline { get; set; }
    public string? FooterText { get; set; }
    public List<SocialLink> SocialLinks { get; set; } = new();
}

public sealed class SocialLink
{
    public string Network { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class NavItem
{
    public string Label { get; set; } = "";
    public Guid PageId { get; set; }
    public bool Visible { get; set; } = true;
}

public sealed class SeoSettings
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public Guid? ShareImageMediaId { get; set; }
}

public sealed class PageDoc
{
    public Guid Id { get; set; }
    /// <summary>home | catalog | design | about | services | contact | custom</summary>
    public string Kind { get; set; } = "custom";
    /// <summary>"" for the home page, otherwise a lowercase url segment.</summary>
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Visible { get; set; } = true;
    public SeoSettings Seo { get; set; } = new();
    public List<SectionDoc> Sections { get; set; } = new();
}

public sealed class SectionStyle
{
    public string? Background { get; set; }
    public Guid? BackgroundMediaId { get; set; }
    public string Align { get; set; } = "left";
    public string Spacing { get; set; } = "comfortable";
    public string? TextColor { get; set; }
    public string? ButtonShape { get; set; }
}

public sealed class SectionDoc
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Variant { get; set; } = "";
    public bool Visible { get; set; } = true;
    /// <summary>Validated per section type. Never executed, never rendered as HTML.</summary>
    public JsonElement Props { get; set; } = StorefrontJson.EmptyObject;
    public SectionStyle Style { get; set; } = new();
}

public static class StorefrontJson
{
    public static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>Strict: an unknown property anywhere in a document is an error, not silently ignored.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(StorefrontDocument doc) => JsonSerializer.Serialize(doc, Options);

    public static StorefrontDocument Deserialize(string json)
        => JsonSerializer.Deserialize<StorefrontDocument>(json, Options) ?? throw new JsonException("Empty document.");

    public static StorefrontDocument Clone(StorefrontDocument doc) => Deserialize(Serialize(doc));
}
