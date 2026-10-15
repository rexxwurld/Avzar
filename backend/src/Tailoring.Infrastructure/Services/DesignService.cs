using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Designs;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Common;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Media;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class DesignService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, IEntitlementService entitlements) : IDesignService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    // ------------------------------------------------------------------ designs

    public async Task<PagedResult<DesignSummaryDto>> ListAsync(DesignListQuery query, CancellationToken ct = default)
    {
        var status = string.IsNullOrWhiteSpace(query.Status) ? DesignStatus.Active : Parse.Value<DesignStatus>(query.Status, "status");
        var q = db.Designs.AsNoTracking().Where(d => d.Status == status);

        if (query.CategoryId is { } cid) q = q.Where(d => d.CategoryId == cid);
        if (query.Featured is { } featured) q = q.Where(d => d.IsFeatured == featured);
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            var kind = Parse.Value<DesignKind>(query.Kind, "kind");
            q = q.Where(d => d.Kind == kind);
        }
        if (!string.IsNullOrWhiteSpace(query.Visibility))
        {
            var vis = Parse.Value<DesignVisibility>(query.Visibility, "visibility");
            q = q.Where(d => d.Visibility == vis);
        }
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = NormalizeOne(query.Tag);
            q = q.Where(d => d.Tags.Contains(tag));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{Parse.Like(query.Search.Trim())}%";
            q = q.Where(d => EF.Functions.ILike(d.Name, pattern)
                || (d.Description != null && EF.Functions.ILike(d.Description, pattern))
                || (d.GarmentType != null && EF.Functions.ILike(d.GarmentType, pattern)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(d => d.IsFeatured).ThenBy(d => d.Name)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(d => new
            {
                d.Id, d.Name, d.Slug, d.Kind, d.CategoryId, d.BasePrice, d.CurrencyCode, d.IsFeatured,
                d.Visibility, d.Status, d.Tags, d.CreatedAtUtc,
                CoverMediaId = db.DesignImages.Where(i => i.DesignId == d.Id).OrderBy(i => i.SortOrder).Select(i => (Guid?)i.MediaAssetId).FirstOrDefault(),
                CategoryName = db.DesignCategories.Where(c => c.Id == d.CategoryId).Select(c => c.Name).FirstOrDefault()
            }).ToListAsync(ct);

        var items = rows.Select(r => new DesignSummaryDto(r.Id, r.Name, r.Slug, r.Kind.ToString(), r.CategoryId, r.CategoryName,
            r.BasePrice, r.CurrencyCode, r.IsFeatured, r.Visibility.ToString(), r.Status.ToString(), r.Tags, r.CreatedAtUtc, r.CoverMediaId)).ToList();
        return new PagedResult<DesignSummaryDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<DesignDto> GetAsync(Guid id, CancellationToken ct = default)
        => await ToDtoAsync(await LoadAsync(id, ct, track: false), ct);

    public async Task<DesignDto> CreateAsync(UpsertDesignRequest req, CancellationToken ct = default)
    {
        var kind = Parse.Value<DesignKind>(req.Kind, "kind");
        await EnsureCategoryAsync(req.CategoryId, ct);
        if (req.ParentDesignId is { } parentId && !await db.Designs.AnyAsync(d => d.Id == parentId, ct))
            throw AppException.BadRequest("unknown_parent_design", "The design you are varying does not exist.");

        var currency = await db.Businesses.AsNoTracking().Select(b => b.CurrencyCode).FirstAsync(ct);
        var design = new Design { BusinessId = BusinessId, Kind = kind, ParentDesignId = req.ParentDesignId, CreatedByUserId = tenant.UserId };
        Apply(design, req, currency);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await entitlements.EnsureBelowLimitAsync(BusinessId, EntitlementKeys.DesignsMax,
                c => db.Designs.LongCountAsync(d => d.Status == DesignStatus.Active, c), ct);

            design.Slug = await UniqueSlugAsync(design.Name, ct);
            db.Designs.Add(design);
            audit.Record(BusinessId, tenant.UserId, "design.created", nameof(Design), design.Id, new { design.Name, design.Slug });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two designs with the same name saved at the same instant raced for one slug.
            throw AppException.Conflict("slug_conflict", "Another design with the same name was saved at the same moment. Please try again.");
        }
        return await ToDtoAsync(design, ct);
    }

    public async Task<DesignDto> UpdateAsync(Guid id, UpsertDesignRequest req, CancellationToken ct = default)
    {
        if (req.Version is null)
            throw AppException.BadRequest("version_required", "Include the design's current version when updating.");

        var design = await LoadAsync(id, ct, track: true);
        if (design.Status == DesignStatus.Archived)
            throw AppException.Conflict("design_archived", "Restore this design before editing it.");

        var kind = Parse.Value<DesignKind>(req.Kind, "kind");
        await EnsureCategoryAsync(req.CategoryId, ct);
        var currency = await db.Businesses.AsNoTracking().Select(b => b.CurrencyCode).FirstAsync(ct);

        design.Kind = kind;
        Apply(design, req, currency); // name changes never touch the slug: public URLs stay stable

        db.Entry(design).Property(d => d.Version).OriginalValue = req.Version.Value;
        design.UpdatedAtUtc = DateTime.UtcNow; // guarantees an UPDATE, so a stale Version is always detected
        audit.Record(BusinessId, tenant.UserId, "design.updated", nameof(Design), design.Id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict("stale_version", "This design was changed by someone else. Reload and try again.");
        }
        return await ToDtoAsync(design, ct);
    }

    public async Task<DesignDto> SetStatusAsync(Guid id, SetDesignStatusRequest req, CancellationToken ct = default)
    {
        var status = Parse.Value<DesignStatus>(req.Status, "status");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var design = await LoadAsync(id, ct, track: true);
        if (design.Status == status) return await ToDtoAsync(design, ct);

        if (status == DesignStatus.Active)
            await entitlements.EnsureBelowLimitAsync(BusinessId, EntitlementKeys.DesignsMax,
                c => db.Designs.LongCountAsync(d => d.Status == DesignStatus.Active, c), ct);
        else
            design.Visibility = DesignVisibility.Private; // archived designs never stay public

        design.Status = status;
        audit.Record(BusinessId, tenant.UserId, "design.status_changed", nameof(Design), design.Id, new { status = status.ToString() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDtoAsync(design, ct);
    }

    public async Task<DesignDto> SetVisibilityAsync(Guid id, SetDesignVisibilityRequest req, CancellationToken ct = default)
    {
        var visibility = Parse.Value<DesignVisibility>(req.Visibility, "visibility");
        var design = await LoadAsync(id, ct, track: true);
        if (visibility == DesignVisibility.Public && design.Status != DesignStatus.Active)
            throw AppException.Conflict("design_archived", "Archived designs cannot be made public. Restore it first.");

        design.Visibility = visibility;
        audit.Record(BusinessId, tenant.UserId, "design.visibility_changed", nameof(Design), design.Id, new { visibility = visibility.ToString() });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(design, ct);
    }

    public async Task<DesignDto> SetImagesAsync(Guid id, SetDesignImagesRequest req, CancellationToken ct = default)
    {
        var design = await LoadAsync(id, ct, track: false);
        if (design.Status == DesignStatus.Archived)
            throw AppException.Conflict("design_archived", "Restore this design before changing its images.");

        var ids = req.Images.Select(i => i.MediaId).ToList();
        if (ids.Count != ids.Distinct().Count())
            throw AppException.BadRequest("duplicate_image", "The same image was listed twice.");

        // Tenant filter on MediaAssets means another business's image id simply is not found.
        var valid = await db.MediaAssets.Where(a => ids.Contains(a.Id) && a.Purpose == MediaPurpose.DesignImage)
            .Select(a => a.Id).ToListAsync(ct);
        if (valid.Count != ids.Count)
            throw AppException.BadRequest("unknown_image", "One or more images were not found. Upload them as design images first.");

        var existing = await db.DesignImages.Where(i => i.DesignId == id).ToListAsync(ct);
        db.DesignImages.RemoveRange(existing.Where(e => !ids.Contains(e.MediaAssetId)));
        for (var pos = 0; pos < req.Images.Count; pos++)
        {
            var input = req.Images[pos];
            var row = existing.FirstOrDefault(e => e.MediaAssetId == input.MediaId);
            if (row is null)
                db.DesignImages.Add(new DesignImage { BusinessId = BusinessId, DesignId = id, MediaAssetId = input.MediaId, SortOrder = pos, AltText = Parse.Clean(input.AltText) });
            else { row.SortOrder = pos; row.AltText = Parse.Clean(input.AltText); }
        }
        audit.Record(BusinessId, tenant.UserId, "design.images_changed", nameof(Design), id, new { count = ids.Count });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(design, ct);
    }

    // ------------------------------------------------------------------ categories

    public async Task<IReadOnlyList<DesignCategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
        => await db.DesignCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new DesignCategoryDto(c.Id, c.Name, c.SortOrder, db.Designs.Count(d => d.CategoryId == c.Id)))
            .ToListAsync(ct);

    public async Task<DesignCategoryDto> CreateCategoryAsync(UpsertCategoryRequest req, CancellationToken ct = default)
    {
        var name = req.Name.Trim();
        await EnsureCategoryNameFreeAsync(name, null, ct);
        var cat = new DesignCategory { BusinessId = BusinessId, Name = name, SortOrder = req.SortOrder };
        db.DesignCategories.Add(cat);
        audit.Record(BusinessId, tenant.UserId, "design_category.created", nameof(DesignCategory), cat.Id, new { name });
        await db.SaveChangesAsync(ct);
        return new DesignCategoryDto(cat.Id, cat.Name, cat.SortOrder, 0);
    }

    public async Task<DesignCategoryDto> UpdateCategoryAsync(Guid id, UpsertCategoryRequest req, CancellationToken ct = default)
    {
        var cat = await db.DesignCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw AppException.NotFound("category_not_found", "Category not found.");
        var name = req.Name.Trim();
        await EnsureCategoryNameFreeAsync(name, id, ct);
        cat.Name = name; cat.SortOrder = req.SortOrder;
        audit.Record(BusinessId, tenant.UserId, "design_category.updated", nameof(DesignCategory), cat.Id);
        await db.SaveChangesAsync(ct);
        return new DesignCategoryDto(cat.Id, cat.Name, cat.SortOrder, await db.Designs.CountAsync(d => d.CategoryId == id, ct));
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var cat = await db.DesignCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw AppException.NotFound("category_not_found", "Category not found.");
        var inUse = await db.Designs.CountAsync(d => d.CategoryId == id, ct);
        if (inUse > 0)
            throw AppException.Conflict("category_in_use", $"{inUse} design(s) use this category. Move them to another category first.",
                new Dictionary<string, object?> { ["designCount"] = inUse });

        db.DesignCategories.Remove(cat);
        audit.Record(BusinessId, tenant.UserId, "design_category.deleted", nameof(DesignCategory), cat.Id, new { cat.Name });
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Design> LoadAsync(Guid id, CancellationToken ct, bool track)
    {
        var q = track ? db.Designs.AsQueryable() : db.Designs.AsNoTracking();
        return await q.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw AppException.NotFound("design_not_found", "Design not found.");
    }

    private async Task EnsureCategoryAsync(Guid? categoryId, CancellationToken ct)
    {
        if (categoryId is { } id && !await db.DesignCategories.AnyAsync(c => c.Id == id, ct))
            throw AppException.BadRequest("unknown_category", "Category not found.");
    }

    private async Task EnsureCategoryNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.DesignCategories.AnyAsync(c => c.Id != exceptId && c.Name.ToLower() == lower, ct))
            throw AppException.Conflict("category_exists", "A category with this name already exists.");
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = Slug.From(name, "design", 70);
        var taken = (await db.Designs.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.BusinessId == BusinessId && (d.Slug == baseSlug || d.Slug.StartsWith(baseSlug + "-")))
            .Select(d => d.Slug).ToListAsync(ct)).ToHashSet();
        if (!taken.Contains(baseSlug)) return baseSlug;
        for (var n = 2; ; n++)
            if (!taken.Contains($"{baseSlug}-{n}")) return $"{baseSlug}-{n}";
    }

    private static void Apply(Design d, UpsertDesignRequest req, string businessCurrency)
    {
        d.Name = req.Name.Trim();
        d.Description = Parse.Clean(req.Description);
        d.CategoryId = req.CategoryId;
        d.GarmentType = Parse.Clean(req.GarmentType);
        d.Tags = NormalizeList(req.Tags, 30, lower: true);
        d.Fabrics = NormalizeList(req.Fabrics, 40, lower: false);
        d.Colours = NormalizeList(req.Colours, 40, lower: false);
        d.Sizes = NormalizeList(req.Sizes, 40, lower: false);
        d.BasePrice = req.BasePrice is { } p ? Math.Round(p, 2, MidpointRounding.AwayFromZero) : null;
        // The price is stamped with the business's CURRENT currency on every save.
        d.CurrencyCode = d.BasePrice is null ? null : businessCurrency;
        d.ProductionDays = req.ProductionDays;
        d.IsFeatured = req.IsFeatured;
    }

    private static string NormalizeOne(string s) => s.Trim().ToLowerInvariant();

    /// <summary>Trim, drop blanks, de-duplicate case-insensitively, cap length. Order of first appearance is kept.</summary>
    private static List<string> NormalizeList(IEnumerable<string>? items, int maxLen, bool lower)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in items ?? [])
        {
            var v = Parse.Truncate(raw?.Trim() ?? "", maxLen);
            if (lower) v = v.ToLowerInvariant();
            if (v.Length > 0 && seen.Add(v)) result.Add(v);
        }
        return result;
    }

    private async Task<DesignDto> ToDtoAsync(Design d, CancellationToken ct)
    {
        var categoryName = d.CategoryId is { } cid
            ? await db.DesignCategories.AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;
        var images = await db.DesignImages.AsNoTracking().Where(i => i.DesignId == d.Id).OrderBy(i => i.SortOrder)
            .Select(i => new DesignImageDto(i.MediaAssetId, i.AltText, i.SortOrder)).ToListAsync(ct);
        return new DesignDto(d.Id, d.Name, d.Slug, d.Description, d.Kind.ToString(), d.CategoryId, categoryName, d.GarmentType,
            d.Tags, d.Fabrics, d.Colours, d.Sizes, d.BasePrice, d.CurrencyCode, d.ProductionDays, d.IsFeatured,
            d.Visibility.ToString(), d.Status.ToString(), d.ParentDesignId, d.Version, d.CreatedAtUtc, images);
    }
}
