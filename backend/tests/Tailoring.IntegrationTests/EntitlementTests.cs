using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Pure rules: no database needed.</summary>
public sealed class EntitlementRuleTests
{
    private static EffectiveEntitlements For(string code, IEnumerable<TenantEntitlementOverride>? overrides = null)
    {
        var seed = DefaultPlans.All.Single(p => p.Code == code);
        return new EffectiveEntitlements(seed.Code, seed.Name, SubscriptionStatus.Active, seed.Values, overrides ?? [], DateTime.UtcNow);
    }

    [Fact]
    public void Default_plans_only_contain_valid_values_for_catalogue_keys()
    {
        foreach (var plan in DefaultPlans.All)
        {
            Assert.Equal(EntitlementCatalog.All.Select(d => d.Key).Order(), plan.Values.Keys.Order()); // no missing, no unknown
            foreach (var (k, v) in plan.Values) Assert.Null(EntitlementCatalog.Validate(k, v));
        }
    }

    [Fact]
    public void Plans_are_monotonic_each_tier_grants_at_least_what_the_one_below_does()
    {
        var order = new[] { "free", "starter", "professional", "business" };
        for (var i = 1; i < order.Length; i++)
        {
            var lower = For(order[i - 1]); var higher = For(order[i]);
            foreach (var d in EntitlementCatalog.All)
            {
                switch (d.Kind)
                {
                    case EntitlementKind.Limit: Assert.True(higher.Limit(d.Key).Max >= lower.Limit(d.Key).Max, d.Key); break;
                    case EntitlementKind.Flag: Assert.True(!lower.Flag(d.Key) || higher.Flag(d.Key), d.Key); break;
                    default: Assert.True(higher.TierAtLeast(d.Key, lower.Tier(d.Key)), d.Key); break;
                }
            }
        }
    }

    [Fact]
    public void Override_beats_plan_but_an_expired_override_is_ignored()
    {
        var live = new TenantEntitlementOverride { Key = EntitlementKeys.UsersMax, Value = "50", Reason = "enterprise" };
        var expired = new TenantEntitlementOverride { Key = EntitlementKeys.CustomersActiveMax, Value = "99999", Reason = "trial", ExpiresAtUtc = DateTime.UtcNow.AddDays(-1) };
        var e = For("free", [live, expired]);
        Assert.Equal(50, e.Limit(EntitlementKeys.UsersMax).Max);
        Assert.Equal(30, e.Limit(EntitlementKeys.CustomersActiveMax).Max);
    }

    [Fact]
    public void Missing_or_corrupt_values_fall_back_to_the_most_restrictive_default()
    {
        var e = new EffectiveEntitlements("x", "X", SubscriptionStatus.Active,
            new Dictionary<string, string> { [EntitlementKeys.UsersMax] = "lots", [EntitlementKeys.StorefrontBuilder] = "yes" }, [], DateTime.UtcNow);
        Assert.Equal(0, e.Limit(EntitlementKeys.UsersMax).Max);
        Assert.False(e.Flag(EntitlementKeys.StorefrontBuilder));
        Assert.Equal("standard", e.Tier(EntitlementKeys.StorefrontTemplateTier));
        Assert.Equal(0, e.Limit(EntitlementKeys.OrdersMonthlyMax).Max); // key absent entirely
    }

    [Fact]
    public void Storefront_tiers_match_the_spec_per_plan()
    {
        Assert.False(For("starter").Flag(EntitlementKeys.StorefrontBuilder));
        Assert.True(For("starter").TierAtLeast(EntitlementKeys.StorefrontTemplateTier, "basic"));
        Assert.False(For("starter").TierAtLeast(EntitlementKeys.StorefrontTemplateTier, "advanced"));
        Assert.True(For("professional").Flag(EntitlementKeys.StorefrontBuilder));
        Assert.Equal(1, For("professional").Limit(EntitlementKeys.StorefrontCountMax).Max);
        Assert.Equal(5, For("business").Limit(EntitlementKeys.StorefrontCountMax).Max);
        Assert.False(For("business").Flag(EntitlementKeys.StorefrontCustomDomain)); // not advertised until built
    }

    [Theory]
    [InlineData(EntitlementKeys.UsersMax, "-1")]
    [InlineData(EntitlementKeys.UsersMax, "1.5")]
    [InlineData(EntitlementKeys.StorefrontBuilder, "maybe")]
    [InlineData(EntitlementKeys.StorefrontThemeLevel, "ultra")]
    [InlineData("not.a.key", "1")]
    public void Admin_edits_are_validated(string key, string value) => Assert.NotNull(EntitlementCatalog.Validate(key, value));

    [Fact]
    public void Unlimited_is_a_valid_limit() => Assert.Null(EntitlementCatalog.Validate(EntitlementKeys.UsersMax, "unlimited"));
}

