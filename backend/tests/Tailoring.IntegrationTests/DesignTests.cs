using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Common;
using Tailoring.Application.Designs;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Common;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

public sealed class SlugTests
{
    [Theory]
    [InlineData("Ankara Wrap Dress (2024)", "ankara-wrap-dress-2024")]
    [InlineData("  Crème Brûlée  Gown ", "creme-brulee-gown")]
    [InlineData("Agbada --- Set!!", "agbada-set")]
    [InlineData("!!!", "item")]
    [InlineData("", "item")]
    public void Produces_url_safe_slugs(string input, string expected) => Assert.Equal(expected, Slug.From(input));

    [Fact]
    public void Respects_the_length_cap_without_a_trailing_dash()
    {
        var s = Slug.From(new string('a', 58) + " bbbbbbbb", maxLength: 60);
        Assert.True(s.Length <= 60);
        Assert.False(s.EndsWith('-'));
    }
}

public sealed class DesignTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private async Task<Guid> NewBusinessAsync(string currency = "NGN", string plan = "professional")
    {
        await using var db = fx.Create(null);
        var p = await db.Plans.FirstAsync(x => x.Code == plan);
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = currency, TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = p.Id });
        await db.SaveChangesAsync();
        return b.Id;
    }

    private DesignService Svc(Guid bid) { var db = fx.Create(bid); return new DesignService(db, new FakeTenant(bid), new AuditLogger(db), new EntitlementService(db)); }

    private static UpsertDesignRequest Req(string name, Action<UpsertDesignRequest>? _ = null) => new() { Name = name, Kind = "MadeToOrder" };

    [Fact]
    public async Task Slugs_are_unique_per_business_and_stable_when_renamed()
    {
        var bid = await NewBusinessAsync();
        var a = await Svc(bid).CreateAsync(Req("Wedding Gown"));
        var b = await Svc(bid).CreateAsync(Req("Wedding Gown"));
        Assert.Equal("wedding-gown", a.Slug);
        Assert.Equal("wedding-gown-2", b.Slug);

        var renamed = await Svc(bid).UpdateAsync(a.Id, new UpsertDesignRequest { Name = "Bridal Gown", Kind = "MadeToOrder", Version = a.Version });
        Assert.Equal("Bridal Gown", renamed.Name);
        Assert.Equal("wedding-gown", renamed.Slug); // public URL does not move

        var other = await NewBusinessAsync();
        Assert.Equal("wedding-gown", (await Svc(other).CreateAsync(Req("Wedding Gown"))).Slug); // same slug is fine in another tenant
    }

    [Fact]
    public async Task Another_business_cannot_read_or_change_a_design()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var d = await Svc(a).CreateAsync(Req("Secret Suit"));

        var ex = await Assert.ThrowsAsync<AppException>(() => Svc(b).GetAsync(d.Id));
        Assert.Equal("design_not_found", ex.Code);
        await Assert.ThrowsAsync<AppException>(() => Svc(b).SetVisibilityAsync(d.Id, new SetDesignVisibilityRequest { Visibility = "Public" }));
        Assert.Empty((await Svc(b).ListAsync(new DesignListQuery())).Items);
    }

    [Fact]
    public async Task A_category_from_another_business_is_rejected()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var cat = await Svc(a).CreateCategoryAsync(new UpsertCategoryRequest { Name = "Bridal" });
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Svc(b).CreateAsync(new UpsertDesignRequest { Name = "X1", Kind = "ReadyMade", CategoryId = cat.Id }));
        Assert.Equal("unknown_category", ex.Code);
    }

    [Fact]
    public async Task Stale_versions_are_rejected_and_a_version_is_required()
    {
        var bid = await NewBusinessAsync();
        var d = await Svc(bid).CreateAsync(Req("Kaftan"));
        await Svc(bid).UpdateAsync(d.Id, new UpsertDesignRequest { Name = "Kaftan v2", Kind = "MadeToOrder", Version = d.Version });

        var stale = await Assert.ThrowsAsync<AppException>(() =>
            Svc(bid).UpdateAsync(d.Id, new UpsertDesignRequest { Name = "Kaftan v3", Kind = "MadeToOrder", Version = d.Version }));
        Assert.Equal("stale_version", stale.Code);
        var missing = await Assert.ThrowsAsync<AppException>(() => Svc(bid).UpdateAsync(d.Id, Req("x1")));
        Assert.Equal("version_required", missing.Code);
    }

    [Fact]
    public async Task Prices_are_rounded_stamped_with_the_business_currency_and_optional()
    {
        var bid = await NewBusinessAsync("NGN");
        var priced = await Svc(bid).CreateAsync(new UpsertDesignRequest { Name = "Agbada", Kind = "ReadyMade", BasePrice = 45000.005m });
        Assert.Equal(45000.01m, priced.BasePrice);
        Assert.Equal("NGN", priced.CurrencyCode);

        var onRequest = await Svc(bid).CreateAsync(Req("Custom Piece"));
        Assert.Null(onRequest.BasePrice);
        Assert.Null(onRequest.CurrencyCode);
    }

    [Fact]
    public async Task Tags_and_option_lists_are_normalised()
    {
        var bid = await NewBusinessAsync();
        var d = await Svc(bid).CreateAsync(new UpsertDesignRequest
        {
            Name = "Lace Gown", Kind = "MadeToOrder",
            Tags = ["  Bridal ", "bridal", "LACE", ""], Colours = ["Ivory", "ivory", "Blush"]
        });
        Assert.Equal(new[] { "bridal", "lace" }, d.Tags);
        Assert.Equal(new[] { "Ivory", "Blush" }, d.Colours);

        var found = await Svc(bid).ListAsync(new DesignListQuery { Tag = " LACE " });
        Assert.Single(found.Items);
    }

    [Fact]
    public async Task Archiving_makes_a_design_private_and_it_cannot_be_published_while_archived()
    {
        var bid = await NewBusinessAsync();
        var d = await Svc(bid).CreateAsync(Req("Blazer"));
        var pub = await Svc(bid).SetVisibilityAsync(d.Id, new SetDesignVisibilityRequest { Visibility = "Public" });
        Assert.Equal("Public", pub.Visibility);

        var archived = await Svc(bid).SetStatusAsync(d.Id, new SetDesignStatusRequest { Status = "Archived" });
        Assert.Equal("Archived", archived.Status);
        Assert.Equal("Private", archived.Visibility);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Svc(bid).SetVisibilityAsync(d.Id, new SetDesignVisibilityRequest { Visibility = "Public" }));
        Assert.Equal("design_archived", ex.Code);
        var edit = await Assert.ThrowsAsync<AppException>(() =>
            Svc(bid).UpdateAsync(d.Id, new UpsertDesignRequest { Name = "Blazer 2", Kind = "MadeToOrder", Version = archived.Version }));
        Assert.Equal("design_archived", edit.Code);
    }

    [Fact]
    public async Task List_filters_search_and_defaults_to_active_designs()
    {
        var bid = await NewBusinessAsync();
        var cat = await Svc(bid).CreateCategoryAsync(new UpsertCategoryRequest { Name = "Menswear" });
        await Svc(bid).CreateAsync(new UpsertDesignRequest { Name = "Linen Shirt", Kind = "ReadyMade", CategoryId = cat.Id, IsFeatured = true });
        await Svc(bid).CreateAsync(new UpsertDesignRequest { Name = "Silk Gown", Kind = "MadeToOrder" });
        var old = await Svc(bid).CreateAsync(Req("Old Coat"));
        await Svc(bid).SetStatusAsync(old.Id, new SetDesignStatusRequest { Status = "Archived" });

        Assert.Equal(2, (await Svc(bid).ListAsync(new DesignListQuery())).TotalCount);
        Assert.Single((await Svc(bid).ListAsync(new DesignListQuery { Status = "Archived" })).Items);
        Assert.Equal("Linen Shirt", (await Svc(bid).ListAsync(new DesignListQuery { CategoryId = cat.Id })).Items.Single().Name);
        Assert.Equal("Silk Gown", (await Svc(bid).ListAsync(new DesignListQuery { Search = "SILK" })).Items.Single().Name);
        Assert.Single((await Svc(bid).ListAsync(new DesignListQuery { Kind = "ReadyMade", Featured = true })).Items);
        Assert.Empty((await Svc(bid).ListAsync(new DesignListQuery { Search = "100%" })).Items); // wildcards are literal
    }

    [Fact]
    public async Task Saved_design_limit_counts_active_designs_only_and_restoring_is_limited()
    {
        var bid = await NewBusinessAsync(plan: "free");
        await using (var db = fx.Create(null))
        {
            db.EntitlementOverrides.Add(new TenantEntitlementOverride { BusinessId = bid, Key = EntitlementKeys.DesignsMax, Value = "2", Reason = "test" });
            await db.SaveChangesAsync();
        }
        var one = await Svc(bid).CreateAsync(Req("One 1"));
        await Svc(bid).CreateAsync(Req("Two 2"));
        var full = await Assert.ThrowsAsync<AppException>(() => Svc(bid).CreateAsync(Req("Three 3")));
        Assert.Equal("plan_limit_reached", full.Code);

        await Svc(bid).SetStatusAsync(one.Id, new SetDesignStatusRequest { Status = "Archived" }); // frees a slot
        await Svc(bid).CreateAsync(Req("Three 3"));
        var restore = await Assert.ThrowsAsync<AppException>(() =>
            Svc(bid).SetStatusAsync(one.Id, new SetDesignStatusRequest { Status = "Active" }));
        Assert.Equal("plan_limit_reached", restore.Code);
    }

    [Fact]
    public async Task Concurrent_creates_with_the_same_name_never_duplicate_a_slug_or_exceed_the_limit()
    {
        var bid = await NewBusinessAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try { return (await Svc(bid).CreateAsync(Req("Same Name"))).Slug; }
            catch (AppException ex) when (ex.Code == "slug_conflict") { return null; }
        }));
        var slugs = results.Where(s => s is not null).ToList();
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
        Assert.NotEmpty(slugs);
    }

    [Fact]
    public async Task Categories_cannot_be_deleted_while_in_use_and_names_are_unique_ignoring_case()
    {
        var bid = await NewBusinessAsync();
        var cat = await Svc(bid).CreateCategoryAsync(new UpsertCategoryRequest { Name = "Bridal" });
        var dup = await Assert.ThrowsAsync<AppException>(() => Svc(bid).CreateCategoryAsync(new UpsertCategoryRequest { Name = "bridal" }));
        Assert.Equal("category_exists", dup.Code);

        await Svc(bid).CreateAsync(new UpsertDesignRequest { Name = "Veil", Kind = "ReadyMade", CategoryId = cat.Id });
        var inUse = await Assert.ThrowsAsync<AppException>(() => Svc(bid).DeleteCategoryAsync(cat.Id));
        Assert.Equal("category_in_use", inUse.Code);
    }

    [Fact]
    public async Task The_database_itself_refuses_an_archived_public_design()
    {
        var bid = await NewBusinessAsync();
        var d = await Svc(bid).CreateAsync(Req("Cape"));
        await using var db = fx.Create(bid);
        var row = await db.Designs.SingleAsync(x => x.Id == d.Id);
        row.Status = DesignStatus.Archived; row.Visibility = DesignVisibility.Public;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
