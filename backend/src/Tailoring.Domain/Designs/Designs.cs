using Tailoring.Domain.Common;

namespace Tailoring.Domain.Designs;

public enum DesignKind { ReadyMade, MadeToOrder, Service }
public enum DesignStatus { Active, Archived }

/// <summary>Only <see cref="Public"/> designs can ever appear on a storefront. Private is the default.</summary>
public enum DesignVisibility { Private, Public }

public class DesignCategory : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class Design : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Stable, URL-safe, unique per business. Generated once; renaming a design never changes its public URL.</summary>
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public DesignKind Kind { get; set; } = DesignKind.MadeToOrder;
    public Guid? CategoryId { get; set; }
    /// <summary>Free text ("Agbada", "Two-piece suit"). Not tied to measurement templates yet.</summary>
    public string? GarmentType { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Fabrics { get; set; } = new();
    public List<string> Colours { get; set; } = new();
    public List<string> Sizes { get; set; } = new();
    /// <summary>Null means "price on request". The currency is captured with the price so a later business-currency change cannot relabel it.</summary>
    public decimal? BasePrice { get; set; }
    public string? CurrencyCode { get; set; }
    public int? ProductionDays { get; set; }
    public bool IsFeatured { get; set; }
    public DesignVisibility Visibility { get; set; } = DesignVisibility.Private;
    public DesignStatus Status { get; set; } = DesignStatus.Active;
    /// <summary>Set when this design is a variation of another design.</summary>
    public Guid? ParentDesignId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public uint Version { get; set; }
}

/// <summary>Ordered gallery of a design. Position 0 is the cover image.</summary>
public class DesignImage : ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid DesignId { get; set; }
    public Guid MediaAssetId { get; set; }
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
}

/// <summary>A curated, ordered group of designs ("Bridal 2026"). A Public collection only ever SHOWS Public + Active designs.</summary>
public class DesignCollection : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public DesignVisibility Visibility { get; set; } = DesignVisibility.Private;
    public int SortOrder { get; set; }
    public uint Version { get; set; }
}

public class DesignCollectionItem : ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid CollectionId { get; set; }
    public Guid DesignId { get; set; }
    public int SortOrder { get; set; }
}
