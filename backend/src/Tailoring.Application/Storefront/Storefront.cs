using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Tailoring.Application.Common;
using Tailoring.Domain.Storefront;

namespace Tailoring.Application.Storefront;

// ------------------------------------------------------------------ owner side

public sealed record TemplateInfoDto(string Key, string Name, string Description, string Tier, string DefaultPreset, bool Allowed,
    IReadOnlyDictionary<string, string[]> Supported);
public sealed record ThemePresetDto(string Key, string Name, IReadOnlyDictionary<string, string> Colors, string HeadingFont, string BodyFont);
/// <summary>Describes one editable field so the builder can generate its form from data. Kind: Text | Bool | Int | Guid | Link | List | StringList.</summary>
public sealed record PropSpecDto(string Name, string Kind, int Max, int Min, bool Required, bool Multiline, int ItemMax, IReadOnlyList<PropSpecDto>? Fields);
public sealed record SectionTypeDto(string Type, string Label, string MinLevel, IReadOnlyList<string> Variants, bool Allowed, IReadOnlyList<PropSpecDto> Props);
public sealed record BuilderCatalogDto(IReadOnlyList<TemplateInfoDto> Templates, IReadOnlyList<ThemePresetDto> Themes, IReadOnlyList<string> Fonts,
    IReadOnlyList<SectionTypeDto> SectionTypes, IReadOnlyList<SectionPresetDto> Presets, BuilderCapabilitiesDto Capabilities);

/// <summary>What this plan lets the signed-in business do. The UI uses it to enable controls; the server enforces it regardless.</summary>
public sealed record BuilderCapabilitiesDto(bool Builder, string ThemeLevel, string BrandingLevel, string SectionLevel, string TemplateTier,
    bool CustomPages, bool CustomDomain, int? MaxPages, int? MaxStorefronts);

public sealed record StorefrontSummaryDto(Guid Id, string Name, string Slug, string Status, string TemplateKey, int? PublishedVersion,
    int DraftRevision, bool HasUnpublishedChanges, DateTime UpdatedAtUtc);

public sealed record StorefrontDto(Guid Id, string Name, string Slug, string Status, string TemplateKey, int? PublishedVersion, int DraftRevision,
    bool HasUnpublishedChanges, string? ContactEmail, string? ContactPhone, string? Address, string? OpeningHours,
    bool WorkflowInquiry, bool WorkflowBooking, bool WorkflowCustomOrder, bool WorkflowContact, bool AllowSearchIndexing, uint Version, string PublicUrlPath);

public sealed record CreateStorefrontRequest
{
    [Required, StringLength(80, MinimumLength = 2)] public string Name { get; init; } = "";
    /// <summary>Subdomain: 3-40 lowercase letters, digits and dashes. Derived from the name when omitted.</summary>
    [StringLength(40)] public string? Slug { get; init; }
    public string TemplateKey { get; init; } = "standard";
}

public sealed record UpdateStorefrontSettingsRequest
{
    [Required] public uint? Version { get; init; }
    [Required, StringLength(80, MinimumLength = 2)] public string Name { get; init; } = "";
    [EmailAddress, StringLength(120)] public string? ContactEmail { get; init; }
    [StringLength(40)] public string? ContactPhone { get; init; }
    [StringLength(300)] public string? Address { get; init; }
    [StringLength(300)] public string? OpeningHours { get; init; }
    public bool WorkflowInquiry { get; init; } = true;
    public bool WorkflowBooking { get; init; }
    public bool WorkflowCustomOrder { get; init; }
    public bool WorkflowContact { get; init; } = true;
    public bool AllowSearchIndexing { get; init; } = true;
}

public sealed record ContrastWarningDto(string Foreground, string Background, string ForegroundHex, string BackgroundHex, double Ratio, double Required, string SuggestedForegroundHex);
public sealed record DocIssueDto(string Path, string Message);
public sealed record PolicyIssueDto(string Code, string Message);

public sealed record DraftDto(Guid StorefrontId, int Revision, JsonElement Document, ResolvedTheme Theme,
    IReadOnlyList<ContrastWarningDto> ContrastWarnings, IReadOnlyList<PolicyIssueDto> PlanIssues);

