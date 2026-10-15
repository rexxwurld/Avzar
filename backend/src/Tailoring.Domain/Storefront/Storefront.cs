using Tailoring.Domain.Common;

namespace Tailoring.Domain.Storefront;

public enum StorefrontStatus { Draft, Published, Unpublished }
public enum DomainStatus { Pending, Verified, Failed }
public enum InquiryKind { Inquiry, Quotation, OrderRequest, Appointment, Contact }
public enum InquiryStatus { New, Reviewed, Converted, Rejected }

/// <summary>
/// A business's public website. The DRAFT is a validated JSON document the owner edits; PUBLISHED versions are immutable
/// snapshots in <see cref="StorefrontVersion"/>. Visitors only ever see a snapshot, never the draft.
/// </summary>
public class Storefront : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Globally unique: yourname.avzar.app. Chosen once.</summary>
    public string Slug { get; set; } = "";
    public string TemplateKey { get; set; } = "standard";
    public string DraftJson { get; set; } = "{}";
    /// <summary>Counts draft saves. Editors send the revision they loaded; a stale one is refused.</summary>
    public int DraftRevision { get; set; }
    public StorefrontStatus Status { get; set; } = StorefrontStatus.Draft;
    public Guid? PublishedVersionId { get; set; }

    // Public contact details. Deliberately separate from private business data: only what the owner types here is ever shown.
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? OpeningHours { get; set; }

    // Which public workflows are switched on. Publishing is blocked if one has no entry point in the layout.
    public bool WorkflowInquiry { get; set; } = true;
    public bool WorkflowBooking { get; set; }
    public bool WorkflowCustomOrder { get; set; }
    public bool WorkflowContact { get; set; } = true;
    public bool AllowSearchIndexing { get; set; } = true;
    public uint Version { get; set; }

    public IReadOnlyList<string> EnabledWorkflows() => new[]
    {
        WorkflowInquiry ? "inquiry" : null, WorkflowBooking ? "booking" : null,
        WorkflowCustomOrder ? "customOrder" : null, WorkflowContact ? "contact" : null,
    }.Where(w => w is not null).Select(w => w!).ToList();
}

/// <summary>Immutable. Never updated after insert; rollback creates a NEW version that copies an old one.</summary>
public class StorefrontVersion : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid StorefrontId { get; set; }
    public int VersionNumber { get; set; }
    public int SchemaVersion { get; set; }
    public string TemplateKey { get; set; } = "";
    public string DocumentJson { get; set; } = "{}";
    public string? Note { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public int? RolledBackFromVersion { get; set; }
}

/// <summary>Which media a version shows. Lets the public file route serve exactly the images of the live version, nothing else.</summary>
public class StorefrontVersionMedia : ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid VersionId { get; set; }
    public Guid MediaAssetId { get; set; }
}

public class StorefrontDomain : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid StorefrontId { get; set; }
    /// <summary>Lowercase hostname, globally unique.</summary>
    public string Hostname { get; set; } = "";
    /// <summary>The owner proves control by publishing this value as a DNS TXT record on _avzar-verify.{hostname}.</summary>
    public string VerificationToken { get; set; } = "";
    public DomainStatus Status { get; set; } = DomainStatus.Pending;
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? LastCheckedAtUtc { get; set; }
    public string? LastError { get; set; }
}

public class SectionPreset : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string SectionType { get; set; } = "";
    public string Variant { get; set; } = "";
    public string PropsJson { get; set; } = "{}";
    public string StyleJson { get; set; } = "{}";
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// Anything a visitor sends from the site. Never an order: it only becomes one when staff convert it.
/// </summary>
public class Inquiry : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid StorefrontId { get; set; }
    public InquiryKind Kind { get; set; }
    public InquiryStatus Status { get; set; } = InquiryStatus.New;
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Message { get; set; } = "";
    public Guid? DesignId { get; set; }
    public string? DesignNameSnapshot { get; set; }
    public string? GarmentType { get; set; }
    public string? Budget { get; set; }
    public DateOnly? PreferredDate { get; set; }
    public DateOnly? EventDate { get; set; }
    public Guid? ConvertedOrderId { get; set; }
    public string? InternalNote { get; set; }
    public Guid? HandledByUserId { get; set; }
    public DateTime? HandledAtUtc { get; set; }
    /// <summary>SHA-256 of the sender's IP (never the address itself), for abuse limits.</summary>
    public string? IpHash { get; set; }
}
