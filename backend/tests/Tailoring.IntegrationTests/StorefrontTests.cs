using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tailoring.Application.Common;
using Tailoring.Application.Designs;
using Tailoring.Application.Media;
using Tailoring.Application.Storefront;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Media;
using Tailoring.Domain.Storefront;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Tailoring.Infrastructure.Storage;
using Tailoring.Application.Abstractions;
using Xunit;

namespace Tailoring.IntegrationTests;

internal sealed class FakeDns : IDnsTxtResolver
{
    public Dictionary<string, string[]> Records { get; } = new();
    public bool Fail { get; set; }
    public Task<IReadOnlyList<string>> LookupTxtAsync(string name, CancellationToken ct = default)
        => Fail ? throw new InvalidOperationException("dns down") : Task.FromResult<IReadOnlyList<string>>(Records.GetValueOrDefault(name) ?? []);
}

internal sealed class FakeCaptcha(bool configured, bool passes) : ICaptchaVerifier
{
    public bool IsConfigured => configured;
    public Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken ct = default) => Task.FromResult(passes && token == "ok");
}

public sealed class StorefrontTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly EphemeralDataProtectionProvider Dp = new();
    private static readonly IOptions<StorefrontOptions> Opts = Options.Create(new StorefrontOptions { BaseDomain = "avzar.test" });
    private readonly FakeDns _dns = new();
    private readonly InMemoryObjectStorage _storage = new();

    // ------------------------------------------------------------------ setup helpers

    private async Task<Guid> BusinessAsync(string plan = "professional", params (string Key, string Value)[] overrides)
    {
        await using var db = fx.Create(null);
        var p = await db.Plans.FirstAsync(x => x.Code == plan);
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = p.Id });
        foreach (var (k, v) in overrides) db.EntitlementOverrides.Add(new TenantEntitlementOverride { BusinessId = b.Id, Key = k, Value = v, Reason = "test" });
        db.Locations.Add(new BusinessLocation { BusinessId = b.Id, Name = "Main" });
        await db.SaveChangesAsync();
        return b.Id;
    }

    private StorefrontService Sites(Guid bid) { var db = fx.Create(bid); return new StorefrontService(db, new FakeUserTenant(bid, Staff), new AuditLogger(db), new EntitlementService(db), Dp, _dns, Opts); }
    private PublicSiteService Public() { var db = fx.Create(null); return new PublicSiteService(db, new EntitlementService(db), Dp, Opts); }
    private DesignService Designs(Guid bid) { var db = fx.Create(bid); return new DesignService(db, new FakeUserTenant(bid, Staff), new AuditLogger(db), new EntitlementService(db)); }

    private MediaService Media(Guid? bid)
    {
        var o = Options.Create(new StorageOptions()); var db = fx.Create(bid);
        return new MediaService(db, new FakeUserTenant(bid ?? Guid.Empty, Staff), new AuditLogger(db), new EntitlementService(db), new ImageSharpProcessor(o), _storage, o);
    }

    private InquiryService Inquiries(Guid? bid, ICaptchaVerifier? captcha = null)
    {
        var db = fx.Create(bid);
        ITenantContext t = bid is { } b ? new FakeUserTenant(b, Staff) : new FakeTenant(null);
        var customers = new CustomerService(db, t, new AuditLogger(db), new EntitlementService(db));
        var orders = new OrderService(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new EntitlementService(db), new PricingService(db, t, new AuditLogger(db)), new AllowAllPermissions());
        return new InquiryService(db, t, new AuditLogger(db), captcha ?? new FakeCaptcha(false, true), customers, orders);
    }

    private async Task<Guid> SiteImageAsync(Guid bid, bool storeBytes = false)
    {
        await using var db = fx.Create(null);
        var asset = new MediaAsset { BusinessId = bid, Purpose = MediaPurpose.SiteImage, OriginalFileName = "photo.png", Width = 100, Height = 100, SizeBytes = 10, Sha256 = "abc" };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        if (storeBytes) foreach (var v in MediaVariants.All) await _storage.PutAsync(MediaVariants.KeyFor(bid, asset.Id, v), new byte[] { 1, 2, 3 }, "image/webp");
        return asset.Id;
    }

    private async Task<StorefrontDto> CreateAsync(Guid bid, string name = "Ada Atelier", string template = "standard", string? slug = null)
        => await Sites(bid).CreateAsync(new CreateStorefrontRequest { Name = name, TemplateKey = template, Slug = slug });

    private static StorefrontDocument ToDoc(DraftDto d) => d.Document.Deserialize<StorefrontDocument>(StorefrontJson.Options)!;
    private static JsonElement ToJson(StorefrontDocument d) => JsonSerializer.SerializeToElement(d, StorefrontJson.Options);
    private static JsonElement Props(object o) => JsonSerializer.SerializeToElement(o, StorefrontJson.Options);

    private async Task<DraftDto> EditAsync(Guid bid, Guid siteId, Action<StorefrontDocument> change)
    {
        var draft = await Sites(bid).GetDraftAsync(siteId);
        var doc = ToDoc(draft); change(doc);
        return await Sites(bid).SaveDraftAsync(siteId, new SaveDraftRequest { Document = ToJson(doc), ExpectedRevision = draft.Revision });
    }

    private static async Task<AppException> Fails(Func<Task> action) => await Assert.ThrowsAsync<AppException>(action);
    private static async Task<string> CodeOf(Func<Task> action) => (await Fails(action)).Code;
    private static SectionDoc First(StorefrontDocument d, string type) => d.Pages.SelectMany(p => p.Sections).First(s => s.Type == type);
    private static string Sig(JsonElement doc) => string.Join(">", doc.GetProperty("pages").EnumerateArray().SelectMany(p => p.GetProperty("sections").EnumerateArray()).Select(s => s.GetProperty("type").GetString() + ":" + s.GetProperty("variant").GetString()));

    private async Task<Guid> PublicDesignAsync(Guid bid, string name, bool publish = true)
    {
        var d = await Designs(bid).CreateAsync(new UpsertDesignRequest { Name = name, Kind = "ReadyMade", BasePrice = 5000m });
        if (publish) await Designs(bid).SetVisibilityAsync(d.Id, new SetDesignVisibilityRequest { Visibility = "Public" });
        return d.Id;
    }

    // ------------------------------------------------------------------ plans

    [Fact]
    public async Task Free_gets_the_standard_site_only_and_cannot_use_the_builder()
    {
        var bid = await BusinessAsync("free");
        Assert.Equal("feature_not_in_plan", await CodeOf(() => CreateAsync(bid, template: "luxury")));
        var site = await CreateAsync(bid);
        Assert.Matches("^ada-atelier(-\\d+)?$", site.Slug);
        Assert.Equal("Draft", site.Status);

        Assert.Equal("feature_not_in_plan", await CodeOf(() => EditAsync(bid, site.Id, d => d.Pages[0].Title = "Start")));  // no builder
        Assert.Equal("plan_limit_reached", await CodeOf(() => CreateAsync(bid, "Second Site")));                          // one site only

        var caps = (await Sites(bid).CatalogAsync()).Capabilities;
        Assert.False(caps.Builder);
        Assert.Equal("none", caps.ThemeLevel);
        Assert.Contains((await Sites(bid).CatalogAsync()).Templates, t => t.Key == "standard" && t.Allowed);
        Assert.Contains((await Sites(bid).CatalogAsync()).Templates, t => t.Key == "luxury" && !t.Allowed);

        var published = await Sites(bid).PublishAsync(site.Id, new PublishRequest());
        Assert.Equal(1, published.Version.VersionNumber);
        Assert.Equal("Home", (await Public().GetSiteAsync(site.Slug)).Document.GetProperty("pages")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Starter_can_pick_a_basic_template_and_a_limited_palette_but_not_fonts_or_the_builder()
    {
        var bid = await BusinessAsync("starter");
        var site = await CreateAsync(bid, template: "bespoke");
        var logo = await SiteImageAsync(bid);

        var draft = await Sites(bid).GetDraftAsync(site.Id);
        var doc = ToDoc(draft);
        doc.Theme.Preset = "elegant"; doc.Theme.Colors["primary"] = "#7A2E3A"; doc.Branding.LogoMediaId = logo;
        var saved = await Sites(bid).SaveAppearanceAsync(site.Id, new SaveAppearanceRequest { Theme = JsonSerializer.SerializeToElement(doc.Theme, StorefrontJson.Options), Branding = JsonSerializer.SerializeToElement(doc.Branding, StorefrontJson.Options), ExpectedRevision = draft.Revision });
        Assert.Equal(draft.Revision + 1, saved.Revision);

        doc.Theme.HeadingFont = "Lora";
        var ex = await Fails(() => Sites(bid).SaveAppearanceAsync(site.Id, new SaveAppearanceRequest { Theme = JsonSerializer.SerializeToElement(doc.Theme, StorefrontJson.Options), Branding = JsonSerializer.SerializeToElement(doc.Branding, StorefrontJson.Options), ExpectedRevision = saved.Revision }));
        Assert.Equal("plan_violations", ex.Code);
        Assert.Equal(403, ex.StatusCode);

        Assert.Equal("feature_not_in_plan", await CodeOf(() => EditAsync(bid, site.Id, d => d.Pages[0].Title = "X")));
        Assert.Equal("feature_not_in_plan", await CodeOf(() => Sites(bid).PreviewSwitchAsync(site.Id, "luxury")));
        await Sites(bid).PreviewSwitchAsync(site.Id, "ready-to-wear"); // basic tier: fine
        await Sites(bid).PublishAsync(site.Id, new PublishRequest());
    }

    [Fact]
    public async Task Professional_arranges_pages_with_advanced_sections_and_Business_gets_premium_templates()
    {
        var pro = await BusinessAsync("professional");
        var site = await CreateAsync(pro, template: "luxury");
        var draft = await EditAsync(pro, site.Id, d =>
        {
            var home = d.Pages.First(p => p.Kind == "home");
            home.Sections.Reverse();                                                                           // reorder
            home.Sections.Add(new SectionDoc { Id = Guid.NewGuid(), Type = "faq", Variant = "accordion", Props = Props(new { heading = "FAQ", items = new[] { new { question = "Q?", answer = "A." } } }) });
            var copy = StorefrontJson.Clone(new StorefrontDocument { Pages = [home] }).Pages[0].Sections[0]; copy.Id = Guid.NewGuid(); home.Sections.Add(copy); // duplicate
            home.Sections.RemoveAt(1);                                                                          // remove
            d.Theme.Colors["background"] = "#1C1C1C"; d.Theme.HeadingFont = "Cinzel";
        });
        Assert.Equal("Cinzel", draft.Theme.HeadingFont);
        Assert.Equal("feature_not_in_plan", await CodeOf(() => CreateAsync(pro, "Second", "streetwear")));      // premium template, one site

        var biz = await BusinessAsync("business");
        var a = await CreateAsync(biz, "Street One", "streetwear");
        var b = await CreateAsync(biz, "Studio Two", "atelier-studio");                                          // up to 5 sites on Business
        Assert.NotEqual(a.Slug, b.Slug);
        await EditAsync(biz, a.Id, d => { d.Theme.ButtonShape = "pill"; d.Branding.Tagline = "Drop 01"; });
    }

    // ------------------------------------------------------------------ the acceptance criterion

    [Fact]
    public async Task Two_businesses_publish_visibly_distinct_sites_on_the_same_platform_with_no_leakage()
    {
        var a = await BusinessAsync("professional"); var b = await BusinessAsync("professional");
        var siteA = await CreateAsync(a, "Royal Couture", "luxury", "royal-couture");
        var siteB = await CreateAsync(b, "Adire House", "african-heritage", "adire-house");

        await EditAsync(a, siteA.Id, d => { d.Theme.Colors["primary"] = "#D4AF37"; d.Theme.HeadingFont = "Cinzel"; d.Theme.BodyFont = "Lato"; d.Pages.RemoveAll(p => p.Kind == "services"); d.Navigation.RemoveAll(n => d.Pages.All(p => p.Id != n.PageId)); });
        await EditAsync(b, siteB.Id, d => { d.Theme.Preset = "african-heritage"; d.Theme.Colors["primary"] = "#C2410C"; d.Theme.HeadingFont = "DM Serif Display"; d.Theme.BodyFont = "Nunito"; First(d, "hero").Props = Props(new { headline = "Adire House", subheadline = "Hand-dyed, hand-cut." }); });

        var privateA = await PublicDesignAsync(a, "Midnight Gown", publish: false);
        await PublicDesignAsync(a, "Gold Coat");
        await PublicDesignAsync(b, "Indigo Agbada");
        await Sites(a).PublishAsync(siteA.Id, new PublishRequest { Note = "Launch" });
        await Sites(b).PublishAsync(siteB.Id, new PublishRequest { Note = "Launch" });

        var pubA = await Public().GetSiteAsync("royal-couture"); var pubB = await Public().GetSiteAsync("adire-house");
        Assert.NotEqual(pubA.TemplateKey, pubB.TemplateKey);
        Assert.NotEqual(Sig(pubA.Document), Sig(pubB.Document));                                   // different pages and sections
        Assert.NotEqual(pubA.Theme.HeadingFont, pubB.Theme.HeadingFont);                           // different typography
        Assert.NotEqual(pubA.Theme.Colors["primary"], pubB.Theme.Colors["primary"]);               // different colours
        Assert.NotEqual(pubA.Theme.Colors["background"], pubB.Theme.Colors["background"]);
        Assert.Equal(pubA.Document.GetProperty("pages").GetArrayLength() + 1, pubB.Document.GetProperty("pages").GetArrayLength() - 0); // A dropped its services page

        // no cross-tenant leakage: each site shows only its own public designs
        var designsA = await Public().ListDesignsAsync("royal-couture", new PublicDesignQuery());
        var designsB = await Public().ListDesignsAsync("adire-house", new PublicDesignQuery());
        Assert.Equal(new[] { "Gold Coat" }, designsA.Items.Select(d => d.Name));
        Assert.Equal(new[] { "Indigo Agbada" }, designsB.Items.Select(d => d.Name));
        Assert.Equal("design_not_found", await CodeOf(() => Public().GetDesignAsync("royal-couture", "midnight-gown")));   // private
        Assert.Equal("design_not_found", await CodeOf(() => Public().GetDesignAsync("adire-house", "gold-coat")));         // another tenant's public design

        // neither owner can see or change the other's site
        Assert.Equal("storefront_not_found", await CodeOf(() => Sites(b).GetAsync(siteA.Id)));
        Assert.Equal("storefront_not_found", await CodeOf(() => Sites(b).GetDraftAsync(siteA.Id)));
        Assert.Equal("storefront_not_found", await CodeOf(() => Sites(b).PublishAsync(siteA.Id, new PublishRequest())));
        Assert.Equal("storefront_not_found", await CodeOf(() => Sites(b).UnpublishAsync(siteA.Id)));
        Assert.Equal("storefront_not_found", await CodeOf(() => Sites(b).CreatePreviewLinkAsync(siteA.Id)));
        Assert.Single(await Sites(b).ListVersionsAsync(siteB.Id));
        Assert.Single(await Sites(a).ListAsync());
        Assert.NotEqual(Guid.Empty, privateA);
    }

    // ------------------------------------------------------------------ drafts, versions, rollback

    [Fact]
    public async Task Draft_changes_stay_private_until_published_and_rollback_restores_an_old_version_exactly()
    {
        var bid = await BusinessAsync();
        var site = await CreateAsync(bid, "Draft Test", "luxury", "draft-test");
        await Sites(bid).PublishAsync(site.Id, new PublishRequest { Note = "v1" });
        var v1 = await Public().GetSiteAsync("draft-test");

        await EditAsync(bid, site.Id, d => First(d, "hero").Props = Props(new { headline = "Brand new headline" }));
        Assert.True((await Sites(bid).GetAsync(site.Id)).HasUnpublishedChanges);
        Assert.Equal(v1.Document.GetRawText(), (await Public().GetSiteAsync("draft-test")).Document.GetRawText());   // visitors still see v1

        await Sites(bid).PublishAsync(site.Id, new PublishRequest { Note = "v2" });
        Assert.Contains("Brand new headline", (await Public().GetSiteAsync("draft-test")).Document.GetRawText());
        Assert.False((await Sites(bid).GetAsync(site.Id)).HasUnpublishedChanges);

        var rolled = await Sites(bid).RollbackAsync(site.Id, 1, new PublishRequest());
        Assert.Equal(3, rolled.Version.VersionNumber);                                                                // history only grows
        Assert.Equal(1, rolled.Version.RolledBackFromVersion);
        var afterRollback = await Public().GetSiteAsync("draft-test");
        Assert.Equal(v1.Document.GetRawText(), afterRollback.Document.GetRawText());                                  // identical to v1
        Assert.Equal(3, afterRollback.VersionNumber);
        Assert.False((await Sites(bid).GetAsync(site.Id)).HasUnpublishedChanges);                                     // the draft was reset to v1 too

        var versions = await Sites(bid).ListVersionsAsync(site.Id);
        Assert.Equal(new[] { 3, 2, 1 }, versions.Select(v => v.VersionNumber));
        Assert.Equal(new[] { true, false, false }, versions.Select(v => v.IsLive));
        Assert.Contains("Brand new headline", (await Sites(bid).GetVersionDocumentAsync(site.Id, 2)).GetRawText());   // v2 is untouched
        Assert.Equal("version_not_found", await CodeOf(() => Sites(bid).RollbackAsync(site.Id, 99, new PublishRequest())));
    }

    [Fact]
    public async Task Hidden_pages_sections_and_the_archive_are_not_sent_to_visitors()
    {
        var bid = await BusinessAsync();
        var site = await CreateAsync(bid, "Hidden", "luxury", "hidden-bits");
        await EditAsync(bid, site.Id, d =>
        {
            d.Pages.First(p => p.Kind == "about").Visible = false;
            First(d, "testimonials").Visible = false;
            d.Archive.Add(new SectionDoc { Id = Guid.NewGuid(), Type = "pricing", Variant = "table", Props = Props(new { heading = "SECRET PRICES" }) });
            d.Navigation.First(n => n.PageId == d.Pages.First(p => p.Kind == "about").Id).Visible = false;
        });
        await Sites(bid).PublishAsync(site.Id, new PublishRequest());
        var json = (await Public().GetSiteAsync("hidden-bits")).Document.GetRawText();
        Assert.DoesNotContain("SECRET PRICES", json);
        Assert.DoesNotContain("\"testimonials\"", json);
        Assert.DoesNotContain("\"kind\":\"about\"", json);
    }

    [Fact]
    public async Task Publishing_is_blocked_when_an_enabled_workflow_loses_its_entry_point()
    {
        var bid = await BusinessAsync();
        var site = await CreateAsync(bid, "Workflow", "luxury", "workflow-test");
        await Sites(bid).UpdateSettingsAsync(site.Id, Settings(site, w => w with { WorkflowBooking = true }));
        await EditAsync(bid, site.Id, d => { foreach (var p in d.Pages) p.Sections.RemoveAll(s => s.Type == "booking"); });

        var ex = await Fails(() => Sites(bid).PublishAsync(site.Id, new PublishRequest()));
        Assert.Equal("missing_entry_points", ex.Code);
        Assert.Contains("appointment booking", ex.Message);

        var current = await Sites(bid).GetAsync(site.Id);
        await Sites(bid).UpdateSettingsAsync(site.Id, Settings(current, w => w with { WorkflowBooking = false })); // switch the workflow off instead
        await Sites(bid).PublishAsync(site.Id, new PublishRequest());
    }

    private static UpdateStorefrontSettingsRequest Settings(StorefrontDto s, Func<UpdateStorefrontSettingsRequest, UpdateStorefrontSettingsRequest> change)
        => change(new UpdateStorefrontSettingsRequest
        {
            Version = s.Version, Name = s.Name, ContactEmail = s.ContactEmail, ContactPhone = s.ContactPhone, Address = s.Address, OpeningHours = s.OpeningHours,
            WorkflowInquiry = s.WorkflowInquiry, WorkflowBooking = s.WorkflowBooking, WorkflowCustomOrder = s.WorkflowCustomOrder, WorkflowContact = s.WorkflowContact,
            AllowSearchIndexing = s.AllowSearchIndexing,
        });

    // ------------------------------------------------------------------ unsafe input

    [Fact]
    public async Task Unsafe_or_foreign_content_is_rejected_with_a_list_of_problems()
    {
        var a = await BusinessAsync(); var b = await BusinessAsync();
        var site = await CreateAsync(a, "Safe", "luxury");
        var theirs = await SiteImageAsync(b);

        var draft = await Sites(a).GetDraftAsync(site.Id);
        async Task<AppException> Save(Action<StorefrontDocument> change)
        {
            var doc = ToDoc(draft); change(doc);
            return await Fails(() => Sites(a).SaveDraftAsync(site.Id, new SaveDraftRequest { Document = ToJson(doc), ExpectedRevision = draft.Revision }));
        }

        var script = await Save(d => First(d, "hero").Props = Props(new { headline = "<script>alert(1)</script>" }));
        Assert.Equal(("invalid_document", 400), (script.Code, script.StatusCode));
        Assert.NotNull(script.Extensions!["errors"]);
        Assert.Equal("invalid_document", (await Save(d => d.Theme.Colors["primary"] = "red; background:url(x)")).Code);
        Assert.Equal("invalid_document", (await Save(d => d.Theme.HeadingFont = "Not A Font")).Code);
        Assert.Equal("invalid_document", (await Save(d => First(d, "hero").Props = Props(new { headline = "x", imageMediaId = theirs }))).Code); // another tenant's image
        Assert.Equal("invalid_document", (await Save(d => First(d, "designList").Props = Props(new { heading = "x", categoryId = Guid.NewGuid() }))).Code);   // a category that does not exist

        var rawUnknown = JsonDocument.Parse(ToJson(ToDoc(draft)).GetRawText().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"customCss\":\"x\"")).RootElement.Clone();
        Assert.Equal("invalid_document", (await Fails(() => Sites(a).SaveDraftAsync(site.Id, new SaveDraftRequest { Document = rawUnknown, ExpectedRevision = draft.Revision }))).Code);
        Assert.Equal("invalid_document", (await Fails(() => Sites(a).SaveDraftAsync(site.Id, new SaveDraftRequest { Document = JsonDocument.Parse("\"just a string\"").RootElement.Clone(), ExpectedRevision = draft.Revision }))).Code);

        Assert.Equal(draft.Revision, (await Sites(a).GetDraftAsync(site.Id)).Revision); // nothing was saved by any of the above
    }

    [Fact]
    public async Task Saving_with_an_old_revision_is_refused_so_nobody_overwrites_someone_else()
    {
        var bid = await BusinessAsync();
        var site = await CreateAsync(bid, "Revisions", "luxury");
        var loaded = await Sites(bid).GetDraftAsync(site.Id);
        await EditAsync(bid, site.Id, d => d.Pages[0].Title = "First editor");
        var doc = ToDoc(loaded); doc.Pages[0].Title = "Second editor";
        Assert.Equal("stale_revision", await CodeOf(() => Sites(bid).SaveDraftAsync(site.Id, new SaveDraftRequest { Document = ToJson(doc), ExpectedRevision = loaded.Revision })));
    }

    // ------------------------------------------------------------------ template switching

    [Fact]
    public async Task Template_switch_needs_the_preview_token_keeps_content_and_warns_first()
    {
        var bid = await BusinessAsync("business");
        var site = await CreateAsync(bid, "Switch", "luxury");
        var before = ToDoc(await Sites(bid).GetDraftAsync(site.Id));
        var total = before.Pages.Sum(p => p.Sections.Count);

        var plan = await Sites(bid).PreviewSwitchAsync(site.Id, "streetwear");
        Assert.True(plan.NeedsConfirmation);
        Assert.NotEmpty(plan.Archived);
        Assert.Equal(total, plan.Kept.Count + plan.Converted.Count + plan.Archived.Count);

        var revision = (await Sites(bid).GetDraftAsync(site.Id)).Revision;
        Assert.Equal("plan_changed", await CodeOf(() => Sites(bid).ApplySwitchAsync(site.Id, new ApplySwitchRequest { TargetTemplate = "streetwear", Token = "wrong", ExpectedRevision = revision })));

        var switched = await Sites(bid).ApplySwitchAsync(site.Id, new ApplySwitchRequest { TargetTemplate = "streetwear", Token = plan.Token, ExpectedRevision = revision });
        var after = ToDoc(switched);
        Assert.Equal("streetwear", after.TemplateKey);
        Assert.Equal(total, after.Pages.Sum(p => p.Sections.Count) + after.Archive.Count);        // nothing deleted
        Assert.Equal(before.Theme.Preset, after.Theme.Preset);                                    // look untouched
        Assert.Equal("streetwear", (await Sites(bid).GetAsync(site.Id)).TemplateKey);

        // the same (now stale) token cannot be replayed
        Assert.Equal("stale_revision", await CodeOf(() => Sites(bid).ApplySwitchAsync(site.Id, new ApplySwitchRequest { TargetTemplate = "streetwear", Token = plan.Token, ExpectedRevision = revision })));

        var back = await Sites(bid).PreviewSwitchAsync(site.Id, "luxury");
        Assert.NotEmpty(back.Restored);
        var restored = ToDoc(await Sites(bid).ApplySwitchAsync(site.Id, new ApplySwitchRequest { TargetTemplate = "luxury", Token = back.Token, ExpectedRevision = switched.Revision }));
        Assert.Empty(restored.Archive);
    }

    // ------------------------------------------------------------------ downgrades

    [Fact]
    public async Task After_a_downgrade_the_live_site_keeps_rendering_and_editing_locks_but_settings_survive()
    {
        var bid = await BusinessAsync("professional");
        var site = await CreateAsync(bid, "Downgrade", "luxury", "downgrade-me");
        await EditAsync(bid, site.Id, d => { d.Theme.Colors["background"] = "#202020"; d.Theme.HeadingFont = "Cinzel"; });
        await Sites(bid).PublishAsync(site.Id, new PublishRequest());

        await using (var db = fx.Create(null))
        {
            var starter = await db.Plans.FirstAsync(p => p.Code == "starter");
            (await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.BusinessId == bid)).PlanId = starter.Id;
            await db.SaveChangesAsync();
        }

        var live = await Public().GetSiteAsync("downgrade-me");                                       // still renders safely
        Assert.Equal("Cinzel", live.Theme.HeadingFont);
        Assert.Equal("feature_not_in_plan", await CodeOf(() => EditAsync(bid, site.Id, d => d.Pages[0].Title = "Edited")));   // builder locked
        Assert.Equal("plan_violations", await CodeOf(() => Sites(bid).PublishAsync(site.Id, new PublishRequest())));          // luxury is above Starter
        Assert.False((await Sites(bid).CatalogAsync()).Capabilities.Builder);

        var draft = await Sites(bid).GetDraftAsync(site.Id);
        Assert.NotEmpty(draft.PlanIssues);                                                              // the UI can explain what is locked
        Assert.Equal("Cinzel", draft.Theme.HeadingFont);                                                // settings preserved for re-upgrade

        // they may still REMOVE the advanced customisations
        var doc = ToDoc(draft); doc.Theme.Colors.Clear(); doc.Theme.HeadingFont = null;
        var cleaned = await Sites(bid).SaveAppearanceAsync(site.Id, new SaveAppearanceRequest { Theme = JsonSerializer.SerializeToElement(doc.Theme, StorefrontJson.Options), Branding = JsonSerializer.SerializeToElement(doc.Branding, StorefrontJson.Options), ExpectedRevision = draft.Revision });
        Assert.DoesNotContain(cleaned.PlanIssues, i => i.Code.StartsWith("theme"));
    }

    // ------------------------------------------------------------------ media on sites

    [Fact]
    public async Task Site_images_are_public_only_while_part_of_the_live_version_and_cannot_be_deleted_while_used()
    {
        var bid = await BusinessAsync();
        var site = await CreateAsync(bid, "Media", "luxury", "media-site");
        var image = await SiteImageAsync(bid, storeBytes: true);

        async Task<string?> PublicCode()
        {
            try { await using var c = (await Media(null).OpenPublicAsync(image, "md")).Content; return null; }
            catch (AppException ex) { return ex.Code; }
        }

        await EditAsync(bid, site.Id, d => First(d, "hero").Props = Props(new { headline = "Hi", imageMediaId = image }));
        Assert.Equal("media_not_found", await PublicCode());                                           // in a draft only: not public
        Assert.Equal("media_in_use", await CodeOf(() => Media(bid).DeleteAsync(image)));              // but protected from deletion

        await Sites(bid).PublishAsync(site.Id, new PublishRequest());
        Assert.Null(await PublicCode());                                                                // live: public

        await EditAsync(bid, site.Id, d => First(d, "hero").Props = Props(new { headline = "Hi" }));
        await Sites(bid).PublishAsync(site.Id, new PublishRequest());
        Assert.Equal("media_not_found", await PublicCode());                                           // no longer in the live version
        Assert.Equal("media_in_use", await CodeOf(() => Media(bid).DeleteAsync(image)));              // still in version 1, which can be rolled back to

        await Sites(bid).UnpublishAsync(site.Id);
        await Sites(bid).RollbackAsync(site.Id, 1, new PublishRequest());                              // v1 is live again
        Assert.Null(await PublicCode());
        await Sites(bid).UnpublishAsync(site.Id);
        Assert.Equal("media_not_found", await PublicCode());                                           // an unpublished site shows nothing
    }

    // ------------------------------------------------------------------ public catalog

    [Fact]
    public async Task The_public_catalog_shows_only_public_active_designs_and_hides_private_collection_members()
    {
        var bid = await BusinessAsync();
        var site = await CreateAsync(bid, "Catalog", "standard", "catalog-site");
        var shown = await PublicDesignAsync(bid, "Shown Dress");
        var hidden = await PublicDesignAsync(bid, "Private Dress", publish: false);
        var gone = await PublicDesignAsync(bid, "Archived Dress");
        await Designs(bid).SetStatusAsync(gone, new SetDesignStatusRequest { Status = "Archived" });
        await Sites(bid).PublishAsync(site.Id, new PublishRequest());

        var list = await Public().ListDesignsAsync("catalog-site", new PublicDesignQuery());
        Assert.Equal(new[] { "Shown Dress" }, list.Items.Select(d => d.Name));
        Assert.Equal(1, list.TotalCount);
        Assert.Equal("Shown Dress", (await Public().GetDesignAsync("catalog-site", "shown-dress")).Name);
        Assert.Equal("design_not_found", await CodeOf(() => Public().GetDesignAsync("catalog-site", "private-dress")));
        Assert.Equal("design_not_found", await CodeOf(() => Public().GetDesignAsync("catalog-site", "archived-dress")));
        Assert.Single((await Public().ListDesignsAsync("catalog-site", new PublicDesignQuery { Search = "SHOWN" })).Items);
        Assert.Empty((await Public().ListDesignsAsync("catalog-site", new PublicDesignQuery { Search = "private" })).Items);

        var cols = new CollectionService(fx.Create(bid), new FakeUserTenant(bid, Staff), new AuditLogger(fx.Create(bid)));
        var col = await cols.CreateAsync(new Tailoring.Application.Collections.UpsertCollectionRequest { Name = "Summer", Visibility = "Public" });
        await cols.SetItemsAsync(col.Id, new Tailoring.Application.Collections.SetCollectionItemsRequest { DesignIds = [shown, hidden] });
        var pubCol = await Public().GetCollectionAsync("catalog-site", "summer");
        Assert.Equal(new[] { "Shown Dress" }, pubCol.Designs.Select(d => d.Name));                     // the private member is not exposed
        var privateCol = await cols.CreateAsync(new Tailoring.Application.Collections.UpsertCollectionRequest { Name = "Secret", Visibility = "Private" });
        Assert.Equal("collection_not_found", await CodeOf(() => Public().GetCollectionAsync("catalog-site", privateCol.Slug)));
    }

    [Fact]
    public async Task Unpublished_or_unknown_sites_are_a_plain_404_everywhere()
    {
        var bid = await BusinessAsync();
        var site = await CreateAsync(bid, "Not Live", "standard", "not-live");
        foreach (var call in new Func<Task>[]
        {
            () => Public().GetSiteAsync("not-live"), () => Public().ListDesignsAsync("not-live", new PublicDesignQuery()),
            () => Public().ListCategoriesAsync("not-live"), () => Public().GetSiteAsync("no-such-site"),
        })
            Assert.Equal("site_not_found", await CodeOf(call));

        await Sites(bid).PublishAsync(site.Id, new PublishRequest());
        await Public().GetSiteAsync("not-live");
        await Sites(bid).UnpublishAsync(site.Id);
        Assert.Equal("site_not_found", await CodeOf(() => Public().GetSiteAsync("not-live")));
        Assert.Equal("not_published", await CodeOf(() => Sites(bid).UnpublishAsync(site.Id)));
    }

    [Fact]
    public async Task Sitemap_and_robots_follow_the_owners_choices_and_only_name_the_sites_own_address()
    {
        var bid = await BusinessAsync();
        var site = await CreateAsync(bid, "Seo", "standard", "seo-site");
        await PublicDesignAsync(bid, "Indexed Gown"); await PublicDesignAsync(bid, "Private Gown", publish: false);
        await Sites(bid).PublishAsync(site.Id, new PublishRequest());

        var xml = await Public().SitemapXmlAsync("seo-site", "https://seo-site.avzar.test");
        Assert.Contains("https://seo-site.avzar.test/design/indexed-gown", xml);
        Assert.DoesNotContain("private-gown", xml);
        Assert.DoesNotContain("/design</loc>", xml);                                                     // the design TEMPLATE page is not a URL
        Assert.Contains("Sitemap: https://seo-site.avzar.test/sitemap.xml", await Public().RobotsTxtAsync("seo-site", "https://seo-site.avzar.test"));
        Assert.Equal("invalid_base_url", await CodeOf(() => Public().SitemapXmlAsync("seo-site", "https://evil.example.com")));
        Assert.Equal("invalid_base_url", await CodeOf(() => Public().SitemapXmlAsync("seo-site", "http://seo-site.avzar.test")));

        await Sites(bid).UpdateSettingsAsync(site.Id, Settings(await Sites(bid).GetAsync(site.Id), w => w with { AllowSearchIndexing = false }));
        Assert.DoesNotContain("<url>", await Public().SitemapXmlAsync("seo-site", "https://seo-site.avzar.test"));
        Assert.Contains("Disallow: /", await Public().RobotsTxtAsync("seo-site", "https://seo-site.avzar.test"));
    }

    [Fact]
    public async Task Preview_links_show_the_draft_privately_and_expire_or_fail_when_tampered()
    {
        var bid = await BusinessAsync();
        var site = await CreateAsync(bid, "Preview", "luxury", "preview-site");
        await EditAsync(bid, site.Id, d => First(d, "hero").Props = Props(new { headline = "Only in the draft" }));

        var link = await Sites(bid).CreatePreviewLinkAsync(site.Id);
        var preview = await Public().GetPreviewAsync(link.Token);
        Assert.True(preview.IsPreview);
        Assert.False(preview.AllowSearchIndexing);
        Assert.Contains("Only in the draft", preview.Document.GetRawText());
        Assert.Equal("site_not_found", await CodeOf(() => Public().GetSiteAsync("preview-site")));   // the draft is not public

        Assert.Equal("preview_expired", await CodeOf(() => Public().GetPreviewAsync(link.Token + "x")));
        Assert.Equal("preview_expired", await CodeOf(() => Public().GetPreviewAsync("garbage")));
    }

    // ------------------------------------------------------------------ presets

    [Fact]
    public async Task Presets_include_built_ins_and_validated_custom_ones()
    {
        var bid = await BusinessAsync();
        Assert.Equal(StorefrontPresets.All.Count, (await Sites(bid).ListPresetsAsync()).Count(p => p.IsBuiltIn));

        var created = await Sites(bid).CreatePresetAsync(new CreatePresetRequest
        { Name = "My hero", SectionType = "hero", Variant = "minimal", Props = Props(new { headline = "Handmade in Lagos" }) });
        Assert.False(created.IsBuiltIn);
        Assert.Contains(await Sites(bid).ListPresetsAsync(), p => p.Name == "My hero");

        Assert.Equal("preset_exists", await CodeOf(() => Sites(bid).CreatePresetAsync(new CreatePresetRequest { Name = "my hero", SectionType = "hero", Variant = "minimal", Props = Props(new { headline = "x" }) })));
        Assert.Equal("invalid_document", await CodeOf(() => Sites(bid).CreatePresetAsync(new CreatePresetRequest { Name = "Bad", SectionType = "hero", Variant = "minimal", Props = Props(new { headline = "<b>x</b>" }) })));
        Assert.Equal("invalid_document", await CodeOf(() => Sites(bid).CreatePresetAsync(new CreatePresetRequest { Name = "Img", SectionType = "hero", Variant = "minimal", Props = Props(new { headline = "x", imageMediaId = Guid.NewGuid() }) })));
        Assert.Equal("invalid_document", await CodeOf(() => Sites(bid).CreatePresetAsync(new CreatePresetRequest { Name = "Nope", SectionType = "script", Variant = "x", Props = Props(new { }) })));

        var other = await BusinessAsync();
        Assert.DoesNotContain(await Sites(other).ListPresetsAsync(), p => p.Name == "My hero");                  // presets are per business
        await Sites(bid).DeletePresetAsync(Guid.Parse(created.Key));
        Assert.DoesNotContain(await Sites(bid).ListPresetsAsync(), p => p.Name == "My hero");

        var starter = await BusinessAsync("starter");
        Assert.Equal("feature_not_in_plan", await CodeOf(() => Sites(starter).CreatePresetAsync(new CreatePresetRequest { Name = "X1", SectionType = "hero", Variant = "minimal", Props = Props(new { headline = "x" }) })));
    }

    // ------------------------------------------------------------------ custom domains

    [Fact]
    public async Task Custom_domains_need_the_plan_feature_dns_proof_and_stop_working_if_the_feature_is_lost()
    {
        var bid = await BusinessAsync("professional");   // custom domains are off by default in every plan
        var site = await CreateAsync(bid, "Domain", "standard", "domain-site");
        Assert.Equal("feature_not_in_plan", await CodeOf(() => Sites(bid).AddDomainAsync(site.Id, new AddDomainRequest { Hostname = "shop.example.com" })));

        var biz = await BusinessAsync("professional", (EntitlementKeys.StorefrontCustomDomain, "true"));
        var s2 = await CreateAsync(biz, "Domain Two", "standard", "domain-two");
        Assert.Equal("invalid_hostname", await CodeOf(() => Sites(biz).AddDomainAsync(s2.Id, new AddDomainRequest { Hostname = "https://shop.example.com/path" })));
        Assert.Equal("invalid_hostname", await CodeOf(() => Sites(biz).AddDomainAsync(s2.Id, new AddDomainRequest { Hostname = "192.168.0.1" })));
        Assert.Equal("reserved_hostname", await CodeOf(() => Sites(biz).AddDomainAsync(s2.Id, new AddDomainRequest { Hostname = "evil.avzar.test" })));

        var added = await Sites(biz).AddDomainAsync(s2.Id, new AddDomainRequest { Hostname = "Shop.Example.COM." });
        Assert.Equal(("shop.example.com", "Pending"), (added.Hostname, added.Status));
        Assert.Equal("_avzar-verify.shop.example.com", added.VerificationRecordName);
        Assert.False(await Public().IsDomainAllowedAsync("shop.example.com"));                                  // no certificate for an unverified domain

        var wrong = await Sites(biz).VerifyDomainAsync(s2.Id, added.Id);                                         // no record yet
        Assert.Equal("Failed", wrong.Status);
        await using (var db = fx.Create(null)) { (await db.StorefrontDomains.IgnoreQueryFilters().FirstAsync(d => d.Id == added.Id)).LastCheckedAtUtc = DateTime.UtcNow.AddMinutes(-5); await db.SaveChangesAsync(); }

        _dns.Records["_avzar-verify.shop.example.com"] = ["\"" + added.VerificationRecordValue + "\""];
        var ok = await Sites(biz).VerifyDomainAsync(s2.Id, added.Id);
        Assert.Equal("Verified", ok.Status);
        Assert.True(await Public().IsDomainAllowedAsync("SHOP.example.com"));
        Assert.Equal("domain-two", await Public().ResolveHostAsync("shop.example.com"));

        Assert.Equal("domain_taken", await CodeOf(async () => { var o = await BusinessAsync("professional", (EntitlementKeys.StorefrontCustomDomain, "true")); var x = await CreateAsync(o, "Thief", "standard"); await Sites(o).AddDomainAsync(x.Id, new AddDomainRequest { Hostname = "shop.example.com" }); }));

        await using (var db = fx.Create(null))                                                                   // the feature is removed (downgrade)
        {
            (await db.EntitlementOverrides.IgnoreQueryFilters().FirstAsync(o => o.BusinessId == biz && o.Key == EntitlementKeys.StorefrontCustomDomain)).Value = "false";
            await db.SaveChangesAsync();
        }
        Assert.Null(await Public().ResolveHostAsync("shop.example.com"));
        Assert.False(await Public().IsDomainAllowedAsync("shop.example.com"));
    }

    [Fact]
    public async Task Domain_checks_are_rate_limited_and_survive_dns_failures()
    {
        var bid = await BusinessAsync("professional", (EntitlementKeys.StorefrontCustomDomain, "true"));
        var site = await CreateAsync(bid, "Dns", "standard", "dns-site");
        var d = await Sites(bid).AddDomainAsync(site.Id, new AddDomainRequest { Hostname = "dns.example.org" });
        _dns.Fail = true;
        var failed = await Sites(bid).VerifyDomainAsync(site.Id, d.Id);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("too_soon", await CodeOf(() => Sites(bid).VerifyDomainAsync(site.Id, d.Id)));
        await Sites(bid).RemoveDomainAsync(site.Id, d.Id);
        Assert.Empty(await Sites(bid).ListDomainsAsync(site.Id));
    }

    // ------------------------------------------------------------------ public workflows keep working

    private async Task<string> PublishedSlugAsync(Guid bid, string template, Func<StorefrontDto, UpdateStorefrontSettingsRequest>? settings = null)
    {
        var site = await CreateAsync(bid, "Requests " + template, template, ("req-" + template + "-" + Guid.NewGuid().ToString("N")[..5]).Replace("_", "-"));
        if (settings is not null) await Sites(bid).UpdateSettingsAsync(site.Id, settings(site));
        await Sites(bid).PublishAsync(site.Id, new PublishRequest());
        return site.Slug;
    }

    private static SubmitInquiryRequest Ask(string kind = "Inquiry", Action<SubmitInquiryRequest>? _ = null) => new() { Kind = kind, Name = "Ngozi", Email = "ngozi@example.com", Message = "Is this available in blue?" };

    [Theory]
    [InlineData("standard"), InlineData("luxury"), InlineData("african-heritage"), InlineData("streetwear"), InlineData("atelier-studio")]
    public async Task Requests_work_the_same_whatever_template_the_site_uses(string template)
    {
        var bid = await BusinessAsync("business");
        var slug = await PublishedSlugAsync(bid, template);
        var accepted = await Inquiries(null).SubmitAsync(slug, Ask(), "203.0.113.9");
        Assert.True(accepted.Received);

        var staff = await Inquiries(bid).ListAsync(new InquiryListQuery());
        var item = Assert.Single(staff.Items);
        Assert.Equal(("New", "Inquiry", "Ngozi"), (item.Status, item.Kind, item.Name));
        Assert.Equal(1, (await Inquiries(bid).SummaryAsync()).New);
    }

    [Fact]
    public async Task Every_kind_of_request_needs_its_workflow_switched_on_and_lands_in_the_right_business()
    {
        var a = await BusinessAsync(); var b = await BusinessAsync();
        var slugA = await PublishedSlugAsync(a, "african-heritage", s => Settings(s, w => w with { WorkflowBooking = true, WorkflowCustomOrder = true }));
        var slugB = await PublishedSlugAsync(b, "standard");

        await Inquiries(null).SubmitAsync(slugA, Ask("Quotation"), "1.1.1.1");
        await Inquiries(null).SubmitAsync(slugA, Ask("Contact"), "1.1.1.1");
        await Inquiries(null).SubmitAsync(slugA, Ask("OrderRequest") with { GarmentType = "Agbada", Budget = "N150,000", EventDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)) }, "1.1.1.1");
        await Inquiries(null).SubmitAsync(slugA, Ask("Appointment") with { PreferredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)) }, "1.1.1.1");

        Assert.Equal("workflow_disabled", await CodeOf(() => Inquiries(null).SubmitAsync(slugB, Ask("Appointment") with { PreferredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)) }, "2.2.2.2")));
        Assert.Equal("workflow_disabled", await CodeOf(() => Inquiries(null).SubmitAsync(slugB, Ask("OrderRequest"), "2.2.2.2")));

        Assert.Equal(4, (await Inquiries(a).ListAsync(new InquiryListQuery())).TotalCount);
        Assert.Equal(0, (await Inquiries(b).ListAsync(new InquiryListQuery())).TotalCount);                       // nothing crossed over
        Assert.Equal(4, (await Inquiries(a).SummaryAsync()).New);
    }

    [Fact]
    public async Task Bad_requests_spam_and_unknown_designs_are_refused_or_dropped()
    {
        var bid = await BusinessAsync();
        var slug = await PublishedSlugAsync(bid, "standard");
        var priv = await PublicDesignAsync(bid, "Private Piece", publish: false);

        Assert.Equal("contact_required", await CodeOf(() => Inquiries(null).SubmitAsync(slug, Ask() with { Email = null, Phone = null }, "3.3.3.3")));
        Assert.Equal("invalid_text", await CodeOf(() => Inquiries(null).SubmitAsync(slug, Ask() with { Message = "<script>alert(1)</script>" }, "3.3.3.3")));
        Assert.Equal("unknown_design", await CodeOf(() => Inquiries(null).SubmitAsync(slug, Ask() with { DesignId = priv }, "3.3.3.3")));          // private design
        Assert.Equal("unknown_design", await CodeOf(() => Inquiries(null).SubmitAsync(slug, Ask() with { DesignId = Guid.NewGuid() }, "3.3.3.3")));
        Assert.Equal("site_not_found", await CodeOf(() => Inquiries(null).SubmitAsync("nobody", Ask(), "3.3.3.3")));
        Assert.Equal("invalid_date", await CodeOf(() => Inquiries(null).SubmitAsync(slug, Ask() with { EventDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(20)) }, "3.3.3.3")));

        var before = (await Inquiries(bid).ListAsync(new InquiryListQuery())).TotalCount;
        var bot = await Inquiries(null).SubmitAsync(slug, Ask() with { Website = "http://spam.example" }, "4.4.4.4");   // honeypot
        Assert.True(bot.Received);                                                                                       // looks successful to the bot
        Assert.Equal(before, (await Inquiries(bid).ListAsync(new InquiryListQuery())).TotalCount);                      // but nothing was stored
    }

    [Fact]
    public async Task One_address_cannot_flood_a_business_and_a_configured_captcha_is_enforced()
    {
        var bid = await BusinessAsync();
        var slug = await PublishedSlugAsync(bid, "standard");
        for (var i = 0; i < InquiryService.MaxPerIpPerHour; i++) await Inquiries(null).SubmitAsync(slug, Ask(), "5.5.5.5");
        var ex = await Fails(() => Inquiries(null).SubmitAsync(slug, Ask(), "5.5.5.5"));
        Assert.Equal(429, ex.StatusCode);
        await Inquiries(null).SubmitAsync(slug, Ask(), "6.6.6.6");                                                       // a different address is fine

        var captcha = new FakeCaptcha(configured: true, passes: true);
        Assert.Equal("captcha_failed", await CodeOf(() => Inquiries(null, captcha).SubmitAsync(slug, Ask(), "7.7.7.7")));
        Assert.Equal("captcha_failed", await CodeOf(() => Inquiries(null, captcha).SubmitAsync(slug, Ask() with { CaptchaToken = "forged" }, "7.7.7.7")));
        Assert.True((await Inquiries(null, captcha).SubmitAsync(slug, Ask() with { CaptchaToken = "ok" }, "7.7.7.7")).Received);
    }

    [Fact]
    public async Task Staff_review_reject_and_convert_a_request_and_a_double_convert_makes_one_order()
    {
        var bid = await BusinessAsync();
        var slug = await PublishedSlugAsync(bid, "standard");
        var design = await PublicDesignAsync(bid, "Ankara Wrap");
        await Inquiries(null).SubmitAsync(slug, Ask() with { DesignId = design, Phone = "08031234567" }, "8.8.8.8");
        await Inquiries(null).SubmitAsync(slug, Ask() with { Name = "Spammer" }, "8.8.8.9");

        var list = (await Inquiries(bid).ListAsync(new InquiryListQuery { Search = "ngozi" })).Items.Single();
        Assert.Equal("Ankara Wrap", list.DesignName);

        var spam = (await Inquiries(bid).ListAsync(new InquiryListQuery { Search = "spammer" })).Items.Single();
        Assert.Equal("use_convert", await CodeOf(() => Inquiries(bid).UpdateAsync(spam.Id, new UpdateInquiryRequest { Status = "Converted" })));
        var rejected = await Inquiries(bid).UpdateAsync(spam.Id, new UpdateInquiryRequest { Status = "Rejected", InternalNote = "spam" });
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("not_convertible", await CodeOf(() => Inquiries(bid).ConvertToOrderAsync(spam.Id, Convert(bid))));

        await Inquiries(bid).UpdateAsync(list.Id, new UpdateInquiryRequest { Status = "Reviewed" });
        var location = await LocationAsync(bid);
        var converted = await Inquiries(bid).ConvertToOrderAsync(list.Id, new ConvertInquiryRequest { LocationId = location, PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)), UnitPrice = 45000m });
        Assert.Equal("Converted", converted.Status);
        Assert.NotNull(converted.ConvertedOrderId);

        await using var db = fx.Create(bid);
        var order = await db.Orders.Include(o => o.Garments).SingleAsync(o => o.Id == converted.ConvertedOrderId);
        Assert.Equal(OrderStatusDraft(), order.Status.ToString());                                  // a request is never a confirmed order
        Assert.Equal(("Ankara Wrap", 45000m), (order.Garments.Single().Name, order.Total));
        Assert.Equal(design, order.Garments.Single().DesignId);
        Assert.Equal(1, await db.Customers.CountAsync(c => c.FullName == "Ngozi"));                  // the customer was created

        Assert.Equal("not_convertible", await CodeOf(() => Inquiries(bid).ConvertToOrderAsync(list.Id, Convert(bid, location)))); // a second click
        Assert.Equal(1, await db.Orders.CountAsync());

        var other = await BusinessAsync();
        Assert.Equal("inquiry_not_found", await CodeOf(() => Inquiries(other).GetAsync(list.Id)));
        Assert.Equal("inquiry_not_found", await CodeOf(() => Inquiries(other).UpdateAsync(list.Id, new UpdateInquiryRequest { Status = "Reviewed" })));
    }

    private static string OrderStatusDraft() => "Draft";
    private static ConvertInquiryRequest Convert(Guid _, Guid? location = null) => new() { LocationId = location ?? Guid.NewGuid(), PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), UnitPrice = 1m };
    private async Task<Guid> LocationAsync(Guid bid) { await using var db = fx.Create(bid); return await db.Locations.Select(l => l.Id).FirstAsync(); }

    [Fact]
    public async Task A_returning_customer_is_matched_not_duplicated_when_a_request_is_converted()
    {
        var bid = await BusinessAsync();
        var slug = await PublishedSlugAsync(bid, "standard");
        var location = await LocationAsync(bid);
        await Inquiries(null).SubmitAsync(slug, Ask(), "9.9.9.1");
        await Inquiries(null).SubmitAsync(slug, Ask() with { Message = "A second question about sizes" }, "9.9.9.2");
        foreach (var i in (await Inquiries(bid).ListAsync(new InquiryListQuery())).Items)
            await Inquiries(bid).ConvertToOrderAsync(i.Id, new ConvertInquiryRequest { LocationId = location, PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), UnitPrice = 100m });
        await using var db = fx.Create(bid);
        Assert.Equal(1, await db.Customers.CountAsync());
        Assert.Equal(2, await db.Orders.CountAsync());
    }
}
