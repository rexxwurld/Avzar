using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tailoring.Api.Security;
using Tailoring.Application.Common;
using Tailoring.Application.Storefront;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

/// <summary>The owner's side: everything here is tenant-scoped and permission-guarded.</summary>
[ApiController]
public class StorefrontsController(IStorefrontService sites) : ControllerBase
{
    /// <summary>Templates, themes, fonts, section types and presets, each marked with what the current plan allows.</summary>
    [HttpGet("api/storefront-builder/catalog"), HasPermission(Permissions.StorefrontView)]
    public async Task<BuilderCatalogDto> Catalog(CancellationToken ct) => await sites.CatalogAsync(ct);

    [HttpGet("api/storefronts"), HasPermission(Permissions.StorefrontView)]
    public async Task<IReadOnlyList<StorefrontSummaryDto>> List(CancellationToken ct) => await sites.ListAsync(ct);

    [HttpGet("api/storefronts/{id:guid}"), HasPermission(Permissions.StorefrontView)]
    public async Task<StorefrontDto> Get(Guid id, CancellationToken ct) => await sites.GetAsync(id, ct);

    [HttpPost("api/storefronts"), HasPermission(Permissions.StorefrontEdit)]
    public async Task<ActionResult<StorefrontDto>> Create(CreateStorefrontRequest request, CancellationToken ct)
    {
        var created = await sites.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("api/storefronts/{id:guid}/settings"), HasPermission(Permissions.StorefrontEdit)]
    public async Task<StorefrontDto> Settings(Guid id, UpdateStorefrontSettingsRequest request, CancellationToken ct) => await sites.UpdateSettingsAsync(id, request, ct);

    // ---- draft (never visible to visitors)

    [HttpGet("api/storefronts/{id:guid}/draft"), HasPermission(Permissions.StorefrontView)]
    public async Task<DraftDto> Draft(Guid id, CancellationToken ct) => await sites.GetDraftAsync(id, ct);

    /// <summary>Replace the whole draft. Needs the visual builder. 400 lists every invalid value; 403 lists what the plan does not allow.</summary>
    [HttpPut("api/storefronts/{id:guid}/draft"), HasPermission(Permissions.StorefrontEdit)]
    public async Task<DraftDto> SaveDraft(Guid id, SaveDraftRequest request, CancellationToken ct) => await sites.SaveDraftAsync(id, request, ct);

    /// <summary>Theme and branding only: available on plans without the full builder, within their level.</summary>
    [HttpPut("api/storefronts/{id:guid}/appearance"), HasPermission(Permissions.StorefrontEdit)]
    public async Task<DraftDto> Appearance(Guid id, SaveAppearanceRequest request, CancellationToken ct) => await sites.SaveAppearanceAsync(id, request, ct);

    [HttpPost("api/storefronts/{id:guid}/preview-link"), HasPermission(Permissions.StorefrontView)]
    public async Task<PreviewLinkDto> PreviewLink(Guid id, CancellationToken ct) => await sites.CreatePreviewLinkAsync(id, ct);

    // ---- template switching: preview first, then apply with the token from the preview

    [HttpGet("api/storefronts/{id:guid}/template-switch"), HasPermission(Permissions.StorefrontEdit)]
    public async Task<SwitchPlanDto> PreviewSwitch(Guid id, [FromQuery] string target, CancellationToken ct) => await sites.PreviewSwitchAsync(id, target, ct);

    [HttpPost("api/storefronts/{id:guid}/template-switch"), HasPermission(Permissions.StorefrontEdit)]
    public async Task<DraftDto> ApplySwitch(Guid id, ApplySwitchRequest request, CancellationToken ct) => await sites.ApplySwitchAsync(id, request, ct);

    // ---- publishing

    [HttpPost("api/storefronts/{id:guid}/publish"), HasPermission(Permissions.StorefrontPublish)]
    public async Task<PublishResultDto> Publish(Guid id, PublishRequest request, CancellationToken ct) => await sites.PublishAsync(id, request, ct);

    [HttpPost("api/storefronts/{id:guid}/unpublish"), HasPermission(Permissions.StorefrontPublish)]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct) { await sites.UnpublishAsync(id, ct); return NoContent(); }

    [HttpGet("api/storefronts/{id:guid}/versions"), HasPermission(Permissions.StorefrontView)]
    public async Task<IReadOnlyList<VersionDto>> Versions(Guid id, CancellationToken ct) => await sites.ListVersionsAsync(id, ct);

    [HttpGet("api/storefronts/{id:guid}/versions/{number:int}"), HasPermission(Permissions.StorefrontView)]
    public async Task<System.Text.Json.JsonElement> Version(Guid id, int number, CancellationToken ct) => await sites.GetVersionDocumentAsync(id, number, ct);

    [HttpPost("api/storefronts/{id:guid}/versions/{number:int}/rollback"), HasPermission(Permissions.StorefrontPublish)]
    public async Task<PublishResultDto> Rollback(Guid id, int number, PublishRequest request, CancellationToken ct) => await sites.RollbackAsync(id, number, request, ct);

    // ---- section presets

    [HttpGet("api/storefront-presets"), HasPermission(Permissions.StorefrontView)]
    public async Task<IReadOnlyList<SectionPresetDto>> Presets(CancellationToken ct) => await sites.ListPresetsAsync(ct);

    [HttpPost("api/storefront-presets"), HasPermission(Permissions.StorefrontEdit)]
    public async Task<ActionResult<SectionPresetDto>> CreatePreset(CreatePresetRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await sites.CreatePresetAsync(request, ct));

    [HttpDelete("api/storefront-presets/{id:guid}"), HasPermission(Permissions.StorefrontEdit)]
    public async Task<IActionResult> DeletePreset(Guid id, CancellationToken ct) { await sites.DeletePresetAsync(id, ct); return NoContent(); }

    // ---- custom domains

    [HttpGet("api/storefronts/{id:guid}/domains"), HasPermission(Permissions.StorefrontView)]
    public async Task<IReadOnlyList<DomainDto>> Domains(Guid id, CancellationToken ct) => await sites.ListDomainsAsync(id, ct);

    [HttpPost("api/storefronts/{id:guid}/domains"), HasPermission(Permissions.StorefrontPublish)]
    public async Task<ActionResult<DomainDto>> AddDomain(Guid id, AddDomainRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await sites.AddDomainAsync(id, request, ct));

    [HttpPost("api/storefronts/{id:guid}/domains/{domainId:guid}/verify"), HasPermission(Permissions.StorefrontPublish)]
    public async Task<DomainDto> VerifyDomain(Guid id, Guid domainId, CancellationToken ct) => await sites.VerifyDomainAsync(id, domainId, ct);

    [HttpDelete("api/storefronts/{id:guid}/domains/{domainId:guid}"), HasPermission(Permissions.StorefrontPublish)]
    public async Task<IActionResult> RemoveDomain(Guid id, Guid domainId, CancellationToken ct) { await sites.RemoveDomainAsync(id, domainId, ct); return NoContent(); }
}

