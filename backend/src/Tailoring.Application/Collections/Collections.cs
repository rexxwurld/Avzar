using System.ComponentModel.DataAnnotations;
using Tailoring.Application.Common;

namespace Tailoring.Application.Collections;

/// <param name="PublicDesignCount">How many designs a visitor would actually see (Public + Active). Lets the UI warn "3 of 8 are hidden".</param>
public sealed record CollectionSummaryDto(Guid Id, string Name, string Slug, string Visibility, int SortOrder, int DesignCount, int PublicDesignCount);

public sealed record CollectionItemDto(Guid DesignId, string Name, string Slug, string Status, string Visibility, int SortOrder, Guid? CoverMediaId);

public sealed record CollectionDto(Guid Id, string Name, string Slug, string? Description, string Visibility, int SortOrder,
    uint Version, IReadOnlyList<CollectionItemDto> Items);

public sealed record CollectionListQuery : PagedQuery
{
    public string? Search { get; init; }
    public string? Visibility { get; init; }
}

public sealed record UpsertCollectionRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Description { get; init; }
    /// <summary>Private | Public</summary>
    [Required] public string Visibility { get; init; } = "Private";
    [Range(0, 10000)] public int SortOrder { get; init; }
    /// <summary>Required on update.</summary>
    public uint? Version { get; init; }
}

public sealed record SetCollectionItemsRequest
{
    /// <summary>Design ids in display order. Replaces the whole list.</summary>
    [Required, MaxLength(100)] public List<Guid> DesignIds { get; init; } = new();
}

public interface ICollectionService
{
    Task<PagedResult<CollectionSummaryDto>> ListAsync(CollectionListQuery query, CancellationToken ct = default);
    Task<CollectionDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CollectionDto> CreateAsync(UpsertCollectionRequest req, CancellationToken ct = default);
    Task<CollectionDto> UpdateAsync(Guid id, UpsertCollectionRequest req, CancellationToken ct = default);
    /// <summary>Only existing, non-archived designs of this business may be added.</summary>
    Task<CollectionDto> SetItemsAsync(Guid id, SetCollectionItemsRequest req, CancellationToken ct = default);
    /// <summary>Removes the collection only. Its designs are untouched.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
