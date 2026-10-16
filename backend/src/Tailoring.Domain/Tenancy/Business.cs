using Tailoring.Domain.Common;

namespace Tailoring.Domain.Tenancy;

/// <summary>The tenant. Every tenant-scoped row points back to one of these.</summary>
public class Business : Entity
{
    public string Name { get; set; } = "";
    /// <summary>IANA time zone id, e.g. "Africa/Lagos". All timestamps are stored in UTC.</summary>
    public string TimeZoneId { get; set; } = "UTC";
    /// <summary>ISO 4217 code. Amounts in different currencies are never combined.</summary>
    public string CurrencyCode { get; set; } = "USD";
    public bool IsActive { get; set; } = true;

    // --- Document branding (drawn on PDFs). All optional: an unset business gets the neutral default look. ---
    /// <summary>"#RRGGBB". Validated for legibility on white before it is stored.</summary>
    public string? BrandColor { get; set; }
    /// <summary>A MediaAsset of this business. If the asset is later deleted the PDFs simply render without a logo.</summary>
    public Guid? LogoMediaId { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    /// <summary>VAT / TIN / registration number shown on documents.</summary>
    public string? TaxNumber { get; set; }
    public string? PdfFooterNote { get; set; }

    /// <summary>Optimistic concurrency token (mapped to PostgreSQL xmin).</summary>
    public uint Version { get; set; }
}

public class BusinessLocation : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}
