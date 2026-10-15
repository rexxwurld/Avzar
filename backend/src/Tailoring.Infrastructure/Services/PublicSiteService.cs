using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Storefront;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Storefront;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>
/// What an anonymous visitor can reach. No tenant is in scope here, so every query names its business explicitly and
/// IgnoreQueryFilters is used on purpose. Only published snapshots and Public + Active designs ever leave this class.
/// </summary>
public sealed class PublicSiteService(AppDbContext db, IEntitlementService entitlements, IDataProtectionProvider dataProtection,
    IOptions<StorefrontOptions> options) : IPublicSiteService
{
    private static AppException SiteNotFound() => AppException.NotFound("site_not_found", "This site does not exist.");

    // ------------------------------------------------------------------ hosts

    public async Task<string?> ResolveHostAsync(string host, CancellationToken ct = default)
        => await VerifiedDomainSlugAsync(host, ct);

    public async Task<bool> IsDomainAllowedAsync(string host, CancellationToken ct = default)
        => await VerifiedDomainSlugAsync(host, ct) is not null;

    /// <summary>A custom domain works only while it is Verified AND the business's plan still includes custom domains.</summary>
    private async Task<string?> VerifiedDomainSlugAsync(string host, CancellationToken ct)
    {
        var h = host.Trim().TrimEnd('.').ToLowerInvariant();
        var row = await db.StorefrontDomains.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.Hostname == h && d.Status == DomainStatus.Verified)
            .Join(db.Storefronts.IgnoreQueryFilters(), d => d.StorefrontId, s => s.Id, (d, s) => new { s.Slug, s.BusinessId }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        return (await entitlements.GetAsync(row.BusinessId, ct)).Flag(EntitlementKeys.StorefrontCustomDomain) ? row.Slug : null;
    }

    // ------------------------------------------------------------------ the site itself

    public async Task<PublicSiteDto> GetSiteAsync(string slug, CancellationToken ct = default)
    {
        var (s, v) = await LiveAsync(slug, ct);
        return await BuildAsync(s, StorefrontJson.Deserialize(v.DocumentJson), v.VersionNumber, isPreview: false, ct);
    }

    public async Task<PublicSiteDto> GetPreviewAsync(string token, CancellationToken ct = default)
    {
        Guid id;
        try { id = Guid.ParseExact(StorefrontService.PreviewProtector(dataProtection).Unprotect(token), "N"); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        { throw AppException.NotFound("preview_expired", "This preview link has expired. Open the preview again from the builder."); }

        var s = await db.Storefronts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw SiteNotFound();
        return await BuildAsync(s, StorefrontJson.Deserialize(s.DraftJson), 0, isPreview: true, ct);
    }

    private async Task<PublicSiteDto> BuildAsync(Storefront s, StorefrontDocument doc, int versionNumber, bool isPreview, CancellationToken ct)
    {
        var business = await db.Businesses.IgnoreQueryFilters().AsNoTracking().Where(b => b.Id == s.BusinessId)
            .Select(b => new { b.Name, b.CurrencyCode }).FirstAsync(ct);

        // Hidden pages and sections, and the archive, are the owner's business: they are not sent to visitors.
        doc.Archive = new();
        doc.Pages = doc.Pages.Where(p => p.Visible).ToList();
        foreach (var p in doc.Pages) p.Sections = p.Sections.Where(x => x.Visible).ToList();
        var visible = doc.Pages.Select(p => p.Id).ToHashSet();
        doc.Navigation = doc.Navigation.Where(n => n.Visible && visible.Contains(n.PageId)).ToList();

        return new PublicSiteDto(s.Name, s.Slug, business.Name, business.CurrencyCode, doc.TemplateKey, versionNumber, isPreview,
            JsonSerializer.SerializeToElement(doc, StorefrontJson.Options), ResolvedTheme.From(doc.Theme),
            new PublicContactDto(s.ContactEmail, s.ContactPhone, s.Address, s.OpeningHours), s.EnabledWorkflows(), s.AllowSearchIndexing && !isPreview);
    }

    // ------------------------------------------------------------------ catalog data

    public async Task<PagedResult<PublicDesignSummaryDto>> ListDesignsAsync(string slug, PublicDesignQuery query, CancellationToken ct = default)
    {
        var s = await LiveStorefrontAsync(slug, ct);
        var q = PublicDesigns(s.BusinessId);
        if (query.CategoryId is { } cid) q = q.Where(d => d.CategoryId == cid);
        if (query.Featured is { } f) q = q.Where(d => d.IsFeatured == f);
        if (!string.IsNullOrWhiteSpace(query.Kind)) { var k = Parse.Value<DesignKind>(query.Kind, "kind"); q = q.Where(d => d.Kind == k); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{Parse.Like(query.Search.Trim())}%";
            q = q.Where(d => EF.Functions.ILike(d.Name, pattern) || (d.Description != null && EF.Functions.ILike(d.Description, pattern))
                || (d.GarmentType != null && EF.Functions.ILike(d.GarmentType, pattern)));
        }

        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(d => d.IsFeatured).ThenBy(d => d.Name)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<PublicDesignSummaryDto>(await ToSummariesAsync(page, s.BusinessId, ct), query.Page, query.PageSize, total);
    }

    public async Task<PublicDesignDto> GetDesignAsync(string slug, string designSlug, CancellationToken ct = default)
    {
        var s = await LiveStorefrontAsync(slug, ct);
        var d = await PublicDesigns(s.BusinessId).FirstOrDefaultAsync(x => x.Slug == designSlug, ct)
            ?? throw AppException.NotFound("design_not_found", "This design is not available.");
        var category = d.CategoryId is { } c
            ? await db.DesignCategories.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == c && x.BusinessId == s.BusinessId).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var images = await db.DesignImages.IgnoreQueryFilters().AsNoTracking().Where(i => i.DesignId == d.Id && i.BusinessId == s.BusinessId)
            .OrderBy(i => i.SortOrder).Select(i => new PublicImageDto(i.MediaAssetId, i.AltText ?? d.Name)).ToListAsync(ct);
        return new PublicDesignDto(d.Id, d.Name, d.Slug, d.Description, d.Kind.ToString(), d.CategoryId, category, d.GarmentType, d.Tags, d.Fabrics,
            d.Colours, d.Sizes, d.BasePrice, d.CurrencyCode, d.ProductionDays, d.IsFeatured, images);
    }

    public async Task<IReadOnlyList<PublicCategoryDto>> ListCategoriesAsync(string slug, CancellationToken ct = default)
    {
        var s = await LiveStorefrontAsync(slug, ct);
        var used = await PublicDesigns(s.BusinessId).Where(d => d.CategoryId != null).Select(d => d.CategoryId!.Value).Distinct().ToListAsync(ct);
        return await db.DesignCategories.IgnoreQueryFilters().AsNoTracking().Where(c => c.BusinessId == s.BusinessId && used.Contains(c.Id))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new PublicCategoryDto(c.Id, c.Name)).ToListAsync(ct);
    }

    public async Task<PublicCollectionDto> GetCollectionAsync(string slug, string collectionSlug, CancellationToken ct = default)
    {
        var s = await LiveStorefrontAsync(slug, ct);
        // Site documents reference a collection by id; links and sitemaps use its slug. Accept either.
        var byId = Guid.TryParse(collectionSlug, out var collectionId);
        var col = await db.DesignCollections.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(c => c.BusinessId == s.BusinessId && (byId ? c.Id == collectionId : c.Slug == collectionSlug) && c.Visibility == DesignVisibility.Public, ct)
            ?? throw AppException.NotFound("collection_not_found", "This collection is not available.");

        var order = await db.DesignCollectionItems.IgnoreQueryFilters().AsNoTracking().Where(i => i.CollectionId == col.Id && i.BusinessId == s.BusinessId)
            .OrderBy(i => i.SortOrder).Select(i => i.DesignId).ToListAsync(ct);
        var designs = await PublicDesigns(s.BusinessId).Where(d => order.Contains(d.Id)).ToListAsync(ct);
        var ordered = order.Select(id => designs.FirstOrDefault(d => d.Id == id)).OfType<Design>().ToList(); // private/archived members simply are not shown
        return new PublicCollectionDto(col.Id, col.Name, col.Slug, col.Description, await ToSummariesAsync(ordered, s.BusinessId, ct));
    }

    // ------------------------------------------------------------------ SEO

    public async Task<string> SitemapXmlAsync(string slug, string baseUrl, CancellationToken ct = default)
    {
        var (s, v) = await LiveAsync(slug, ct);
        baseUrl = await OwnBaseUrlAsync(slug, baseUrl, ct);
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        if (!s.AllowSearchIndexing) return sb.Append("</urlset>").ToString();

        var root = baseUrl.TrimEnd('/');
        void Url(string path, DateTime? last = null)
            => sb.Append("  <url><loc>").Append(SecurityElement.Escape(root + path)).Append("</loc>")
                 .Append(last is { } d ? $"<lastmod>{d:yyyy-MM-dd}</lastmod>" : "").Append("</url>\n");

        var doc = StorefrontJson.Deserialize(v.DocumentJson);
        foreach (var p in doc.Pages.Where(p => p.Visible && p.Kind != "design")) Url(p.Slug == "" ? "/" : "/" + p.Slug, v.CreatedAtUtc);
        foreach (var d in await PublicDesigns(s.BusinessId).OrderBy(d => d.Name).Take(5000).Select(d => new { d.Slug, d.UpdatedAtUtc }).ToListAsync(ct))
            Url("/design/" + d.Slug, d.UpdatedAtUtc);
        foreach (var c in await db.DesignCollections.IgnoreQueryFilters().AsNoTracking()
                     .Where(c => c.BusinessId == s.BusinessId && c.Visibility == DesignVisibility.Public).Select(c => c.Slug).ToListAsync(ct))
            Url("/collection/" + c);
        return sb.Append("</urlset>").ToString();
    }

    public async Task<string> RobotsTxtAsync(string slug, string baseUrl, CancellationToken ct = default)
    {
        var s = await LiveStorefrontAsync(slug, ct);
        baseUrl = await OwnBaseUrlAsync(slug, baseUrl, ct);
        return s.AllowSearchIndexing
            ? $"User-agent: *\nAllow: /\nSitemap: {baseUrl.TrimEnd('/')}/sitemap.xml\n"
            : "User-agent: *\nDisallow: /\n";
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A sitemap may only name the site's own address: its subdomain or one of its verified custom domains.</summary>
    private async Task<string> OwnBaseUrlAsync(string slug, string baseUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || uri.PathAndQuery != "/")
            throw AppException.BadRequest("invalid_base_url", "baseUrl must be the site's own https address.");
        var host = uri.Host.ToLowerInvariant();
        var own = host == $"{slug}.{options.Value.BaseDomain}" || await VerifiedDomainSlugAsync(host, ct) == slug;
        if (!own) throw AppException.BadRequest("invalid_base_url", "baseUrl must be the site's own https address.");
        return $"https://{host}";
    }

    private IQueryable<Design> PublicDesigns(Guid businessId)
        => db.Designs.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.BusinessId == businessId && d.Status == DesignStatus.Active && d.Visibility == DesignVisibility.Public);

    private async Task<List<PublicDesignSummaryDto>> ToSummariesAsync(List<Design> designs, Guid businessId, CancellationToken ct)
    {
        var ids = designs.Select(d => d.Id).ToList();
        var covers = (await db.DesignImages.IgnoreQueryFilters().AsNoTracking().Where(i => ids.Contains(i.DesignId) && i.BusinessId == businessId)
            .OrderBy(i => i.SortOrder).ToListAsync(ct)).GroupBy(i => i.DesignId).ToDictionary(g => g.Key, g => g.First());
        var catIds = designs.Select(d => d.CategoryId).OfType<Guid>().Distinct().ToList();
        var cats = await db.DesignCategories.IgnoreQueryFilters().AsNoTracking().Where(c => catIds.Contains(c.Id) && c.BusinessId == businessId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return designs.Select(d => new PublicDesignSummaryDto(d.Id, d.Name, d.Slug, d.Kind.ToString(), d.CategoryId is { } c ? cats.GetValueOrDefault(c) : null,
            d.BasePrice, d.CurrencyCode, d.IsFeatured, covers.GetValueOrDefault(d.Id)?.MediaAssetId, covers.GetValueOrDefault(d.Id)?.AltText ?? d.Name)).ToList();
    }

    private async Task<Storefront> LiveStorefrontAsync(string slug, CancellationToken ct)
        => await db.Storefronts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.Status == StorefrontStatus.Published, ct) ?? throw SiteNotFound();

    private async Task<(Storefront S, StorefrontVersion V)> LiveAsync(string slug, CancellationToken ct)
    {
        var s = await LiveStorefrontAsync(slug, ct);
        var v = await db.StorefrontVersions.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == s.PublishedVersionId, ct);
        return (s, v);
    }
}
