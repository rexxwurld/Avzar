using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Collections;
using Tailoring.Application.Common;
using Tailoring.Domain.Common;
using Tailoring.Domain.Designs;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class CollectionService(AppDbContext db, ITenantContext tenant, IAuditLogger audit) : ICollectionService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<PagedResult<CollectionSummaryDto>> ListAsync(CollectionListQuery query, CancellationToken ct = default)
    {
        var q = db.DesignCollections.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Visibility))
        {
            var vis = Parse.Value<DesignVisibility>(query.Visibility, "visibility");
            q = q.Where(c => c.Visibility == vis);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{Parse.Like(query.Search.Trim())}%";
            q = q.Where(c => EF.Functions.ILike(c.Name, pattern));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(c => new
            {
                c.Id, c.Name, c.Slug, c.Visibility, c.SortOrder,
                Count = db.DesignCollectionItems.Count(i => i.CollectionId == c.Id),
                PublicCount = db.DesignCollectionItems.Count(i => i.CollectionId == c.Id
                    && db.Designs.Any(d => d.Id == i.DesignId && d.Visibility == DesignVisibility.Public && d.Status == DesignStatus.Active)),
            }).ToListAsync(ct);

        return new PagedResult<CollectionSummaryDto>(
            rows.Select(r => new CollectionSummaryDto(r.Id, r.Name, r.Slug, r.Visibility.ToString(), r.SortOrder, r.Count, r.PublicCount)).ToList(),
            query.Page, query.PageSize, total);
    }

    public async Task<CollectionDto> GetAsync(Guid id, CancellationToken ct = default)
        => await ToDtoAsync(await LoadAsync(id, ct, track: false), ct);

    public async Task<CollectionDto> CreateAsync(UpsertCollectionRequest req, CancellationToken ct = default)
    {
        var visibility = Parse.Value<DesignVisibility>(req.Visibility, "visibility");
        var collection = new DesignCollection
        {
            BusinessId = BusinessId, Name = req.Name.Trim(), Description = Parse.Clean(req.Description),
            Visibility = visibility, SortOrder = req.SortOrder,
        };
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            collection.Slug = await UniqueSlugAsync(collection.Name, ct);
            db.DesignCollections.Add(collection);
            audit.Record(BusinessId, tenant.UserId, "collection.created", nameof(DesignCollection), collection.Id, new { collection.Name });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw AppException.Conflict("slug_conflict", "Another collection with the same name was saved at the same moment. Please try again.");
        }
        return await ToDtoAsync(collection, ct);
    }

    public async Task<CollectionDto> UpdateAsync(Guid id, UpsertCollectionRequest req, CancellationToken ct = default)
    {
        if (req.Version is null)
            throw AppException.BadRequest("version_required", "Include the collection's current version when updating.");
        var visibility = Parse.Value<DesignVisibility>(req.Visibility, "visibility");

        var collection = await LoadAsync(id, ct, track: true);
        collection.Name = req.Name.Trim(); // the slug never changes, so public URLs stay stable
        collection.Description = Parse.Clean(req.Description);
        collection.Visibility = visibility;
        collection.SortOrder = req.SortOrder;
        collection.UpdatedAtUtc = DateTime.UtcNow;

        db.Entry(collection).Property(c => c.Version).OriginalValue = req.Version.Value;
        audit.Record(BusinessId, tenant.UserId, "collection.updated", nameof(DesignCollection), collection.Id, new { visibility = visibility.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict("stale_version", "This collection was changed by someone else. Reload and try again.");
        }
        return await ToDtoAsync(collection, ct);
    }

    public async Task<CollectionDto> SetItemsAsync(Guid id, SetCollectionItemsRequest req, CancellationToken ct = default)
    {
        var collection = await LoadAsync(id, ct, track: false);
        var ids = req.DesignIds;
        if (ids.Count != ids.Distinct().Count())
            throw AppException.BadRequest("duplicate_design", "The same design was listed twice.");

        // The tenant filter means another business's design id is simply "not found".
        var found = await db.Designs.Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.Status }).ToListAsync(ct);
        if (found.Count != ids.Count)
            throw AppException.BadRequest("unknown_design", "One or more designs were not found.");
        if (found.Any(d => d.Status == DesignStatus.Archived))
            throw AppException.Conflict("design_archived", "Archived designs cannot be added to a collection. Restore them first.");

        var existing = await db.DesignCollectionItems.Where(i => i.CollectionId == id).ToListAsync(ct);
        db.DesignCollectionItems.RemoveRange(existing.Where(e => !ids.Contains(e.DesignId)));
        for (var pos = 0; pos < ids.Count; pos++)
        {
            var row = existing.FirstOrDefault(e => e.DesignId == ids[pos]);
            if (row is null) db.DesignCollectionItems.Add(new DesignCollectionItem { BusinessId = BusinessId, CollectionId = id, DesignId = ids[pos], SortOrder = pos });
            else row.SortOrder = pos;
        }
        audit.Record(BusinessId, tenant.UserId, "collection.items_changed", nameof(DesignCollection), id, new { count = ids.Count });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(collection, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var collection = await LoadAsync(id, ct, track: true);
        db.DesignCollections.Remove(collection); // items cascade; designs are untouched
        audit.Record(BusinessId, tenant.UserId, "collection.deleted", nameof(DesignCollection), id, new { collection.Name });
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<DesignCollection> LoadAsync(Guid id, CancellationToken ct, bool track)
    {
        var q = track ? db.DesignCollections.AsQueryable() : db.DesignCollections.AsNoTracking();
        return await q.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw AppException.NotFound("collection_not_found", "Collection not found.");
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = Slug.From(name, "collection", 70);
        var taken = (await db.DesignCollections.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.BusinessId == BusinessId && (c.Slug == baseSlug || c.Slug.StartsWith(baseSlug + "-")))
            .Select(c => c.Slug).ToListAsync(ct)).ToHashSet();
        if (!taken.Contains(baseSlug)) return baseSlug;
        for (var n = 2; ; n++)
            if (!taken.Contains($"{baseSlug}-{n}")) return $"{baseSlug}-{n}";
    }

    private async Task<CollectionDto> ToDtoAsync(DesignCollection c, CancellationToken ct)
    {
        var items = await db.DesignCollectionItems.AsNoTracking().Where(i => i.CollectionId == c.Id).OrderBy(i => i.SortOrder)
            .Select(i => new
            {
                i.SortOrder,
                Design = db.Designs.Where(d => d.Id == i.DesignId).Select(d => new { d.Id, d.Name, d.Slug, d.Status, d.Visibility }).First(),
                Cover = db.DesignImages.Where(x => x.DesignId == i.DesignId).OrderBy(x => x.SortOrder).Select(x => (Guid?)x.MediaAssetId).FirstOrDefault(),
            }).ToListAsync(ct);

        return new CollectionDto(c.Id, c.Name, c.Slug, c.Description, c.Visibility.ToString(), c.SortOrder, c.Version,
            items.Select(i => new CollectionItemDto(i.Design.Id, i.Design.Name, i.Design.Slug, i.Design.Status.ToString(),
                i.Design.Visibility.ToString(), i.SortOrder, i.Cover)).ToList());
    }
}
