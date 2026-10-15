using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Storefront;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Common;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Media;
using Tailoring.Domain.Storefront;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed partial class StorefrontService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, IEntitlementService entitlements,
    IDataProtectionProvider dataProtection, IDnsTxtResolver dns, IOptions<StorefrontOptions> options) : IStorefrontService
{
    private static readonly string[] ReservedSubdomains =
        ["www", "api", "admin", "app", "mail", "static", "assets", "cdn", "public", "dashboard", "login", "support", "help", "status", "blog", "docs", "preview"];

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{1,38})[a-z0-9]$")] private static partial Regex SubdomainRegex();
    [GeneratedRegex(@"^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}$")] private static partial Regex HostnameRegex();

    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");
    private StorefrontOptions Opt => options.Value;

    // ------------------------------------------------------------------ catalog + list

    public async Task<BuilderCatalogDto> CatalogAsync(CancellationToken ct = default)
    {
        var ent = await entitlements.GetAsync(BusinessId, ct);
        var advanced = ent.TierAtLeast(EntitlementKeys.StorefrontSectionLevel, "advanced");
        var templates = StorefrontTemplates.All.Select(t => new TemplateInfoDto(t.Key, t.Name, t.Description, t.Tier, t.DefaultPreset,
            ent.TierAtLeast(EntitlementKeys.StorefrontTemplateTier, t.Tier), t.Supported)).ToList();
        var themes = StorefrontCatalog.ThemePresets.Values.Select(p => new ThemePresetDto(p.Key, p.Name, p.Colors, p.HeadingFont, p.BodyFont)).ToList();
        var types = StorefrontCatalog.SectionTypes.Values.Select(d => new SectionTypeDto(d.Type, d.Label, d.MinLevel, d.AllVariants, d.MinLevel == "basic" || advanced, d.Props.Select(ToDto).ToList())).ToList();
        var maxPages = ent.Limit(EntitlementKeys.StorefrontPagesMax); var maxSites = ent.Limit(EntitlementKeys.StorefrontCountMax);
        var caps = new BuilderCapabilitiesDto(ent.Flag(EntitlementKeys.StorefrontBuilder), ent.Tier(EntitlementKeys.StorefrontThemeLevel),
            ent.Tier(EntitlementKeys.StorefrontBrandingLevel), ent.Tier(EntitlementKeys.StorefrontSectionLevel), ent.Tier(EntitlementKeys.StorefrontTemplateTier),
            ent.Flag(EntitlementKeys.StorefrontCustomPages), ent.Flag(EntitlementKeys.StorefrontCustomDomain),
            maxPages.IsUnlimited ? null : (int)maxPages.Max, maxSites.IsUnlimited ? null : (int)maxSites.Max);
        return new BuilderCatalogDto(templates, themes, StorefrontCatalog.Fonts, types, await ListPresetsAsync(ct), caps);
    }

    public async Task<IReadOnlyList<StorefrontSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.Storefronts.AsNoTracking().OrderBy(s => s.CreatedAtUtc).ToListAsync(ct);
        var live = await LiveVersionsAsync(rows.Select(r => r.PublishedVersionId).OfType<Guid>().ToList(), ct);
        return rows.Select(s => new StorefrontSummaryDto(s.Id, s.Name, s.Slug, s.Status.ToString(), s.TemplateKey,
            s.PublishedVersionId is { } v ? live.GetValueOrDefault(v)?.VersionNumber : null, s.DraftRevision, HasUnpublished(s, live), s.UpdatedAtUtc)).ToList();
    }

    public async Task<StorefrontDto> GetAsync(Guid id, CancellationToken ct = default)
        => await ToDtoAsync(await LoadAsync(id, track: false, ct), ct);

    // ------------------------------------------------------------------ create + settings

    public async Task<StorefrontDto> CreateAsync(CreateStorefrontRequest req, CancellationToken ct = default)
    {
        var template = StorefrontTemplates.Find(req.TemplateKey) ?? throw AppException.BadRequest("unknown_template", "That template does not exist.");
        await entitlements.RequireTierAsync(BusinessId, EntitlementKeys.StorefrontTemplateTier, template.Tier, ct);

        var name = req.Name.Trim();
        var doc = template.Build(name);
        var ent = await entitlements.GetAsync(BusinessId, ct);
        if (StorefrontPolicy.Violations(doc, ent) is { Count: > 0 } issues) throw PlanIssues(issues);

        var storefront = new Storefront
        {
            BusinessId = BusinessId, Name = name, TemplateKey = template.Key, DraftJson = StorefrontJson.Serialize(doc), DraftRevision = 1,
            ContactEmail = null,
        };
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await entitlements.EnsureBelowLimitAsync(BusinessId, EntitlementKeys.StorefrontCountMax, c => db.Storefronts.LongCountAsync(c), ct);
            storefront.Slug = await ChooseSlugAsync(req.Slug, name, ct);
            db.Storefronts.Add(storefront);
            audit.Record(BusinessId, tenant.UserId, "storefront.created", nameof(Storefront), storefront.Id, new { storefront.Slug, template = template.Key });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw AppException.Conflict("slug_taken", "That web address is already taken. Choose another.");
        }
        return await ToDtoAsync(storefront, ct);
    }

    public async Task<StorefrontDto> UpdateSettingsAsync(Guid id, UpdateStorefrontSettingsRequest req, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, track: true, ct);
        if (s.Version != req.Version) throw AppException.Conflict("stale_version", "This site was changed by someone else. Reload and try again.");

        s.Name = req.Name.Trim();
        s.ContactEmail = Parse.Clean(req.ContactEmail); s.ContactPhone = Parse.Clean(req.ContactPhone);
        s.Address = Parse.Clean(req.Address); s.OpeningHours = Parse.Clean(req.OpeningHours);
        foreach (var text in new[] { s.Name, s.ContactPhone, s.Address, s.OpeningHours }.OfType<string>())
            if (text.Contains('<') || text.Contains('>')) throw AppException.BadRequest("invalid_text", "The characters < and > are not allowed.");
        s.WorkflowInquiry = req.WorkflowInquiry; s.WorkflowBooking = req.WorkflowBooking;
        s.WorkflowCustomOrder = req.WorkflowCustomOrder; s.WorkflowContact = req.WorkflowContact;
        s.AllowSearchIndexing = req.AllowSearchIndexing;

        db.Entry(s).Property(x => x.Version).OriginalValue = req.Version!.Value;
        s.UpdatedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, "storefront.settings_changed", nameof(Storefront), s.Id);
        await SaveAsync(ct);
        return await ToDtoAsync(s, ct);
    }

    // ------------------------------------------------------------------ draft

    public async Task<DraftDto> GetDraftAsync(Guid id, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, track: false, ct);
        return ToDraftDto(s, StorefrontJson.Deserialize(s.DraftJson), await entitlements.GetAsync(BusinessId, ct));
    }

    public async Task<DraftDto> SaveDraftAsync(Guid id, SaveDraftRequest req, CancellationToken ct = default)
    {
        await entitlements.RequireFlagAsync(BusinessId, EntitlementKeys.StorefrontBuilder, ct);
        var s = await LoadAsync(id, track: true, ct);
        RequireRevision(s, req.ExpectedRevision);

        var doc = ParseDocument(req.Document);
        var previous = StorefrontJson.Deserialize(s.DraftJson);
        var ent = await entitlements.GetAsync(BusinessId, ct);
        await ValidateAsync(doc, previous, ent, checkCollectionsAndCategories: true, ct);
        return await StoreDraftAsync(s, doc, ent, "storefront.draft_saved", ct);
    }

    public async Task<DraftDto> SaveAppearanceAsync(Guid id, SaveAppearanceRequest req, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, track: true, ct);
        RequireRevision(s, req.ExpectedRevision);

        var previous = StorefrontJson.Deserialize(s.DraftJson);
        var doc = StorefrontJson.Clone(previous);
        try
        {
            doc.Theme = req.Theme.Deserialize<ThemeSettings>(StorefrontJson.Options) ?? throw new JsonException("Empty theme.");
            doc.Branding = req.Branding.Deserialize<BrandingSettings>(StorefrontJson.Options) ?? throw new JsonException("Empty branding.");
        }
        catch (JsonException ex) { throw AppException.BadRequest("invalid_document", $"The theme or branding could not be read: {ex.Message}"); }

        var ent = await entitlements.GetAsync(BusinessId, ct);
        await ValidateAsync(doc, previous, ent, checkCollectionsAndCategories: false, ct);
        return await StoreDraftAsync(s, doc, ent, "storefront.appearance_saved", ct);
    }

    // ------------------------------------------------------------------ template switching

    public async Task<SwitchPlanDto> PreviewSwitchAsync(Guid id, string targetTemplate, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, track: false, ct);
        var target = StorefrontTemplates.Find(targetTemplate) ?? throw AppException.BadRequest("unknown_template", "That template does not exist.");
        await entitlements.RequireTierAsync(BusinessId, EntitlementKeys.StorefrontTemplateTier, target.Tier, ct);
        return ToDto(TemplateSwitcher.Plan(StorefrontJson.Deserialize(s.DraftJson), target));
    }

    public async Task<DraftDto> ApplySwitchAsync(Guid id, ApplySwitchRequest req, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, track: true, ct);
        RequireRevision(s, req.ExpectedRevision);
        var target = StorefrontTemplates.Find(req.TargetTemplate) ?? throw AppException.BadRequest("unknown_template", "That template does not exist.");
        await entitlements.RequireTierAsync(BusinessId, EntitlementKeys.StorefrontTemplateTier, target.Tier, ct);

        var previous = StorefrontJson.Deserialize(s.DraftJson);
        var plan = TemplateSwitcher.Plan(previous, target);
        if (plan.Token != req.Token)
            throw AppException.Conflict("plan_changed", "The site changed since you previewed this switch. Preview it again before applying.");

        var doc = TemplateSwitcher.Apply(previous, target, req.UseTemplateTheme);
        var ent = await entitlements.GetAsync(BusinessId, ct);
        await ValidateAsync(doc, previous, ent, checkCollectionsAndCategories: false, ct);
        return await StoreDraftAsync(s, doc, ent, "storefront.template_switched", ct, new { from = previous.TemplateKey, to = target.Key, archived = plan.Archived.Count, converted = plan.Converted.Count });
    }

    // ------------------------------------------------------------------ publish, versions, rollback

    public async Task<PublishResultDto> PublishAsync(Guid id, PublishRequest req, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, track: true, ct);
        var doc = StorefrontJson.Deserialize(s.DraftJson);
        return await PublishDocumentAsync(s, doc, req.Note, rolledBackFrom: null, checkCollectionsAndCategories: true, draftToStore: null, ct);
    }

    public async Task UnpublishAsync(Guid id, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, track: true, ct);
        if (s.Status != StorefrontStatus.Published) throw AppException.Conflict("not_published", "This site is not published.");
        s.Status = StorefrontStatus.Unpublished; // the versions stay; publishing again creates a new one
        s.UpdatedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, "storefront.unpublished", nameof(Storefront), s.Id);
        await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<VersionDto>> ListVersionsAsync(Guid id, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, track: false, ct);
        var rows = await db.StorefrontVersions.AsNoTracking().Where(v => v.StorefrontId == id).OrderByDescending(v => v.VersionNumber).ToListAsync(ct);
        return rows.Select(v => ToDto(v, s)).ToList();
    }

    public async Task<JsonElement> GetVersionDocumentAsync(Guid id, int versionNumber, CancellationToken ct = default)
    {
        await LoadAsync(id, track: false, ct);
        var v = await db.StorefrontVersions.AsNoTracking().FirstOrDefaultAsync(x => x.StorefrontId == id && x.VersionNumber == versionNumber, ct)
            ?? throw AppException.NotFound("version_not_found", "That version does not exist.");
        return JsonDocument.Parse(v.DocumentJson).RootElement.Clone();
    }

    public async Task<PublishResultDto> RollbackAsync(Guid id, int versionNumber, PublishRequest req, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, track: true, ct);
        var old = await db.StorefrontVersions.AsNoTracking().FirstOrDefaultAsync(x => x.StorefrontId == id && x.VersionNumber == versionNumber, ct)
            ?? throw AppException.NotFound("version_not_found", "That version does not exist.");
        var doc = StorefrontJson.Deserialize(old.DocumentJson); // exactly what was published, byte for byte in meaning
        return await PublishDocumentAsync(s, doc, req.Note ?? $"Rolled back to version {versionNumber}", rolledBackFrom: versionNumber,
            checkCollectionsAndCategories: false, draftToStore: doc, ct);
    }

    // ------------------------------------------------------------------ section presets

    public async Task<IReadOnlyList<SectionPresetDto>> ListPresetsAsync(CancellationToken ct = default)
    {
        var builtIn = StorefrontPresets.All.Select(p => new SectionPresetDto(p.Key, p.Name, p.SectionType, p.Variant, StorefrontPresets.PropsOf(p),
            JsonSerializer.SerializeToElement(p.Style, StorefrontJson.Options), true));
        var custom = (await db.SectionPresets.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct)).Select(p => new SectionPresetDto(
            p.Id.ToString(), p.Name, p.SectionType, p.Variant, JsonDocument.Parse(p.PropsJson).RootElement.Clone(),
            JsonDocument.Parse(p.StyleJson).RootElement.Clone(), false));
        return builtIn.Concat(custom).ToList();
    }

    public async Task<SectionPresetDto> CreatePresetAsync(CreatePresetRequest req, CancellationToken ct = default)
    {
        var ent = await entitlements.GetAsync(BusinessId, ct);
        if (!ent.Flag(EntitlementKeys.StorefrontBuilder)) await entitlements.RequireFlagAsync(BusinessId, EntitlementKeys.StorefrontBuilder, ct);

        var style = req.Style is { ValueKind: JsonValueKind.Object } st ? st.Deserialize<SectionStyle>(StorefrontJson.Options) ?? new SectionStyle() : new SectionStyle();
        var section = new SectionDoc { Id = Guid.NewGuid(), Type = req.SectionType, Variant = req.Variant, Props = req.Props, Style = style };
        var check = StorefrontValidator.ValidateSectionAlone(section);
        if (check.MediaIds.Count > 0) check.Errors.Add(new ValidationError("props", "Presets cannot contain images. Add images after placing the section."));
        if (!check.IsValid) throw InvalidDocument(check.Errors);
        if (StorefrontPolicy.Violations(new StorefrontDocument { Pages = [new PageDoc { Id = Guid.NewGuid(), Kind = "home", Sections = [section] }] }, ent)
                .Any(v => v.Code.StartsWith("section", StringComparison.Ordinal)))
            throw AppException.Forbidden("feature_not_in_plan", "That kind of section is not included in your plan.");

        var name = req.Name.Trim();
        if (await db.SectionPresets.CountAsync(ct) >= 50) throw AppException.Conflict("too_many_presets", "You can save up to 50 presets.");
        if (await db.SectionPresets.AnyAsync(p => p.Name.ToLower() == name.ToLower(), ct)) throw AppException.Conflict("preset_exists", "You already have a preset with this name.");

        var row = new SectionPreset
        {
            BusinessId = BusinessId, Name = name, SectionType = section.Type, Variant = section.Variant, CreatedByUserId = tenant.UserId,
            PropsJson = req.Props.GetRawText(), StyleJson = JsonSerializer.Serialize(style, StorefrontJson.Options),
        };
        db.SectionPresets.Add(row);
        audit.Record(BusinessId, tenant.UserId, "storefront.preset_created", nameof(SectionPreset), row.Id, new { name });
        await db.SaveChangesAsync(ct);
        return new SectionPresetDto(row.Id.ToString(), row.Name, row.SectionType, row.Variant, JsonDocument.Parse(row.PropsJson).RootElement.Clone(),
            JsonDocument.Parse(row.StyleJson).RootElement.Clone(), false);
    }

    public async Task DeletePresetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.SectionPresets.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("preset_not_found", "Preset not found.");
        db.SectionPresets.Remove(p);
        audit.Record(BusinessId, tenant.UserId, "storefront.preset_deleted", nameof(SectionPreset), id, new { p.Name });
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ custom domains

    public async Task<IReadOnlyList<DomainDto>> ListDomainsAsync(Guid storefrontId, CancellationToken ct = default)
    {
        await LoadAsync(storefrontId, track: false, ct);
        return (await db.StorefrontDomains.AsNoTracking().Where(d => d.StorefrontId == storefrontId).OrderBy(d => d.CreatedAtUtc).ToListAsync(ct)).Select(ToDomainDto).ToList();
    }

    public async Task<DomainDto> AddDomainAsync(Guid storefrontId, AddDomainRequest req, CancellationToken ct = default)
    {
        await entitlements.RequireFlagAsync(BusinessId, EntitlementKeys.StorefrontCustomDomain, ct);
        await LoadAsync(storefrontId, track: false, ct);

        var host = req.Hostname.Trim().TrimEnd('.').ToLowerInvariant();
        if (!HostnameRegex().IsMatch(host) || host.Length > 253)
            throw AppException.BadRequest("invalid_hostname", "Enter a domain like shop.example.com (no http://, no path).");
        if (host == Opt.BaseDomain || host.EndsWith("." + Opt.BaseDomain, StringComparison.Ordinal))
            throw AppException.BadRequest("reserved_hostname", $"Domains under {Opt.BaseDomain} are managed by Avzar.");
        if (await db.StorefrontDomains.CountAsync(d => d.StorefrontId == storefrontId, ct) >= 3)
            throw AppException.Conflict("too_many_domains", "A site can have up to 3 custom domains.");
        if (await db.StorefrontDomains.IgnoreQueryFilters().AnyAsync(d => d.Hostname == host, ct))
            throw AppException.Conflict("domain_taken", "That domain is already connected to a site.");

        var domain = new StorefrontDomain
        {
            BusinessId = BusinessId, StorefrontId = storefrontId, Hostname = host,
            VerificationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
        };
        db.StorefrontDomains.Add(domain);
        audit.Record(BusinessId, tenant.UserId, "storefront.domain_added", nameof(StorefrontDomain), domain.Id, new { host });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw AppException.Conflict("domain_taken", "That domain is already connected to a site."); }
        return ToDomainDto(domain);
    }

    public async Task<DomainDto> VerifyDomainAsync(Guid storefrontId, Guid domainId, CancellationToken ct = default)
    {
        await LoadAsync(storefrontId, track: false, ct);
        var d = await db.StorefrontDomains.FirstOrDefaultAsync(x => x.Id == domainId && x.StorefrontId == storefrontId, ct)
            ?? throw AppException.NotFound("domain_not_found", "Domain not found.");
        if (d.LastCheckedAtUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromSeconds(20))
            throw AppException.Conflict("too_soon", "Wait a few seconds before checking again. DNS changes can also take time to spread.");

        d.LastCheckedAtUtc = DateTime.UtcNow;
        IReadOnlyList<string> records;
        try { records = await dns.LookupTxtAsync($"_avzar-verify.{d.Hostname}", ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            d.Status = DomainStatus.Failed; d.LastError = "The DNS lookup failed. Try again in a minute.";
            await db.SaveChangesAsync(ct);
            return ToDomainDto(d);
        }

        if (records.Any(r => string.Equals(r.Trim().Trim('"'), d.VerificationToken, StringComparison.OrdinalIgnoreCase)))
        {
            d.Status = DomainStatus.Verified; d.VerifiedAtUtc = DateTime.UtcNow; d.LastError = null;
            audit.Record(BusinessId, tenant.UserId, "storefront.domain_verified", nameof(StorefrontDomain), d.Id, new { d.Hostname });
        }
        else { d.Status = DomainStatus.Failed; d.LastError = "We could not find the verification record yet."; }
        await db.SaveChangesAsync(ct);
        return ToDomainDto(d);
    }

    public async Task RemoveDomainAsync(Guid storefrontId, Guid domainId, CancellationToken ct = default)
    {
        await LoadAsync(storefrontId, track: false, ct);
        var d = await db.StorefrontDomains.FirstOrDefaultAsync(x => x.Id == domainId && x.StorefrontId == storefrontId, ct)
            ?? throw AppException.NotFound("domain_not_found", "Domain not found.");
        db.StorefrontDomains.Remove(d);
        audit.Record(BusinessId, tenant.UserId, "storefront.domain_removed", nameof(StorefrontDomain), d.Id, new { d.Hostname });
        await db.SaveChangesAsync(ct);
    }

    public async Task<PreviewLinkDto> CreatePreviewLinkAsync(Guid id, CancellationToken ct = default)
    {
        await LoadAsync(id, track: false, ct);
        var expires = DateTimeOffset.UtcNow.AddMinutes(Opt.PreviewMinutes);
        var token = PreviewProtector(dataProtection).Protect(id.ToString("N"), expires);
        return new PreviewLinkDto(token, expires.UtcDateTime);
    }

    internal static ITimeLimitedDataProtector PreviewProtector(IDataProtectionProvider provider)
        => provider.CreateProtector("avzar.storefront.preview.v1").ToTimeLimitedDataProtector();

    // ------------------------------------------------------------------ shared helpers

    private async Task<PublishResultDto> PublishDocumentAsync(Storefront s, StorefrontDocument doc, string? note, int? rolledBackFrom,
        bool checkCollectionsAndCategories, StorefrontDocument? draftToStore, CancellationToken ct)
    {
        var ent = await entitlements.GetAsync(BusinessId, ct);
        var check = await ValidateAsync(doc, previous: null, ent, checkCollectionsAndCategories, ct, requireNoViolations: true);

        if (StorefrontPolicy.MissingEntryPoints(doc, s.EnabledWorkflows()) is { Count: > 0 } missing)
            throw new AppException(409, "missing_entry_points",
                "Customers could not reach: " + string.Join(", ", missing.Select(WorkflowLabel)) +
                ". Add the matching section to a visible page, or switch that workflow off in the site settings.",
                new Dictionary<string, object?> { ["workflows"] = missing });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Locks.AcquireAsync(db, $"storefront-publish:{s.Id:N}", ct);
        var next = (await db.StorefrontVersions.Where(v => v.StorefrontId == s.Id).MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0) + 1;

        var version = new StorefrontVersion
        {
            BusinessId = BusinessId, StorefrontId = s.Id, VersionNumber = next, SchemaVersion = doc.SchemaVersion, TemplateKey = doc.TemplateKey,
            DocumentJson = StorefrontJson.Serialize(doc), Note = Parse.Clean(note), PublishedByUserId = tenant.UserId, RolledBackFromVersion = rolledBackFrom,
        };
        db.StorefrontVersions.Add(version);
        foreach (var mediaId in check.MediaIds)
            db.StorefrontVersionMedia.Add(new StorefrontVersionMedia { BusinessId = BusinessId, VersionId = version.Id, MediaAssetId = mediaId });

        s.PublishedVersionId = version.Id; s.Status = StorefrontStatus.Published; s.UpdatedAtUtc = DateTime.UtcNow;
        if (draftToStore is not null)
        {
            s.DraftJson = StorefrontJson.Serialize(draftToStore); s.TemplateKey = draftToStore.TemplateKey; s.DraftRevision++;
        }
        audit.Record(BusinessId, tenant.UserId, rolledBackFrom is null ? "storefront.published" : "storefront.rolled_back", nameof(Storefront), s.Id,
            new { version = next, rolledBackFrom });
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return new PublishResultDto(ToDto(version, s), ContrastWarnings(doc));
    }

    private static string WorkflowLabel(string w) => w switch
    {
        "inquiry" => "design inquiries", "booking" => "appointment booking", "customOrder" => "custom orders", "contact" => "contact", _ => w,
    };

    /// <summary>Structure, references and plan. Throws a 400 listing every problem, or a 403 listing plan problems.</summary>
    private async Task<DocumentCheck> ValidateAsync(StorefrontDocument doc, StorefrontDocument? previous, EffectiveEntitlements ent,
        bool checkCollectionsAndCategories, CancellationToken ct, bool requireNoViolations = false)
    {
        var check = StorefrontValidator.Validate(doc);
        if (check.IsValid) await CheckReferencesAsync(check, checkCollectionsAndCategories, ct);
        if (!check.IsValid) throw InvalidDocument(check.Errors);

        var issues = requireNoViolations ? StorefrontPolicy.Violations(doc, ent) : StorefrontPolicy.NewViolations(doc, previous, ent);
        if (issues.Count > 0) throw PlanIssues(issues);
        return check;
    }

    private async Task CheckReferencesAsync(DocumentCheck check, bool collectionsAndCategories, CancellationToken ct)
    {
        if (check.MediaIds.Count > 0)
        {
            var ids = check.MediaIds.ToList();
            var found = await db.MediaAssets.AsNoTracking().Where(a => ids.Contains(a.Id) && (a.Purpose == MediaPurpose.SiteImage || a.Purpose == MediaPurpose.DesignImage))
                .Select(a => a.Id).ToListAsync(ct);
            foreach (var missing in ids.Except(found)) check.Errors.Add(new ValidationError("media", $"Image {missing} was not found in your library."));
        }
        if (!collectionsAndCategories) return;
        if (check.CollectionIds.Count > 0)
        {
            var ids = check.CollectionIds.ToList();
            var found = await db.DesignCollections.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
            foreach (var missing in ids.Except(found)) check.Errors.Add(new ValidationError("collection", $"Collection {missing} was not found."));
        }
        if (check.CategoryIds.Count > 0)
        {
            var ids = check.CategoryIds.ToList();
            var found = await db.DesignCategories.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
            foreach (var missing in ids.Except(found)) check.Errors.Add(new ValidationError("category", $"Category {missing} was not found."));
        }
    }

    private static StorefrontDocument ParseDocument(JsonElement element)
    {
        try { return element.Deserialize<StorefrontDocument>(StorefrontJson.Options) ?? throw new JsonException("The document is empty."); }
        catch (JsonException ex) { throw AppException.BadRequest("invalid_document", $"The document could not be read: {ex.Message}"); }
        catch (NotSupportedException ex) { throw AppException.BadRequest("invalid_document", $"The document could not be read: {ex.Message}"); }
    }

    private async Task<DraftDto> StoreDraftAsync(Storefront s, StorefrontDocument doc, EffectiveEntitlements ent, string auditAction, CancellationToken ct, object? detail = null)
    {
        s.DraftJson = StorefrontJson.Serialize(doc);
        s.TemplateKey = doc.TemplateKey;
        s.DraftRevision++;
        s.UpdatedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, auditAction, nameof(Storefront), s.Id, detail ?? new { revision = s.DraftRevision });
        await SaveAsync(ct);
        return ToDraftDto(s, doc, ent);
    }

    private static void RequireRevision(Storefront s, int? expected)
    {
        if (expected != s.DraftRevision)
            throw AppException.Conflict("stale_revision", "Someone saved a newer version of this site. Reload it before saving.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("stale_version", "This site was changed by someone else. Reload and try again."); }
    }

    private async Task<Storefront> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = track ? db.Storefronts.AsQueryable() : db.Storefronts.AsNoTracking();
        return await q.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw AppException.NotFound("storefront_not_found", "Site not found.");
    }

    private async Task<string> ChooseSlugAsync(string? requested, string name, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var slug = requested.Trim().ToLowerInvariant();
            if (!SubdomainRegex().IsMatch(slug) || slug.Contains("--")) throw AppException.BadRequest("invalid_slug", "Use 3-40 lowercase letters, digits and single dashes.");
            if (ReservedSubdomains.Contains(slug)) throw AppException.BadRequest("reserved_slug", "That address is reserved.");
            if (await SlugExistsAsync(slug, ct)) throw AppException.Conflict("slug_taken", "That web address is already taken. Choose another.");
            return slug;
        }

        var baseSlug = Slug.From(name, "shop", 34);
        if (baseSlug.Length < 3) baseSlug = $"{baseSlug}-shop";
        if (ReservedSubdomains.Contains(baseSlug)) baseSlug = $"{baseSlug}-studio";
        if (!await SlugExistsAsync(baseSlug, ct)) return baseSlug;
        for (var n = 2; ; n++) if (!await SlugExistsAsync($"{baseSlug}-{n}", ct)) return $"{baseSlug}-{n}";
    }

    private Task<bool> SlugExistsAsync(string slug, CancellationToken ct) => db.Storefronts.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug, ct);

    private async Task<Dictionary<Guid, StorefrontVersion?>> LiveVersionsAsync(List<Guid> ids, CancellationToken ct)
        => (await db.StorefrontVersions.AsNoTracking().Where(v => ids.Contains(v.Id)).ToListAsync(ct)).ToDictionary(v => v.Id, v => (StorefrontVersion?)v);

    /// <summary>The draft differs from what is live when its document is not identical to the published snapshot.</summary>
    private bool HasUnpublished(Storefront s, Dictionary<Guid, StorefrontVersion?> live)
        => s.PublishedVersionId is not { } v || live.GetValueOrDefault(v) is not { } ver || !JsonEquivalent(s.DraftJson, ver.DocumentJson);

    private static bool JsonEquivalent(string a, string b)
    {
        try { return StorefrontJson.Serialize(StorefrontJson.Deserialize(a)) == StorefrontJson.Serialize(StorefrontJson.Deserialize(b)); }
        catch (JsonException) { return false; }
    }

    private async Task<StorefrontDto> ToDtoAsync(Storefront s, CancellationToken ct)
    {
        StorefrontVersion? live = s.PublishedVersionId is { } v ? await db.StorefrontVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == v, ct) : null;
        return new StorefrontDto(s.Id, s.Name, s.Slug, s.Status.ToString(), s.TemplateKey, live?.VersionNumber, s.DraftRevision,
            live is null || !JsonEquivalent(s.DraftJson, live.DocumentJson), s.ContactEmail, s.ContactPhone, s.Address, s.OpeningHours,
            s.WorkflowInquiry, s.WorkflowBooking, s.WorkflowCustomOrder, s.WorkflowContact, s.AllowSearchIndexing, s.Version, $"https://{s.Slug}.{Opt.BaseDomain}");
    }

    private static DraftDto ToDraftDto(Storefront s, StorefrontDocument doc, EffectiveEntitlements ent)
        => new(s.Id, s.DraftRevision, JsonSerializer.SerializeToElement(doc, StorefrontJson.Options), ResolvedTheme.From(doc.Theme),
            ContrastWarnings(doc), StorefrontPolicy.Violations(doc, ent).Select(v => new PolicyIssueDto(v.Code, v.Message)).ToList());

    internal static IReadOnlyList<ContrastWarningDto> ContrastWarnings(StorefrontDocument doc)
        => Contrast.Check(ResolvedTheme.From(doc.Theme).Colors).Select(w => new ContrastWarningDto(w.Foreground, w.Background, w.ForegroundHex,
            w.BackgroundHex, w.Ratio, w.Required, w.SuggestedForegroundHex)).ToList();

    private static PropSpecDto ToDto(PropSpec p)
        => new(p.Name, p.Kind.ToString(), p.Max, p.Min, p.Required, p.Multiline, p.ItemMax, p.Fields?.Select(ToDto).ToList());

    private static SwitchPlanDto ToDto(SwitchPlan p)
    {
        static List<SwitchChangeDto> Map(IEnumerable<SwitchChange> c) => c.Select(x => new SwitchChangeDto(x.SectionId, x.Type, x.FromVariant, x.ToVariant, x.Page)).ToList();
        return new SwitchPlanDto(p.FromTemplate, p.ToTemplate, p.NeedsConfirmation, Map(p.Kept), Map(p.Converted), Map(p.Archived), Map(p.Restored), p.Token);
    }

    private static VersionDto ToDto(StorefrontVersion v, Storefront s)
        => new(v.VersionNumber, v.TemplateKey, v.Note, v.CreatedAtUtc, v.PublishedByUserId, v.RolledBackFromVersion,
            s.Status == StorefrontStatus.Published && s.PublishedVersionId == v.Id);

    private static DomainDto ToDomainDto(StorefrontDomain d)
        => new(d.Id, d.Hostname, d.Status.ToString(), $"_avzar-verify.{d.Hostname}", d.VerificationToken, d.VerifiedAtUtc, d.LastCheckedAtUtc, d.LastError);

    private static AppException InvalidDocument(IEnumerable<ValidationError> errors)
    {
        var list = errors.Take(50).Select(e => new DocIssueDto(e.Path, e.Message)).ToList();
        return new AppException(400, "invalid_document", $"The site has {list.Count} problem(s). First: {list[0].Path}: {list[0].Message}",
            new Dictionary<string, object?> { ["errors"] = list });
    }

    private static AppException PlanIssues(IEnumerable<PolicyViolation> issues)
    {
        var list = issues.Select(i => new PolicyIssueDto(i.Code, i.Message)).ToList();
        return new AppException(403, "plan_violations", list[0].Message + (list.Count > 1 ? $" (and {list.Count - 1} more)" : "") + " Upgrade your plan or remove it.",
            new Dictionary<string, object?> { ["issues"] = list });
    }
}
