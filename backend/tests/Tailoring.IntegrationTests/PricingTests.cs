using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Designs;
using Tailoring.Application.Orders;
using Tailoring.Application.Pricing;
using Tailoring.Domain.Access;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Pricing;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

public sealed class AllowAllPermissions : IPermissionService
{
    public Task<MembershipAccess?> GetAccessAsync(Guid userId, Guid businessId, CancellationToken ct = default)
        => Task.FromResult<MembershipAccess?>(new MembershipAccess(true, new HashSet<string>()));
}

public sealed class FixedPermissions(params string[] granted) : IPermissionService
{
    public Task<MembershipAccess?> GetAccessAsync(Guid userId, Guid businessId, CancellationToken ct = default)
        => Task.FromResult<MembershipAccess?>(new MembershipAccess(false, granted.ToHashSet()));
}

public sealed class FakeUserTenant(Guid businessId, Guid userId) : ITenantContext
{
    public Guid? UserId => userId;
    public Guid? BusinessId => businessId;
}

/// <summary>Pure maths: no database. Expected values are worked out by hand in the comments.</summary>
public sealed class PriceCalculatorTests
{
    private static readonly PricingRules NoExtras = new(1000m, 0m, PricingMethod.Margin, 0m, 0m, 0m);
    private static PricingLines Cost(decimal amount) => new([new MaterialLine("fabric", 1, amount)], [], []);

    [Fact]
    public void A_full_worked_example_is_exact()
    {
        // materials 3*2000 + 10*50 = 6500; labour 10h*1500 = 15000; other 1000
        // overhead 10% of (6500+15000) = 2150; total 24650
        // margin 40% -> 24650/0.6 = 41083.33; rounded UP to a 500 step = 41500
        var rules = new PricingRules(0m, 10m, PricingMethod.Margin, 40m, 7.5m, 500m);
        var lines = new PricingLines(
            [new MaterialLine("Ankara", 3, 2000m), new MaterialLine("Buttons", 10, 50m)],
            [new LabourLine("Sewing", 10, 1500m)], [new OtherCostLine("Delivery", 1000m)]);

        Assert.Empty(PriceCalculator.Validate(rules, lines));
        var o = PriceCalculator.Calculate(rules, lines);
        Assert.Equal((6500m, 15000m, 1000m, 2150m, 24650m), (o.MaterialCost, o.LabourCost, o.OtherCost, o.OverheadCost, o.TotalCost));
        Assert.Equal(41083.33m, o.SuggestedPriceBeforeRounding);
        Assert.Equal(41500m, o.UnitPrice);
        Assert.Equal(16850m, o.Profit);
        Assert.Equal(40.60m, o.MarginPercent);   // 16850 / 41500
        Assert.Equal(68.36m, o.MarkupPercent);   // 16850 / 24650
        Assert.Equal(3112.50m, o.TaxAmount);
        Assert.Equal(44612.50m, o.PriceIncludingTax);
    }

    [Fact]
    public void Markup_and_margin_are_different_numbers_for_the_same_percentage()
    {
        var markup = PriceCalculator.Calculate(NoExtras with { Method = PricingMethod.Markup, TargetPercent = 50m }, Cost(100m));
        Assert.Equal(150m, markup.UnitPrice);
        Assert.Equal(50m, markup.MarkupPercent);
        Assert.Equal(33.33m, markup.MarginPercent);

        var margin = PriceCalculator.Calculate(NoExtras with { Method = PricingMethod.Margin, TargetPercent = 50m }, Cost(100m));
        Assert.Equal(200m, margin.UnitPrice);
        Assert.Equal(50m, margin.MarginPercent);
        Assert.Equal(100m, margin.MarkupPercent);
    }

    [Theory]
    [InlineData(100, 50, 100)]   // already on a step
    [InlineData(101, 50, 150)]   // always up, never down
    [InlineData(149.99, 50, 150)]
    [InlineData(100, 0, 100)]    // no rounding
    public void Rounding_goes_up_to_the_step(double cost, double step, double expected)
    {
        var o = PriceCalculator.Calculate(NoExtras with { RoundingStep = (decimal)step }, Cost((decimal)cost));
        Assert.Equal((decimal)expected, o.UnitPrice);
        Assert.True(o.UnitPrice >= o.TotalCost);
    }