public sealed class EntitlementEnforcementTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private async Task<Guid> BusinessOnAsync(string planCode, SubscriptionStatus status = SubscriptionStatus.Active)
    {
        await using var db = fx.Create(null);
        var plan = await db.Plans.FirstAsync(p => p.Code == planCode);
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = plan.Id, Status = status });
        await db.SaveChangesAsync();
        return b.Id;
    }

    private async Task OverrideAsync(Guid bid, string key, string value)
    {
        await using var db = fx.Create(null);
        db.EntitlementOverrides.Add(new TenantEntitlementOverride { BusinessId = bid, Key = key, Value = value, Reason = "test" });
        await db.SaveChangesAsync();
    }

    private CustomerService Customers(Guid bid) { var db = fx.Create(bid); return new CustomerService(db, new FakeTenant(bid), new AuditLogger(db), new EntitlementService(db)); }

    private static UpsertCustomerRequest Named(string n) => new() { FullName = n, AllowDuplicate = true };

    [Fact]
    public async Task Customer_limit_blocks_creation_with_a_clear_code_and_nothing_is_deleted()
    {
        var bid = await BusinessOnAsync("free");
        await OverrideAsync(bid, EntitlementKeys.CustomersActiveMax, "2");
        await Customers(bid).CreateAsync(Named("A"));
        await Customers(bid).CreateAsync(Named("B"));

        var ex = await Assert.ThrowsAsync<AppException>(() => Customers(bid).CreateAsync(Named("C")));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("plan_limit_reached", ex.Code);
        Assert.Equal(2L, ex.Extensions!["limit"]);

        await using var db = fx.Create(bid);
        Assert.Equal(2, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task Deactivating_frees_a_slot_and_reactivating_is_limited_too()
    {
        var bid = await BusinessOnAsync("free");
        await OverrideAsync(bid, EntitlementKeys.CustomersActiveMax, "1");
        var first = await Customers(bid).CreateAsync(Named("A"));
        await Customers(bid).SetStatusAsync(first.Id, new SetCustomerStatusRequest { Status = "Inactive" });
        var second = await Customers(bid).CreateAsync(Named("B"));

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Customers(bid).SetStatusAsync(first.Id, new SetCustomerStatusRequest { Status = "Active" }));
        Assert.Equal("plan_limit_reached", ex.Code);
        Assert.NotEqual(Guid.Empty, second.Id);
    }

    [Fact]
    public async Task Concurrent_consumers_can_never_overshoot_a_monthly_limit()
    {
        var bid = await BusinessOnAsync("free");
        await OverrideAsync(bid, EntitlementKeys.OrdersMonthlyMax, "3");

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = fx.Create(bid);
            try { await new EntitlementService(db).ConsumeAsync(bid, EntitlementKeys.OrdersMonthlyMax); return true; }
            catch (AppException ex) when (ex.Code == "plan_limit_reached") { return false; }
        }));

        Assert.Equal(3, results.Count(r => r));
        await using var check = fx.Create(bid);
        Assert.Equal(3, await check.UsageCounters.Where(c => c.Key == EntitlementKeys.OrdersMonthlyMax).Select(c => c.Count).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_creators_can_never_overshoot_a_stock_limit()
    {
        var bid = await BusinessOnAsync("free");
        await OverrideAsync(bid, EntitlementKeys.CustomersActiveMax, "4");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
        {
            try { await Customers(bid).CreateAsync(Named("C" + i)); return true; }
            catch (AppException ex) when (ex.Code == "plan_limit_reached") { return false; }
        }));

        Assert.Equal(4, results.Count(r => r));
        await using var db = fx.Create(bid);
        Assert.Equal(4, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task One_tenants_usage_never_affects_another()
    {
        var a = await BusinessOnAsync("free"); var b = await BusinessOnAsync("free");
        await OverrideAsync(a, EntitlementKeys.OrdersMonthlyMax, "1");
        await using (var da = fx.Create(a)) await new EntitlementService(da).ConsumeAsync(a, EntitlementKeys.OrdersMonthlyMax);
        await using (var da = fx.Create(a))
            await Assert.ThrowsAsync<AppException>(() => new EntitlementService(da).ConsumeAsync(a, EntitlementKeys.OrdersMonthlyMax));

        await using var db = fx.Create(b);
        await new EntitlementService(db).ConsumeAsync(b, EntitlementKeys.OrdersMonthlyMax); // B still has its own 20
    }

    [Fact]
    public async Task Downgrade_keeps_existing_records_but_blocks_new_ones()
    {
        var bid = await BusinessOnAsync("professional");
        for (var i = 0; i < 31; i++) await Customers(bid).CreateAsync(Named("P" + i)); // above Free's 30

        await using (var db = fx.Create(null))
        {
            var free = await db.Plans.FirstAsync(p => p.Code == "free");
            var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.BusinessId == bid);
            sub.PlanId = free.Id;
            await db.SaveChangesAsync();
        }

        await using (var db = fx.Create(bid)) Assert.Equal(31, await db.Customers.CountAsync()); // nothing deleted
        var ex = await Assert.ThrowsAsync<AppException>(() => Customers(bid).CreateAsync(Named("new")));
        Assert.Equal("plan_limit_reached", ex.Code);
    }

    [Fact]
    public async Task Expired_subscription_behaves_as_the_free_plan()
    {
        var bid = await BusinessOnAsync("business", SubscriptionStatus.Expired);
        await using var db = fx.Create(bid);
        Assert.Equal("free", (await new EntitlementService(db).GetAsync(bid)).PlanCode);
    }

    [Fact]
    public async Task Feature_gates_throw_feature_not_in_plan()
    {
        var bid = await BusinessOnAsync("starter");
        await using var db = fx.Create(bid);
        var svc = new EntitlementService(db);
        var ex = await Assert.ThrowsAsync<AppException>(() => svc.RequireFlagAsync(bid, EntitlementKeys.StorefrontBuilder));
        Assert.Equal("feature_not_in_plan", ex.Code);
        await svc.RequireTierAsync(bid, EntitlementKeys.StorefrontTemplateTier, "basic"); // allowed
    }

    [Fact]
    public async Task Stock_limit_check_refuses_to_run_outside_a_transaction()
    {
        var bid = await BusinessOnAsync("free");
        await using var db = fx.Create(bid);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EntitlementService(db).EnsureBelowLimitAsync(bid, EntitlementKeys.UsersMax, _ => Task.FromResult(0L)));
    }
}