/// <summary>Anonymous visitors. Only published snapshots and public designs are reachable.</summary>
[ApiController, Route("public")]
public class PublicSiteController(IPublicSiteService site, IInquiryService inquiries) : ControllerBase
{
    [HttpGet("sites/{slug}"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Site(string slug, CancellationToken ct) => Cached(await site.GetSiteAsync(slug, ct), 60);

    [HttpGet("preview/{token}"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Preview(string token, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await site.GetPreviewAsync(token, ct));
    }

    [HttpGet("sites/{slug}/designs"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Designs(string slug, [FromQuery] PublicDesignQuery query, CancellationToken ct) => Cached(await site.ListDesignsAsync(slug, query, ct), 60);

    [HttpGet("sites/{slug}/designs/{designSlug}"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Design(string slug, string designSlug, CancellationToken ct) => Cached(await site.GetDesignAsync(slug, designSlug, ct), 60);

    [HttpGet("sites/{slug}/categories"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Categories(string slug, CancellationToken ct) => Cached(await site.ListCategoriesAsync(slug, ct), 60);

    [HttpGet("sites/{slug}/collections/{collectionSlug}"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Collection(string slug, string collectionSlug, CancellationToken ct) => Cached(await site.GetCollectionAsync(slug, collectionSlug, ct), 60);

    /// <summary>baseUrl must be the site's own address (its subdomain or a verified custom domain).</summary>
    [HttpGet("sites/{slug}/sitemap.xml"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Sitemap(string slug, [FromQuery] string baseUrl, CancellationToken ct)
        => Content(await site.SitemapXmlAsync(slug, baseUrl, ct), "application/xml");

    [HttpGet("sites/{slug}/robots.txt"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Robots(string slug, [FromQuery] string baseUrl, CancellationToken ct)
        => Content(await site.RobotsTxtAsync(slug, baseUrl, ct), "text/plain");

    /// <summary>Which site does this custom domain show? 404 when unknown, unverified, or no longer in the plan.</summary>
    [HttpGet("resolve"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Resolve([FromQuery] string host, CancellationToken ct)
        => await site.ResolveHostAsync(host, ct) is { } slug ? Ok(new { slug }) : NotFound();

    /// <summary>For an on-demand-TLS proxy (e.g. Caddy "ask"): 200 only for verified domains, so certificates are never issued for strangers.</summary>
    [HttpGet("domains/allowed"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Allowed([FromQuery] string domain, CancellationToken ct)
        => await site.IsDomainAllowedAsync(domain, ct) ? Ok() : NotFound();

    /// <summary>Contact, inquiry, quotation, appointment and custom-order requests all arrive here whatever the layout.</summary>
    [HttpPost("sites/{slug}/inquiries"), AllowAnonymous, EnableRateLimiting("public-submit")]
    public async Task<ActionResult<InquiryAcceptedDto>> Submit(string slug, SubmitInquiryRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status202Accepted, await inquiries.SubmitAsync(slug, request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct));

    private IActionResult Cached<T>(T body, int seconds)
    {
        Response.Headers.CacheControl = $"public, max-age={seconds}";
        return Ok(body);
    }
}

[ApiController, Route("api/inquiries")]
public class InquiriesController(IInquiryService inquiries) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.InquiriesView)]
    public async Task<PagedResult<InquiryDto>> List([FromQuery] InquiryListQuery query, CancellationToken ct) => await inquiries.ListAsync(query, ct);

    [HttpGet("summary"), HasPermission(Permissions.InquiriesView)]
    public async Task<InquirySummaryDto> Summary(CancellationToken ct) => await inquiries.SummaryAsync(ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.InquiriesView)]
    public async Task<InquiryDto> Get(Guid id, CancellationToken ct) => await inquiries.GetAsync(id, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.InquiriesManage)]
    public async Task<InquiryDto> Update(Guid id, UpdateInquiryRequest request, CancellationToken ct) => await inquiries.UpdateAsync(id, request, ct);

    /// <summary>Creates a DRAFT order (and the customer if new). Needs orders to be creatable by the caller's plan.</summary>
    [HttpPost("{id:guid}/convert"), HasPermission(Permissions.InquiriesManage)]
    public async Task<InquiryDto> Convert(Guid id, ConvertInquiryRequest request, CancellationToken ct) => await inquiries.ConvertToOrderAsync(id, request, ct);
}