    [Fact]
    public void Complexity_scales_labour_only()
    {
        var lines = new PricingLines([new MaterialLine("x", 1, 500m)], [new LabourLine("Embroidery", 2, 1000m)], [], 1.5m);
        var o = PriceCalculator.Calculate(NoExtras, lines);
        Assert.Equal((500m, 3000m), (o.MaterialCost, o.LabourCost));
    }

    [Fact]
    public void Labour_without_a_rate_uses_the_profile_default()
    {
        var o = PriceCalculator.Calculate(NoExtras with { DefaultHourlyRate = 800m }, new PricingLines([], [new LabourLine("Cut", 2, null)], []));
        Assert.Equal(1600m, o.LabourCost);
    }

    [Fact]
    public void Each_line_is_rounded_half_away_from_zero_before_summing()
    {
        // 3 * 0.335 = 1.005 -> 1.01 per line; two such lines = 2.02 (not 2.01 from rounding the sum)
        var lines = new PricingLines([new MaterialLine("a", 3, 0.335m), new MaterialLine("b", 3, 0.335m)], [], []);
        Assert.Equal(2.02m, PriceCalculator.Calculate(NoExtras, lines).MaterialCost);
    }

    [Fact]
    public void Overhead_applies_to_materials_and_labour_but_not_to_other_costs()
    {
        var rules = NoExtras with { OverheadPercent = 10m };
        var lines = new PricingLines([new MaterialLine("m", 1, 100m)], [new LabourLine("l", 1, 100m)], [new OtherCostLine("outsourced", 1000m)]);
        Assert.Equal(20m, PriceCalculator.Calculate(rules, lines).OverheadCost);
    }

    [Fact]
    public void The_result_always_meets_the_target_and_never_sells_below_cost()
    {
        var rnd = new Random(42);
        for (var i = 0; i < 500; i++)
        {
            var method = i % 2 == 0 ? PricingMethod.Margin : PricingMethod.Markup;
            var target = method == PricingMethod.Margin ? rnd.Next(0, 90) : rnd.Next(0, 300);
            var rules = new PricingRules(rnd.Next(100, 3000), rnd.Next(0, 40), method, target, rnd.Next(0, 20), rnd.Next(0, 3) * 50);
            var lines = new PricingLines(
                [new MaterialLine("m", (decimal)rnd.Next(1, 2000) / 100, (decimal)rnd.Next(1, 500000) / 100)],
                [new LabourLine("l", (decimal)rnd.Next(1, 4000) / 100, null)],
                [new OtherCostLine("o", rnd.Next(0, 5000))], 1m + rnd.Next(0, 4) / 2m);
            Assert.Empty(PriceCalculator.Validate(rules, lines));

            var o = PriceCalculator.Calculate(rules, lines);
            Assert.True(o.UnitPrice >= o.TotalCost, $"#{i} sold below cost");
            Assert.Equal(o.TotalCost, o.MaterialCost + o.LabourCost + o.OtherCost + o.OverheadCost);
            Assert.Equal(o.UnitPrice - o.TotalCost, o.Profit);
            var achieved = method == PricingMethod.Margin ? o.MarginPercent : o.MarkupPercent;
            // the price is rounded to 2 places, so on tiny prices the achieved percentage can differ by up to 0.5/price points
            var tolerance = 0.5m / Math.Min(o.TotalCost, o.UnitPrice) + 0.01m;
            Assert.True(achieved >= target - tolerance, $"#{i} {method} target {target} achieved {achieved}");
        }
    }