/// <summary>Document is raw JSON so it can be parsed STRICTLY (unknown properties are errors, not ignored).</summary>
public sealed record SaveDraftRequest
{
    [Required] public JsonElement Document { get; init; }
    /// <summary>The revision the editor loaded. A stale value is refused so edits are never silently overwritten.</summary>
    [Required] public int? ExpectedRevision { get; init; }
}

public sealed record SaveAppearanceRequest
{
    [Required] public JsonElement Theme { get; init; }
    [Required] public JsonElement Branding { get; init; }
    [Required] public int? ExpectedRevision { get; init; }
}

public sealed record SwitchChangeDto(Guid SectionId, string Type, string? FromVariant, string? ToVariant, string Page);
public sealed record SwitchPlanDto(string FromTemplate, string ToTemplate, bool NeedsConfirmation, IReadOnlyList<SwitchChangeDto> Kept,
    IReadOnlyList<SwitchChangeDto> Converted, IReadOnlyList<SwitchChangeDto> Archived, IReadOnlyList<SwitchChangeDto> Restored, string Token);

public sealed record ApplySwitchRequest
{
    [Required] public string TargetTemplate { get; init; } = "";
    /// <summary>The token from the preview. If the draft changed since, the plan differs and the switch is refused.</summary>
    [Required] public string Token { get; init; } = "";
    public bool UseTemplateTheme { get; init; }
    [Required] public int? ExpectedRevision { get; init; }
}

public sealed record PublishRequest { [StringLength(200)] public string? Note { get; init; } }

public sealed record VersionDto(int VersionNumber, string TemplateKey, string? Note, DateTime PublishedAtUtc, Guid? PublishedByUserId,
    int? RolledBackFromVersion, bool IsLive);

public sealed record PublishResultDto(VersionDto Version, IReadOnlyList<ContrastWarningDto> ContrastWarnings);

public sealed record SectionPresetDto(string Key, string Name, string SectionType, string Variant, JsonElement Props, JsonElement Style, bool IsBuiltIn);

public sealed record CreatePresetRequest
{
    [Required, StringLength(60, MinimumLength = 2)] public string Name { get; init; } = "";
    [Required] public string SectionType { get; init; } = "";
    [Required] public string Variant { get; init; } = "";
    [Required] public JsonElement Props { get; init; }
    public JsonElement? Style { get; init; }
}

public sealed record DomainDto(Guid Id, string Hostname, string Status, string VerificationRecordName, string VerificationRecordValue,
    DateTime? VerifiedAtUtc, DateTime? LastCheckedAtUtc, string? LastError);

public sealed record AddDomainRequest { [Required, StringLength(253, MinimumLength = 4)] public string Hostname { get; init; } = ""; }

public sealed record PreviewLinkDto(string Token, DateTime ExpiresAtUtc);

public interface IStorefrontService
{
    Task<BuilderCatalogDto> CatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StorefrontSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<StorefrontDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<StorefrontDto> CreateAsync(CreateStorefrontRequest request, CancellationToken ct = default);
    Task<StorefrontDto> UpdateSettingsAsync(Guid id, UpdateStorefrontSettingsRequest request, CancellationToken ct = default);

    Task<DraftDto> GetDraftAsync(Guid id, CancellationToken ct = default);
    /// <summary>Needs the visual builder. Rejects anything outside the allow-lists or the plan; changes nothing live.</summary>
    Task<DraftDto> SaveDraftAsync(Guid id, SaveDraftRequest request, CancellationToken ct = default);
    /// <summary>Theme and branding only. Works on plans without the full builder, within their theme/branding level.</summary>
    Task<DraftDto> SaveAppearanceAsync(Guid id, SaveAppearanceRequest request, CancellationToken ct = default);

    Task<SwitchPlanDto> PreviewSwitchAsync(Guid id, string targetTemplate, CancellationToken ct = default);
    Task<DraftDto> ApplySwitchAsync(Guid id, ApplySwitchRequest request, CancellationToken ct = default);

