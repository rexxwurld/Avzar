using System.ComponentModel.DataAnnotations;
using Tailoring.Application.Common;

namespace Tailoring.Application.Designs;

public sealed record DesignCategoryDto(Guid Id, string Name, int SortOrder, int DesignCount);

public sealed record UpsertCategoryRequest
{
    [Required, StringLength(80, MinimumLength = 2)] public string Name { get; init; } = "";
    [Range(0, 10000)] public int SortOrder { get; init; }
}

public sealed record DesignSummaryDto(
    Guid Id, string Name, string Slug, string Kind, Guid? CategoryId, string? CategoryName, decimal? BasePrice,
    string? CurrencyCode, bool IsFeatured, string Visibility, string Status, IReadOnlyList<string> Tags, DateTime CreatedAtUtc,
    Guid? CoverMediaId);

public sealed record DesignDto(
    Guid Id, string Name, string Slug, string? Description, string Kind, Guid? CategoryId, string? CategoryName,
    string? GarmentType, IReadOnlyList<string> Tags, IReadOnlyList<string> Fabrics, IReadOnlyList<string> Colours,
    IReadOnlyList<string> Sizes, decimal? BasePrice, string? CurrencyCode, int? ProductionDays, bool IsFeatured,
    string Visibility, string Status, Guid? ParentDesignId, uint Version, DateTime CreatedAtUtc,
    IReadOnlyList<DesignImageDto> Images);

/// <summary>Position 0 is the cover. Fetch pixels from /api/media/{MediaId}/file/{sm|md|orig} (or /public/media/... for a Public design).</summary>
public sealed record DesignImageDto(Guid MediaId, string? AltText, int SortOrder);

public sealed record DesignImageInput
{
    [Required] public Guid MediaId { get; init; }
    [StringLength(200)] public string? AltText { get; init; }
}

public sealed record SetDesignImagesRequest
{
    [Required, MaxLength(10)] public List<DesignImageInput> Images { get; init; } = new();
}

public sealed record DesignListQuery : PagedQuery
{
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public string? Kind { get; init; }
    public string? Visibility { get; init; }
    /// <summary>Active (default) | Archived</summary>
    public string? Status { get; init; }
    public bool? Featured { get; init; }
    public string? Tag { get; init; }
}

public sealed record UpsertDesignRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Name { get; init; } = "";
    [StringLength(4000)] public string? Description { get; init; }
    /// <summary>ReadyMade | MadeToOrder | Service</summary>
    [Required] public string Kind { get; init; } = "MadeToOrder";
    public Guid? CategoryId { get; init; }
    [StringLength(60)] public string? GarmentType { get; init; }
    [MaxLength(20)] public List<string>? Tags { get; init; }
    [MaxLength(30)] public List<string>? Fabrics { get; init; }
    [MaxLength(30)] public List<string>? Colours { get; init; }
    [MaxLength(30)] public List<string>? Sizes { get; init; }
    [Range(0, 999_999_999.99)] public decimal? BasePrice { get; init; }
    [Range(1, 365)] public int? ProductionDays { get; init; }
    public bool IsFeatured { get; init; }
    /// <summary>Make this a variation of an existing design (create only).</summary>
    public Guid? ParentDesignId { get; init; }
    /// <summary>Required on update (optimistic concurrency).</summary>
    public uint? Version { get; init; }
}

public sealed record SetDesignStatusRequest { [Required] public string Status { get; init; } = ""; }   // Active | Archived
public sealed record SetDesignVisibilityRequest { [Required] public string Visibility { get; init; } = ""; } // Private | Public

public interface IDesignService
{
    Task<PagedResult<DesignSummaryDto>> ListAsync(DesignListQuery query, CancellationToken ct = default);
    Task<DesignDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Counts against the plan's saved-design limit.</summary>
    Task<DesignDto> CreateAsync(UpsertDesignRequest req, CancellationToken ct = default);
    Task<DesignDto> UpdateAsync(Guid id, UpsertDesignRequest req, CancellationToken ct = default);
    /// <summary>Archiving also makes the design Private. Restoring counts against the plan limit again.</summary>
    Task<DesignDto> SetStatusAsync(Guid id, SetDesignStatusRequest req, CancellationToken ct = default);
    Task<DesignDto> SetVisibilityAsync(Guid id, SetDesignVisibilityRequest req, CancellationToken ct = default);
    /// <summary>Replaces the whole ordered gallery (max 10). Only DesignImage uploads of this business are accepted.</summary>
    Task<DesignDto> SetImagesAsync(Guid id, SetDesignImagesRequest req, CancellationToken ct = default);

    Task<IReadOnlyList<DesignCategoryDto>> ListCategoriesAsync(CancellationToken ct = default);
    Task<DesignCategoryDto> CreateCategoryAsync(UpsertCategoryRequest req, CancellationToken ct = default);
    Task<DesignCategoryDto> UpdateCategoryAsync(Guid id, UpsertCategoryRequest req, CancellationToken ct = default);
    Task DeleteCategoryAsync(Guid id, CancellationToken ct = default);
}