    [Theory]
    [InlineData(PricingMethod.Margin, 95, true)]
    [InlineData(PricingMethod.Margin, 100, false)]   // would need an infinite price
    [InlineData(PricingMethod.Margin, -1, false)]
    [InlineData(PricingMethod.Markup, 1000, true)]
    [InlineData(PricingMethod.Markup, 1001, false)]
    public void Target_ranges_depend_on_the_method(PricingMethod method, int target, bool valid)
        => Assert.Equal(valid, PriceCalculator.Validate(NoExtras with { Method = method, TargetPercent = target }, Cost(100m)).Count == 0);

    [Fact]
    public void Bad_input_is_reported_not_silently_priced()
    {
        Assert.NotEmpty(PriceCalculator.Validate(NoExtras, new PricingLines([], [], [])));                                  // nothing to price
        Assert.NotEmpty(PriceCalculator.Validate(NoExtras, new PricingLines([new MaterialLine("x", 1, 0m)], [], [])));      // zero total
        Assert.NotEmpty(PriceCalculator.Validate(NoExtras, new PricingLines([new MaterialLine("x", -1, 5m)], [], [])));
        Assert.NotEmpty(PriceCalculator.Validate(NoExtras with { DefaultHourlyRate = 0 }, new PricingLines([], [new LabourLine("Sew", 3, null)], []))); // free labour by accident
        Assert.NotEmpty(PriceCalculator.Validate(NoExtras, new PricingLines([new MaterialLine("x", 1, 5m)], [], [], 0.4m)));
        Assert.NotEmpty(PriceCalculator.Validate(NoExtras with { OverheadPercent = 201 }, Cost(10m)));
        Assert.NotEmpty(PriceCalculator.Validate(NoExtras with { TaxPercent = 101 }, Cost(10m)));
    }
}

