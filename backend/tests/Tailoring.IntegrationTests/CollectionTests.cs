using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Collections;
using Tailoring.Application.Common;
using Tailoring.Application.Designs;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

public sealed class CollectionTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private async Task<Guid> NewBusinessAsync()
    {
        await using var db = fx.Create(null);
        var p = await db.Plans.FirstAsync(x => x.Code == "professional");
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = p.Id });
        await db.SaveChangesAsync();
        return b.Id;
    }

    private CollectionService Cols(Guid bid) { var db = fx.Create(bid); return new CollectionService(db, new FakeTenant(bid), new AuditLogger(db)); }
    private DesignService Designs(Guid bid) { var db = fx.Create(bid); return new DesignService(db, new FakeTenant(bid), new AuditLogger(db), new EntitlementService(db)); }
    private async Task<Guid> DesignAsync(Guid bid, string name) => (await Designs(bid).CreateAsync(new UpsertDesignRequest { Name = name, Kind = "ReadyMade" })).Id;
    private static UpsertCollectionRequest Req(string name, string visibility = "Private") => new() { Name = name, Visibility = visibility };

    [Fact]
    public async Task Slugs_are_unique_per_business_and_stable_when_renamed()
    {
        var bid = await NewBusinessAsync();
        var a = await Cols(bid).CreateAsync(Req("Bridal 2026"));
        var b = await Cols(bid).CreateAsync(Req("Bridal 2026"));
        Assert.Equal(("bridal-2026", "bridal-2026-2"), (a.Slug, b.Slug));

        var renamed = await Cols(bid).UpdateAsync(a.Id, new UpsertCollectionRequest { Name = "Wedding Season", Visibility = "Private", Version = a.Version });
        Assert.Equal("Wedding Season", renamed.Name);
        Assert.Equal("bridal-2026", renamed.Slug);

        var other = await NewBusinessAsync();
        Assert.Equal("bridal-2026", (await Cols(other).CreateAsync(Req("Bridal 2026"))).Slug);
    }

    [Fact]
    public async Task Items_keep_their_order_and_can_be_replaced_and_reordered()
    {
        var bid = await NewBusinessAsync();
        var (d1, d2, d3) = (await DesignAsync(bid, "One 1"), await DesignAsync(bid, "Two 2"), await DesignAsync(bid, "Three 3"));
        var col = await Cols(bid).CreateAsync(Req("Capsule"));

        var set = await Cols(bid).SetItemsAsync(col.Id, new SetCollectionItemsRequest { DesignIds = [d3, d1, d2] });
        Assert.Equal(new[] { d3, d1, d2 }, set.Items.Select(i => i.DesignId));

        var reordered = await Cols(bid).SetItemsAsync(col.Id, new SetCollectionItemsRequest { DesignIds = [d2, d3] });
        Assert.Equal(new[] { d2, d3 }, reordered.Items.Select(i => i.DesignId));
        Assert.Equal(2, (await Cols(bid).ListAsync(new CollectionListQuery())).Items.Single().DesignCount);
    }

    [Fact]
    public async Task Rejects_duplicates_unknown_other_tenants_and_archived_designs()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var mine = await DesignAsync(a, "Mine 1"); var theirs = await DesignAsync(b, "Theirs 1");
        var old = await DesignAsync(a, "Old 1");
        await Designs(a).SetStatusAsync(old, new SetDesignStatusRequest { Status = "Archived" });
        var col = await Cols(a).CreateAsync(Req("Set"));

        async Task<string> Code(params Guid[] ids) =>
            (await Assert.ThrowsAsync<AppException>(() => Cols(a).SetItemsAsync(col.Id, new SetCollectionItemsRequest { DesignIds = ids.ToList() }))).Code;

        Assert.Equal("duplicate_design", await Code(mine, mine));
        Assert.Equal("unknown_design", await Code(theirs));
        Assert.Equal("unknown_design", await Code(Guid.NewGuid()));
        Assert.Equal("design_archived", await Code(old));
    }

    [Fact]
    public async Task Public_count_shows_how_many_designs_a_visitor_would_really_see()
    {
        var bid = await NewBusinessAsync();
        var (pub, priv, archived) = (await DesignAsync(bid, "Public 1"), await DesignAsync(bid, "Private 1"), await DesignAsync(bid, "Soon gone"));
        await Designs(bid).SetVisibilityAsync(pub, new SetDesignVisibilityRequest { Visibility = "Public" });
        await Designs(bid).SetVisibilityAsync(archived, new SetDesignVisibilityRequest { Visibility = "Public" });
        var col = await Cols(bid).CreateAsync(Req("Showcase", "Public"));
        await Cols(bid).SetItemsAsync(col.Id, new SetCollectionItemsRequest { DesignIds = [pub, priv, archived] });

        var s = (await Cols(bid).ListAsync(new CollectionListQuery())).Items.Single();
        Assert.Equal((3, 2), (s.DesignCount, s.PublicDesignCount));

        await Designs(bid).SetStatusAsync(archived, new SetDesignStatusRequest { Status = "Archived" }); // stays in the collection, stops being public
        s = (await Cols(bid).ListAsync(new CollectionListQuery())).Items.Single();
        Assert.Equal((3, 1), (s.DesignCount, s.PublicDesignCount));
        Assert.Equal("Archived", (await Cols(bid).GetAsync(col.Id)).Items.Single(i => i.DesignId == archived).Status);
    }

    [Fact]
    public async Task Another_business_cannot_read_change_or_delete_a_collection()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var col = await Cols(a).CreateAsync(Req("Private Line"));

        Assert.Equal("collection_not_found", (await Assert.ThrowsAsync<AppException>(() => Cols(b).GetAsync(col.Id))).Code);
        await Assert.ThrowsAsync<AppException>(() => Cols(b).DeleteAsync(col.Id));
        await Assert.ThrowsAsync<AppException>(() => Cols(b).SetItemsAsync(col.Id, new SetCollectionItemsRequest()));
        Assert.Empty((await Cols(b).ListAsync(new CollectionListQuery())).Items);
    }

    [Fact]
    public async Task Stale_versions_are_rejected_and_a_version_is_required()
    {
        var bid = await NewBusinessAsync();
        var col = await Cols(bid).CreateAsync(Req("Autumn"));
        await Cols(bid).UpdateAsync(col.Id, new UpsertCollectionRequest { Name = "Autumn 2", Visibility = "Private", Version = col.Version });

        Assert.Equal("stale_version", (await Assert.ThrowsAsync<AppException>(() =>
            Cols(bid).UpdateAsync(col.Id, new UpsertCollectionRequest { Name = "Autumn 3", Visibility = "Private", Version = col.Version }))).Code);
        Assert.Equal("version_required", (await Assert.ThrowsAsync<AppException>(() => Cols(bid).UpdateAsync(col.Id, Req("Autumn 4")))).Code);
    }

    [Fact]
    public async Task Deleting_a_collection_keeps_its_designs()
    {
        var bid = await NewBusinessAsync();
        var d = await DesignAsync(bid, "Keeper");
        var col = await Cols(bid).CreateAsync(Req("Temp"));
        await Cols(bid).SetItemsAsync(col.Id, new SetCollectionItemsRequest { DesignIds = [d] });

        await Cols(bid).DeleteAsync(col.Id);
        Assert.Equal("collection_not_found", (await Assert.ThrowsAsync<AppException>(() => Cols(bid).GetAsync(col.Id))).Code);
        Assert.Equal("Keeper", (await Designs(bid).GetAsync(d)).Name);
    }

    [Fact]
    public async Task Search_and_visibility_filters_work()
    {
        var bid = await NewBusinessAsync();
        await Cols(bid).CreateAsync(Req("Bridal", "Public"));
        await Cols(bid).CreateAsync(Req("Streetwear"));
        Assert.Equal("Bridal", (await Cols(bid).ListAsync(new CollectionListQuery { Visibility = "Public" })).Items.Single().Name);
        Assert.Equal("Streetwear", (await Cols(bid).ListAsync(new CollectionListQuery { Search = "STREET" })).Items.Single().Name);
    }
}