    Task<PublishResultDto> PublishAsync(Guid id, PublishRequest request, CancellationToken ct = default);
    Task UnpublishAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<VersionDto>> ListVersionsAsync(Guid id, CancellationToken ct = default);
    Task<JsonElement> GetVersionDocumentAsync(Guid id, int versionNumber, CancellationToken ct = default);
    /// <summary>Publishes a copy of an old version as a NEW version and makes it the draft. History is never rewritten.</summary>
    Task<PublishResultDto> RollbackAsync(Guid id, int versionNumber, PublishRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SectionPresetDto>> ListPresetsAsync(CancellationToken ct = default);
    Task<SectionPresetDto> CreatePresetAsync(CreatePresetRequest request, CancellationToken ct = default);
    Task DeletePresetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<DomainDto>> ListDomainsAsync(Guid storefrontId, CancellationToken ct = default);
    Task<DomainDto> AddDomainAsync(Guid storefrontId, AddDomainRequest request, CancellationToken ct = default);
    Task<DomainDto> VerifyDomainAsync(Guid storefrontId, Guid domainId, CancellationToken ct = default);
    Task RemoveDomainAsync(Guid storefrontId, Guid domainId, CancellationToken ct = default);

    Task<PreviewLinkDto> CreatePreviewLinkAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Resolves the TXT records of a DNS name. A seam so tests do not need the internet.</summary>
public interface IDnsTxtResolver
{
    Task<IReadOnlyList<string>> LookupTxtAsync(string name, CancellationToken ct = default);
}

// ------------------------------------------------------------------ public side (no login)

public sealed record PublicContactDto(string? Email, string? Phone, string? Address, string? OpeningHours);

public sealed record PublicSiteDto(string Name, string Slug, string BusinessName, string CurrencyCode, string TemplateKey, int VersionNumber,
    bool IsPreview, JsonElement Document, ResolvedTheme Theme, PublicContactDto Contact, IReadOnlyList<string> Workflows, bool AllowSearchIndexing);

public sealed record PublicImageDto(Guid MediaId, string? Alt);

public sealed record PublicDesignSummaryDto(Guid Id, string Name, string Slug, string Kind, string? CategoryName, decimal? BasePrice,
    string? CurrencyCode, bool IsFeatured, Guid? CoverMediaId, string? CoverAlt);

public sealed record PublicDesignDto(Guid Id, string Name, string Slug, string? Description, string Kind, Guid? CategoryId, string? CategoryName,
    string? GarmentType, IReadOnlyList<string> Tags, IReadOnlyList<string> Fabrics, IReadOnlyList<string> Colours, IReadOnlyList<string> Sizes,
    decimal? BasePrice, string? CurrencyCode, int? ProductionDays, bool IsFeatured, IReadOnlyList<PublicImageDto> Images);

public sealed record PublicCategoryDto(Guid Id, string Name);

public sealed record PublicCollectionDto(Guid Id, string Name, string Slug, string? Description, IReadOnlyList<PublicDesignSummaryDto> Designs);

public sealed record PublicDesignQuery : PagedQuery
{
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public bool? Featured { get; init; }
    public string? Kind { get; init; }
}

public interface IPublicSiteService
{
    /// <summary>Maps a verified custom domain to its site slug. Null when unknown.</summary>
    Task<string?> ResolveHostAsync(string host, CancellationToken ct = default);
    /// <summary>True only for a verified domain. Used by the TLS proxy to decide whether to issue a certificate.</summary>
    Task<bool> IsDomainAllowedAsync(string host, CancellationToken ct = default);
    Task<PublicSiteDto> GetSiteAsync(string slug, CancellationToken ct = default);
    Task<PublicSiteDto> GetPreviewAsync(string token, CancellationToken ct = default);
    Task<PagedResult<PublicDesignSummaryDto>> ListDesignsAsync(string slug, PublicDesignQuery query, CancellationToken ct = default);
    Task<PublicDesignDto> GetDesignAsync(string slug, string designSlug, CancellationToken ct = default);
    Task<IReadOnlyList<PublicCategoryDto>> ListCategoriesAsync(string slug, CancellationToken ct = default);
    Task<PublicCollectionDto> GetCollectionAsync(string slug, string collectionSlug, CancellationToken ct = default);
    Task<string> SitemapXmlAsync(string slug, string baseUrl, CancellationToken ct = default);
    Task<string> RobotsTxtAsync(string slug, string baseUrl, CancellationToken ct = default);
}

// ------------------------------------------------------------------ inquiries

public sealed record SubmitInquiryRequest
{
    /// <summary>Inquiry | Quotation | OrderRequest | Appointment | Contact</summary>
    [Required] public string Kind { get; init; } = "Inquiry";
    [Required, StringLength(100, MinimumLength = 2)] public string Name { get; init; } = "";
    [EmailAddress, StringLength(120)] public string? Email { get; init; }
    [StringLength(40)] public string? Phone { get; init; }
    [Required, StringLength(2000, MinimumLength = 3)] public string Message { get; init; } = "";
    public Guid? DesignId { get; init; }
    [StringLength(60)] public string? GarmentType { get; init; }
    [StringLength(60)] public string? Budget { get; init; }
    public DateOnly? PreferredDate { get; init; }
    public DateOnly? EventDate { get; init; }
    /// <summary>A hidden field real visitors never fill in. Bots do. A filled value is silently dropped.</summary>
    public string? Website { get; init; }
    /// <summary>Cloudflare Turnstile response token, when a captcha is configured.</summary>
    public string? CaptchaToken { get; init; }
}

public sealed record InquiryAcceptedDto(bool Received, string Message);

public sealed record InquiryDto(Guid Id, string Kind, string Status, string Name, string? Email, string? Phone, string Message, Guid? DesignId,
    string? DesignName, string? GarmentType, string? Budget, DateOnly? PreferredDate, DateOnly? EventDate, Guid? ConvertedOrderId,
    string? InternalNote, DateTime CreatedAtUtc, DateTime? HandledAtUtc);

public sealed record InquiryListQuery : PagedQuery
{
    public string? Status { get; init; }
    public string? Kind { get; init; }
    public string? Search { get; init; }
}

public sealed record UpdateInquiryRequest
{
    /// <summary>New | Reviewed | Rejected. Converted is set only by converting.</summary>
    [Required] public string Status { get; init; } = "";
    [StringLength(1000)] public string? InternalNote { get; init; }
}

public sealed record ConvertInquiryRequest
{
    [Required] public Guid LocationId { get; init; }
    [Required] public DateOnly PromisedDate { get; init; }
    /// <summary>Name of the garment line. Defaults to the design name or garment type.</summary>
    [StringLength(120)] public string? GarmentName { get; init; }
    [Range(0, 1_000_000_000)] public decimal UnitPrice { get; init; }
}

public sealed record InquirySummaryDto(int New, int Reviewed, int Converted, int Rejected);

public interface IInquiryService
{
    /// <summary>Anonymous. Spam-guarded. Always lands in the storefront's own business, whatever the layout.</summary>
    Task<InquiryAcceptedDto> SubmitAsync(string slug, SubmitInquiryRequest request, string? clientIp, CancellationToken ct = default);
    Task<PagedResult<InquiryDto>> ListAsync(InquiryListQuery query, CancellationToken ct = default);
    Task<InquiryDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<InquiryDto> UpdateAsync(Guid id, UpdateInquiryRequest request, CancellationToken ct = default);
    /// <summary>Finds or creates the customer, then creates a DRAFT order. The inquiry is marked Converted.</summary>
    Task<InquiryDto> ConvertToOrderAsync(Guid id, ConvertInquiryRequest request, CancellationToken ct = default);
    Task<InquirySummaryDto> SummaryAsync(CancellationToken ct = default);
}

public interface ICaptchaVerifier
{
    bool IsConfigured { get; }
    Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken ct = default);
}

public sealed class StorefrontOptions
{
    /// <summary>Sites live at {slug}.{BaseDomain}. Custom domains may not be under it.</summary>
    public string BaseDomain { get; set; } = "avzar.app";
    public int PreviewMinutes { get; set; } = 30;
    /// <summary>Cloudflare Turnstile secret. Empty means no captcha (the honeypot and rate limits still apply).</summary>
    public string? CaptchaSecretKey { get; set; }
}
