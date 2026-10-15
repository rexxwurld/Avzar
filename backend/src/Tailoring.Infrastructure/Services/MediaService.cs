using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Media;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Media;
using Tailoring.Domain.Storefront;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class MediaService(
    AppDbContext db, ITenantContext tenant, IAuditLogger audit, IEntitlementService entitlements,
    IImageProcessor images, IObjectStorage storage, IOptions<StorageOptions> options) : IMediaService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");
    private StorageOptions Opt => options.Value;

    public async Task<MediaAssetDto> UploadAsync(Stream content, string fileName, string purpose, CancellationToken ct = default)
    {
        var parsedPurpose = Parse.Value<MediaPurpose>(purpose, "purpose");
        var bytes = await ReadBoundedAsync(content, Opt.MaxUploadBytes, ct);
        var processed = await images.ProcessAsync(bytes, ct); // rejects non-images, SVG/GIF, oversized pixel counts

        var asset = new MediaAsset
        {
            BusinessId = BusinessId, Purpose = parsedPurpose, OriginalFileName = SafeFileName(fileName),
            Width = processed.Width, Height = processed.Height, UploadedByUserId = tenant.UserId,
            SizeBytes = processed.Variants.Values.Sum(v => (long)v.Length),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        };

        var written = new List<string>();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await entitlements.EnsureWithinLimitAsync(BusinessId, EntitlementKeys.StorageGbMax,
                async c => await db.MediaAssets.SumAsync(a => (long?)a.SizeBytes, c) ?? 0L,
                asset.SizeBytes, Opt.QuotaBytesPerGb, ct);

            foreach (var (variant, data) in processed.Variants)
            {
                var key = MediaVariants.KeyFor(BusinessId, asset.Id, variant);
                await storage.PutAsync(key, data, asset.ContentType, ct);
                written.Add(key);
            }
            db.MediaAssets.Add(asset);
            audit.Record(BusinessId, tenant.UserId, "media.uploaded", nameof(MediaAsset), asset.Id, new { asset.SizeBytes, purpose = parsedPurpose.ToString() });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            // Compensate: never leave stored files that no row points at. (A hard crash can still orphan files; a cleanup job is a follow-up.)
            foreach (var key in written) { try { await storage.DeleteAsync(key, CancellationToken.None); } catch { /* best effort */ } }
            throw;
        }
        return ToDto(asset, inUse: false);
    }

    public async Task<PagedResult<MediaAssetDto>> ListAsync(MediaListQuery query, CancellationToken ct = default)
    {
        var q = db.MediaAssets.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Purpose))
        {
            var p = Parse.Value<MediaPurpose>(query.Purpose, "purpose");
            q = q.Where(a => a.Purpose == p);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(a => new { Asset = a, InUse = db.DesignImages.Any(i => i.MediaAssetId == a.Id) || db.StorefrontVersionMedia.Any(m => m.MediaAssetId == a.Id) }).ToListAsync(ct);
        return new PagedResult<MediaAssetDto>(rows.Select(r => ToDto(r.Asset, r.InUse)).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<MediaAssetDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw NotFound();
        return ToDto(asset, await db.DesignImages.AnyAsync(i => i.MediaAssetId == id, ct));
    }

    public async Task<MediaUsageDto> GetUsageAsync(CancellationToken ct = default)
    {
        var used = await db.MediaAssets.AsNoTracking().SumAsync(a => (long?)a.SizeBytes, ct) ?? 0L;
        var limit = (await entitlements.GetAsync(BusinessId, ct)).Limit(EntitlementKeys.StorageGbMax);
        return new MediaUsageDto(used, limit.IsUnlimited ? null : limit.Max * Opt.QuotaBytesPerGb);
    }

    public async Task<MediaContent> OpenAsync(Guid id, string variant, CancellationToken ct = default)
    {
        RequireVariant(variant);
        var asset = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw NotFound();
        return await OpenObjectAsync(asset, variant, ct);
    }

    public async Task<MediaContent> OpenPublicAsync(Guid id, string variant, CancellationToken ct = default)
    {
        RequireVariant(variant);
        // Cross-tenant by design (anonymous visitors): public only when attached to a Public + Active design.
        // Everything else, including "exists but private", returns the same 404 so existence is not leaked.
        var asset = await db.MediaAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Id == id && (
                db.DesignImages.IgnoreQueryFilters().Any(i => i.MediaAssetId == a.Id
                    && db.Designs.IgnoreQueryFilters().Any(d => d.Id == i.DesignId && d.BusinessId == i.BusinessId
                        && d.Visibility == DesignVisibility.Public && d.Status == DesignStatus.Active))
                // an image of a site's LIVE published version (never of a draft, an old version, or an unpublished site)
                || db.StorefrontVersionMedia.IgnoreQueryFilters().Any(m => m.MediaAssetId == a.Id
                    && db.Storefronts.IgnoreQueryFilters().Any(s => s.PublishedVersionId == m.VersionId && s.Status == StorefrontStatus.Published))))
            .FirstOrDefaultAsync(ct) ?? throw NotFound();
        return await OpenObjectAsync(asset, variant, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw NotFound();
        if (await db.DesignImages.AnyAsync(i => i.MediaAssetId == id, ct))
            throw AppException.Conflict("media_in_use", "This image is used by a design. Remove it from the design first.");
        // Used by any site version (old versions can be rolled back to) or by a draft: removing it would break them.
        var needle = $"%{id}%";
        if (await db.StorefrontVersionMedia.AnyAsync(m => m.MediaAssetId == id, ct)
            || await db.Storefronts.AnyAsync(s => EF.Functions.ILike(s.DraftJson, needle), ct))
            throw AppException.Conflict("media_in_use", "This image is used by your website (its draft or a published version).");

        db.MediaAssets.Remove(asset);
        audit.Record(BusinessId, tenant.UserId, "media.deleted", nameof(MediaAsset), asset.Id, new { asset.SizeBytes });
        await db.SaveChangesAsync(ct); // row first: a failure below leaves an orphan file, never a dangling row
        foreach (var variant in MediaVariants.All)
        {
            try { await storage.DeleteAsync(MediaVariants.KeyFor(asset.BusinessId, asset.Id, variant), ct); } catch { /* orphan cleanup job later */ }
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task<MediaContent> OpenObjectAsync(MediaAsset asset, string variant, CancellationToken ct)
    {
        var stream = await storage.OpenReadAsync(MediaVariants.KeyFor(asset.BusinessId, asset.Id, variant), ct)
            ?? throw AppException.NotFound("media_missing", "The file for this image is missing.");
        return new MediaContent(stream, asset.ContentType, stream.CanSeek ? stream.Length : -1);
    }

    private static void RequireVariant(string variant)
    {
        if (!MediaVariants.IsValid(variant)) throw NotFound();
    }

    private static AppException NotFound() => AppException.NotFound("media_not_found", "Image not found.");

    private static async Task<byte[]> ReadBoundedAsync(Stream s, int max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await s.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > max)
                throw new AppException(413, "file_too_large", $"Images can be at most {max / (1024 * 1024)} MB.");
            ms.Write(buffer, 0, read);
        }
        if (ms.Length == 0) throw AppException.BadRequest("empty_file", "The uploaded file is empty.");
        return ms.ToArray();
    }

    /// <summary>Display name only: strip any path, control characters and length. It never touches the file system.</summary>
    internal static string SafeFileName(string? name)
    {
        var n = (name ?? "").Replace('\\', '/');
        n = n[(n.LastIndexOf('/') + 1)..];
        n = new string(n.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return n.Length == 0 ? "image" : Parse.Truncate(n, 200);
    }

    private static MediaAssetDto ToDto(MediaAsset a, bool inUse)
        => new(a.Id, a.Purpose.ToString(), a.OriginalFileName, a.Width, a.Height, a.SizeBytes, a.CreatedAtUtc, inUse);
}