public sealed class PricingTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record Seed(Guid BusinessId, Guid LocationId, Guid CustomerId);
    private static readonly Guid Staff = Guid.NewGuid();

    private async Task<Seed> SeedAsync()
    {
        await using var db = fx.Create(null);
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        var loc = new BusinessLocation { BusinessId = b.Id, Name = "Main" };
        var customer = new Customer { BusinessId = b.Id, FullName = "Ada Obi" };
        db.Locations.Add(loc); db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return new Seed(b.Id, loc.Id, customer.Id);
    }

    private PricingService Pricing(Guid bid) { var db = fx.Create(bid); return new PricingService(db, new FakeUserTenant(bid, Staff), new AuditLogger(db)); }
    private DesignService Designs(Guid bid) { var db = fx.Create(bid); return new DesignService(db, new FakeUserTenant(bid, Staff), new AuditLogger(db), new EntitlementService(db)); }

    private OrderService Orders(Guid bid, IPermissionService? perms = null)
    {
        var db = fx.Create(bid); var t = new FakeUserTenant(bid, Staff);
        return new OrderService(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new EntitlementService(db),
            new PricingService(db, t, new AuditLogger(db)), perms ?? new AllowAllPermissions());
    }

    private static PricingInputDto Input(decimal fabric = 10000m) => new()
    {
        Materials = [new MaterialLineInput { Name = "Fabric", Quantity = 1, UnitCost = fabric }],
        Labour = [new LabourLineInput { Task = "Sewing", Hours = 5, HourlyRate = 1000m }],
    };

    private static UpsertPricingProfileRequest Profile(uint? version = null) => new()
    { DefaultHourlyRate = 1000m, OverheadPercent = 10m, Method = "Margin", TargetPercent = 40m, TaxPercent = 0m, RoundingStep = 100m, Version = version };

    private static CreateOrderRequest Order(Seed s, params OrderGarmentInput[] lines) => new()
    {
        CustomerId = s.CustomerId, LocationId = s.LocationId,
        PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), Garments = lines.ToList()
    };

    private static UpdateOrderRequest Update(Seed s, OrderDto o, params OrderGarmentInput[] lines) => new()
    { Version = o.Version, LocationId = s.LocationId, PromisedDate = o.PromisedDate, Garments = lines.ToList() };

    // ------------------------------------------------------------------ profile + calculate

    [Fact]
    public async Task An_unsaved_profile_reports_defaults_and_is_not_configured()
    {
        var s = await SeedAsync();
        var p = await Pricing(s.BusinessId).GetProfileAsync();
        Assert.False(p.IsConfigured);
        Assert.Equal("NGN", p.CurrencyCode);
        Assert.Null(p.Version);
    }

    [Fact]
    public async Task Profile_is_validated_versioned_and_one_per_business()
    {
        var s = await SeedAsync();
        var bad = await Assert.ThrowsAsync<AppException>(() => Pricing(s.BusinessId).UpsertProfileAsync(Profile() with { TargetPercent = 100m }));
        Assert.Equal("invalid_pricing_profile", bad.Code);

        var saved = await Pricing(s.BusinessId).UpsertProfileAsync(Profile());
        Assert.True(saved.IsConfigured);

        Assert.Equal("version_required", (await Assert.ThrowsAsync<AppException>(() => Pricing(s.BusinessId).UpsertProfileAsync(Profile()))).Code);
        var updated = await Pricing(s.BusinessId).UpsertProfileAsync(Profile(saved.Version) with { TargetPercent = 45m });
        Assert.Equal(45m, updated.TargetPercent);
        Assert.Equal("stale_version", (await Assert.ThrowsAsync<AppException>(() => Pricing(s.BusinessId).UpsertProfileAsync(Profile(saved.Version)))).Code);

        var other = await SeedAsync();
        Assert.False((await Pricing(other.BusinessId).GetProfileAsync()).IsConfigured); // another business is untouched
    }

    [Fact]
    public async Task Calculate_uses_the_profile_and_accepts_a_method_and_target_override()
    {
        var s = await SeedAsync();
        await Pricing(s.BusinessId).UpsertProfileAsync(Profile());

        // materials 10000 + labour 5000 = 15000; overhead 10% = 1500; total 16500; margin 40% -> 27500
        var normal = await Pricing(s.BusinessId).CalculateAsync(Input());
        Assert.True(normal.ProfileConfigured);
        Assert.Equal(16500m, normal.Result.TotalCost);
        Assert.Equal(27500m, normal.Result.UnitPrice);

        var markup = await Pricing(s.BusinessId).CalculateAsync(Input() with { Method = "Markup", TargetPercent = 50m });
        Assert.Equal(24800m, markup.Result.UnitPrice); // 16500 * 1.5 = 24750, rounded UP to the profile's 100 step
        Assert.Equal("Markup", markup.Rules.Method);
    }

    [Fact]
    public async Task Calculate_refuses_labour_with_no_rate_when_no_default_is_set()
    {
        var s = await SeedAsync(); // no profile: default hourly rate is 0
        var ex = await Assert.ThrowsAsync<AppException>(() => Pricing(s.BusinessId).CalculateAsync(new PricingInputDto
        { Labour = [new LabourLineInput { Task = "Sewing", Hours = 4 }] }));
        Assert.Equal("invalid_pricing", ex.Code);
    }

    // ------------------------------------------------------------------ orders + pricing

    [Fact]
    public async Task A_calculated_price_is_recomputed_by_the_server_and_stored_as_a_snapshot()
    {
        var s = await SeedAsync();
        await Pricing(s.BusinessId).UpsertProfileAsync(Profile());

        var (order, _) = await Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "Agbada", UnitPrice = 27500m, Pricing = Input() }), null);
        Assert.Equal(27500m, order.Total);

        var pricing = await Orders(s.BusinessId).GetPricingAsync(order.Id);
        var line = pricing.Lines.Single();
        Assert.Null(line.PriceOverrideReason);
        var calc = line.Calculation!.Value;
        Assert.Equal(16500m, calc.GetProperty("result").GetProperty("totalCost").GetDecimal());
        Assert.Equal(27500m, calc.GetProperty("finalUnitPrice").GetDecimal());

        // later rule changes never rewrite history
        var p = await Pricing(s.BusinessId).GetProfileAsync();
        await Pricing(s.BusinessId).UpsertProfileAsync(Profile(p.Version) with { TargetPercent = 60m });
        var again = (await Orders(s.BusinessId).GetPricingAsync(order.Id)).Lines.Single().Calculation!.Value;
        Assert.Equal(27500m, again.GetProperty("result").GetProperty("unitPrice").GetDecimal());
    }

    [Fact]
    public async Task Order_dtos_never_contain_costs_or_margins()
    {
        var s = await SeedAsync();
        await Pricing(s.BusinessId).UpsertProfileAsync(Profile());
        var (order, _) = await Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "Agbada", UnitPrice = 27500m, Pricing = Input() }), null);

        var json = JsonSerializer.Serialize(await Orders(s.BusinessId).GetAsync(order.Id));
        Assert.DoesNotContain("totalCost", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("marginPercent", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overhead", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Changing_the_calculated_price_needs_a_reason_and_the_reason_is_kept()
    {
        var s = await SeedAsync();
        await Pricing(s.BusinessId).UpsertProfileAsync(Profile());

        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "Agbada", UnitPrice = 25000m, Pricing = Input() }), null));
        Assert.Equal("override_reason_required", ex.Code);

        var (order, _) = await Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "Agbada", UnitPrice = 25000m, Pricing = Input(), PriceOverrideReason = "Loyal customer discount" }), null);
        var line = (await Orders(s.BusinessId).GetPricingAsync(order.Id)).Lines.Single();
        Assert.Equal("Loyal customer discount", line.PriceOverrideReason);
        Assert.Equal(25000m, line.Calculation!.Value.GetProperty("finalUnitPrice").GetDecimal());
        Assert.Equal(27500m, line.Calculation!.Value.GetProperty("suggestedUnitPrice").GetDecimal());
    }

    [Fact]
    public async Task A_reason_is_dropped_when_the_price_matches_the_calculation_and_refused_without_pricing()
    {
        var s = await SeedAsync();
        await Pricing(s.BusinessId).UpsertProfileAsync(Profile());
        var (order, _) = await Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "Agbada", UnitPrice = 27500m, Pricing = Input(), PriceOverrideReason = "not needed" }), null);
        Assert.Null((await Orders(s.BusinessId).GetPricingAsync(order.Id)).Lines.Single().PriceOverrideReason);

        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "Cap", UnitPrice = 100m, PriceOverrideReason = "because" }), null));
        Assert.Equal("override_without_pricing", ex.Code);
    }

    [Fact]
    public async Task Using_the_calculator_on_an_order_needs_the_pricing_permission()
    {
        var s = await SeedAsync();
        await Pricing(s.BusinessId).UpsertProfileAsync(Profile());
        var noPricing = new FixedPermissions(Permissions.OrdersManage);

        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(s.BusinessId, noPricing).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "Agbada", UnitPrice = 27500m, Pricing = Input() }), null));
        Assert.Equal(403, ex.StatusCode);

        // the same user can still create an order with a manual price
        var (manual, _) = await Orders(s.BusinessId, noPricing).CreateAsync(Order(s, new OrderGarmentInput { Name = "Cap", UnitPrice = 500m }), null);
        Assert.Equal(500m, manual.Total);

        var allowed = new FixedPermissions(Permissions.OrdersManage, Permissions.PricingView);
        await Orders(s.BusinessId, allowed).CreateAsync(Order(s, new OrderGarmentInput { Name = "Agbada", UnitPrice = 27500m, Pricing = Input() }), null);
    }

    [Fact]
    public async Task A_manual_price_change_drops_the_snapshot_but_an_unchanged_price_keeps_it()
    {
        var s = await SeedAsync();
        await Pricing(s.BusinessId).UpsertProfileAsync(Profile());
        var (order, _) = await Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "Agbada", UnitPrice = 27500m, Pricing = Input() }), null);
        var lineId = order.Garments.Single().Id;

        // edit notes only (same price, no Pricing sent): the snapshot stays
        var kept = await Orders(s.BusinessId).UpdateAsync(order.Id, Update(s, order,
            new OrderGarmentInput { Id = lineId, Name = "Agbada", UnitPrice = 27500m, StyleInstructions = "longer sleeves" }));
        Assert.NotNull((await Orders(s.BusinessId).GetPricingAsync(order.Id)).Lines.Single().Calculation);

        // change the price by hand: the old calculation no longer explains it
        await Orders(s.BusinessId).UpdateAsync(order.Id, Update(s, kept,
            new OrderGarmentInput { Id = lineId, Name = "Agbada", UnitPrice = 30000m }));
        Assert.Null((await Orders(s.BusinessId).GetPricingAsync(order.Id)).Lines.Single().Calculation);
    }

    // ------------------------------------------------------------------ design link

    [Fact]
    public async Task A_design_link_snapshots_name_and_price_and_survives_rename_and_archive()
    {
        var s = await SeedAsync();
        var design = await Designs(s.BusinessId).CreateAsync(new UpsertDesignRequest { Name = "Royal Agbada", Kind = "MadeToOrder", BasePrice = 90000m });

        var (order, _) = await Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "Agbada", UnitPrice = 90000m, DesignId = design.Id }), null);
        Assert.Equal(("Royal Agbada", design.Id), (order.Garments.Single().DesignName, order.Garments.Single().DesignId));

        await Designs(s.BusinessId).UpdateAsync(design.Id, new UpsertDesignRequest { Name = "Renamed", Kind = "MadeToOrder", BasePrice = 120000m, Version = design.Version });
        await Designs(s.BusinessId).SetStatusAsync(design.Id, new SetDesignStatusRequest { Status = "Archived" });

        var reloaded = await Orders(s.BusinessId).GetAsync(order.Id);
        Assert.Equal("Royal Agbada", reloaded.Garments.Single().DesignName); // history unchanged
        Assert.Equal(90000m, (await Orders(s.BusinessId).GetPricingAsync(order.Id)).Lines.Single().DesignPriceSnapshot);

        // editing other things on the line keeps the link even though the design is archived now
        var edited = await Orders(s.BusinessId).UpdateAsync(order.Id, Update(s, reloaded,
            new OrderGarmentInput { Id = reloaded.Garments.Single().Id, Name = "Agbada", UnitPrice = 90000m, DesignId = design.Id, StyleInstructions = "gold trim" }));
        Assert.Equal(design.Id, edited.Garments.Single().DesignId);
    }

    [Fact]
    public async Task Archived_and_other_businesses_designs_cannot_be_linked_and_unlinking_clears_the_snapshot()
    {
        var s = await SeedAsync(); var other = await SeedAsync();
        var mine = await Designs(s.BusinessId).CreateAsync(new UpsertDesignRequest { Name = "Mine 1", Kind = "ReadyMade" });
        var theirs = await Designs(other.BusinessId).CreateAsync(new UpsertDesignRequest { Name = "Theirs 1", Kind = "ReadyMade" });
        var old = await Designs(s.BusinessId).CreateAsync(new UpsertDesignRequest { Name = "Old 1", Kind = "ReadyMade" });
        await Designs(s.BusinessId).SetStatusAsync(old.Id, new SetDesignStatusRequest { Status = "Archived" });

        Assert.Equal("unknown_design", (await Assert.ThrowsAsync<AppException>(() => Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "X", UnitPrice = 1m, DesignId = theirs.Id }), null))).Code);
        Assert.Equal("design_archived", (await Assert.ThrowsAsync<AppException>(() => Orders(s.BusinessId).CreateAsync(Order(s,
            new OrderGarmentInput { Name = "X", UnitPrice = 1m, DesignId = old.Id }), null))).Code);

        var (order, _) = await Orders(s.BusinessId).CreateAsync(Order(s, new OrderGarmentInput { Name = "X", UnitPrice = 1m, DesignId = mine.Id }), null);
        var unlinked = await Orders(s.BusinessId).UpdateAsync(order.Id, Update(s, order,
            new OrderGarmentInput { Id = order.Garments.Single().Id, Name = "X", UnitPrice = 1m }));
        Assert.Null(unlinked.Garments.Single().DesignId);
        Assert.Null(unlinked.Garments.Single().DesignName);
    }
}
