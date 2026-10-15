using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Application.Measurements;
using Tailoring.Application.Orders;
using Tailoring.Domain.Common;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Measurements;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

public sealed class Phase2Tests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record Seed(Guid BusinessId, Guid LocationId, Guid CustomerId, Guid SetId);

    private static readonly Dictionary<string, decimal> ShirtValues = new()
    {
        ["neck"] = 40, ["chest"] = 100, ["shoulder"] = 46, ["sleeve_length"] = 64, ["shirt_length"] = 75
    };

    private OrderService Orders(Guid bid) { var db = fx.Create(bid); var t = new FakeTenant(bid); return new OrderService(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new EntitlementService(db),
        new PricingService(db, t, new AuditLogger(db)), new AllowAllPermissions()); }
    private MeasurementService Measures(Guid bid) { var db = fx.Create(bid); return new MeasurementService(db, new FakeTenant(bid), new AuditLogger(db)); }
    private CustomerService Customers(Guid bid) { var db = fx.Create(bid); return new CustomerService(db, new FakeTenant(bid), new AuditLogger(db), new EntitlementService(db)); }

    private async Task<Seed> SeedAsync(string? phone = null)
    {
        Guid bid, lid, cid, tid;
        await using (var db = fx.Create(null))
        {
            var business = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "USD", TimeZoneId = "UTC" };
            db.Businesses.Add(business);
            var loc = new BusinessLocation { BusinessId = business.Id, Name = "Main" };
            var customer = new Customer { BusinessId = business.Id, FullName = "Ada Obi", Phone = phone, NormalizedPhone = Normalize.Phone(phone) };
            var template = DefaultMeasurementTemplates.Create(business.Id).First(t => t.Name == "Shirt");
            db.Locations.Add(loc); db.Customers.Add(customer); db.MeasurementTemplates.Add(template);
            await db.SaveChangesAsync();
            (bid, lid, cid, tid) = (business.Id, loc.Id, customer.Id, template.Id);
        }
        var set = await Measures(bid).CreateSetAsync(cid, new CreateMeasurementSetRequest { TemplateId = tid, Label = "Everyday", Values = ShirtValues });
        return new Seed(bid, lid, cid, set.Id);
    }

    private static CreateOrderRequest NewOrder(Seed s, Guid? setId = null, decimal discount = 0) => new()
    {
        CustomerId = s.CustomerId, LocationId = s.LocationId, Discount = discount,
        PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
        Garments =
        [
            new OrderGarmentInput { Name = "Shirt", Quantity = 2, UnitPrice = 150.00m, MeasurementSetId = setId },
            new OrderGarmentInput { Name = "Cap", Quantity = 1, UnitPrice = 99.99m }
        ]
    };

    [Fact]
    public async Task Totals_are_computed_on_the_server_and_order_numbers_are_sequential()
    {
        var s = await SeedAsync();
        var (first, created) = await Orders(s.BusinessId).CreateAsync(NewOrder(s, discount: 10m), null);
        var (second, _) = await Orders(s.BusinessId).CreateAsync(NewOrder(s), null);

        Assert.True(created);
        Assert.Equal(399.99m, first.Subtotal);
        Assert.Equal(389.99m, first.Total);
        Assert.Equal("USD", first.CurrencyCode);
        Assert.EndsWith("00001", first.OrderNumber);
        Assert.EndsWith("00002", second.OrderNumber);
    }

    [Fact]
    public async Task Discount_larger_than_subtotal_is_rejected()
    {
        var s = await SeedAsync();
        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(s.BusinessId).CreateAsync(NewOrder(s, discount: 5000m), null));
        Assert.Equal("discount_exceeds_total", ex.Code);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_order()
    {
        var s = await SeedAsync();
        var (a, createdA) = await Orders(s.BusinessId).CreateAsync(NewOrder(s), "key-123");
        var (b, createdB) = await Orders(s.BusinessId).CreateAsync(NewOrder(s), "key-123");

        Assert.True(createdA);
        Assert.False(createdB);
        Assert.Equal(a.Id, b.Id);
        await using var db = fx.Create(s.BusinessId);
        Assert.Equal(1, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Measurement_snapshot_stays_frozen_when_the_customer_is_remeasured()
    {
        var s = await SeedAsync();
        var (order, _) = await Orders(s.BusinessId).CreateAsync(NewOrder(s, s.SetId), null);
        Assert.Equal(1, order.Garments[0].MeasurementRevision);

        var changed = new Dictionary<string, decimal>(ShirtValues) { ["chest"] = 108 };
        var updated = await Measures(s.BusinessId).AddRevisionAsync(s.SetId, new AddRevisionRequest { Values = changed });
        Assert.Equal(2, updated.CurrentRevision);

        var reloaded = await Orders(s.BusinessId).GetAsync(order.Id);
        var line = reloaded.Garments.Single(g => g.MeasurementSetId == s.SetId);
        Assert.Equal(1, line.MeasurementRevision);
        Assert.Contains("100", line.MeasurementSnapshot!.Value.GetRawText()); // old chest value, not 108
        Assert.DoesNotContain("108", line.MeasurementSnapshot!.Value.GetRawText());
    }

    [Fact]
    public async Task Editing_an_order_keeps_existing_snapshots_unless_a_new_revision_is_requested()
    {
        var s = await SeedAsync();
        var (order, _) = await Orders(s.BusinessId).CreateAsync(NewOrder(s, s.SetId), null);
        await Measures(s.BusinessId).AddRevisionAsync(s.SetId, new AddRevisionRequest { Values = new(ShirtValues) { ["chest"] = 108 } });

        var edit = new UpdateOrderRequest
        {
            Version = order.Version, LocationId = order.LocationId, PromisedDate = order.PromisedDate,
            Garments = order.Garments.Select(g => new OrderGarmentInput
            {
                Id = g.Id, Name = g.Name, Quantity = g.Quantity + 1, UnitPrice = g.UnitPrice, MeasurementSetId = g.MeasurementSetId
            }).ToList()
        };
        var after = await Orders(s.BusinessId).UpdateAsync(order.Id, edit);
        Assert.Equal(1, after.Garments.Single(g => g.MeasurementSetId == s.SetId).MeasurementRevision);
        Assert.Equal(3 * 150.00m + 2 * 99.99m, after.Total);
    }

    [Fact]
    public async Task An_order_cannot_use_another_customers_measurement_profile()
    {
        var a = await SeedAsync();
        var otherCustomer = await Customers(a.BusinessId).CreateAsync(new UpsertCustomerRequest { FullName = "Someone Else" });
        var request = new CreateOrderRequest
        {
            CustomerId = otherCustomer.Id, LocationId = a.LocationId,
            PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Garments = [new OrderGarmentInput { Name = "Shirt", UnitPrice = 10, MeasurementSetId = a.SetId }]
        };
        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(a.BusinessId).CreateAsync(request, null));
        Assert.Equal("unknown_measurement_set", ex.Code);
    }

    [Fact]
    public async Task Workflow_requires_quote_before_confirm_and_confirmed_orders_are_locked()
    {
        var s = await SeedAsync();
        var (order, _) = await Orders(s.BusinessId).CreateAsync(NewOrder(s), null);

        var early = await Assert.ThrowsAsync<AppException>(() => Orders(s.BusinessId).ConfirmAsync(order.Id));
        Assert.Equal("invalid_transition", early.Code);

        await Orders(s.BusinessId).QuoteAsync(order.Id);
        var confirmed = await Orders(s.BusinessId).ConfirmAsync(order.Id);
        Assert.Equal("Confirmed", confirmed.Status);

        var edit = new UpdateOrderRequest
        {
            Version = confirmed.Version, LocationId = confirmed.LocationId, PromisedDate = confirmed.PromisedDate,
            Garments = [new OrderGarmentInput { Name = "Shirt", UnitPrice = 1 }]
        };
        var locked = await Assert.ThrowsAsync<AppException>(() => Orders(s.BusinessId).UpdateAsync(order.Id, edit));
        Assert.Equal("order_locked", locked.Code);
    }

    [Fact]
    public async Task Stale_version_is_rejected()
    {
        var s = await SeedAsync();
        var (order, _) = await Orders(s.BusinessId).CreateAsync(NewOrder(s), null);
        var edit = new UpdateOrderRequest
        {
            Version = order.Version + 12345, LocationId = order.LocationId, PromisedDate = order.PromisedDate,
            Garments = order.Garments.Select(g => new OrderGarmentInput { Id = g.Id, Name = g.Name, Quantity = g.Quantity, UnitPrice = g.UnitPrice }).ToList()
        };
        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(s.BusinessId).UpdateAsync(order.Id, edit));
        Assert.Equal("stale_version", ex.Code);
    }

    [Fact]
    public async Task Another_business_cannot_see_the_order()
    {
        var a = await SeedAsync();
        var b = await SeedAsync();
        var (order, _) = await Orders(a.BusinessId).CreateAsync(NewOrder(a), null);

        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(b.BusinessId).GetAsync(order.Id));
        Assert.Equal("order_not_found", ex.Code);
        Assert.Empty((await Orders(b.BusinessId).ListAsync(new OrderListQuery())).Items);
    }

    [Fact]
    public async Task Database_rejects_orders_whose_total_does_not_match_subtotal_minus_discount()
    {
        var s = await SeedAsync();
        await using var db = fx.Create(null);
        db.Orders.Add(new Order
        {
            BusinessId = s.BusinessId, LocationId = s.LocationId, CustomerId = s.CustomerId, OrderNumber = "ORD-BAD-1",
            CurrencyCode = "USD", Subtotal = 100m, Discount = 0m, Total = 1m, PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Measurement_values_are_validated_against_the_template()
    {
        var s = await SeedAsync();
        var bad = new Dictionary<string, decimal>(ShirtValues) { ["chest"] = 9999, ["wingspan"] = 5 };
        bad.Remove("neck");
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Measures(s.BusinessId).AddRevisionAsync(s.SetId, new AddRevisionRequest { Values = bad }));
        Assert.Equal("invalid_measurements", ex.Code);
        Assert.Contains("Neck is required", ex.Message);
        Assert.Contains("wingspan", ex.Message);
    }

    [Fact]
    public async Task Duplicate_customers_are_flagged_unless_explicitly_allowed()
    {
        var s = await SeedAsync(phone: "+234 803 555 0100");
        // Note: "+234 803 555 0100" and "0803-555-0100" normalise differently (documented limitation), so reuse the same number.
        var same = new UpsertCustomerRequest { FullName = "Ada O.", Phone = "+234 803 555 0100" };

        var ex = await Assert.ThrowsAsync<AppException>(() => Customers(s.BusinessId).CreateAsync(same));
        Assert.Equal("possible_duplicate", ex.Code);
        Assert.NotNull(ex.Extensions?["candidates"]);

        var allowed = await Customers(s.BusinessId).CreateAsync(same with { AllowDuplicate = true });
        Assert.NotEqual(s.CustomerId, allowed.Id);
    }

    [Fact]
    public async Task Merging_moves_orders_and_measurement_profiles_to_the_surviving_customer()
    {
        var s = await SeedAsync();
        var (order, _) = await Orders(s.BusinessId).CreateAsync(NewOrder(s, s.SetId), null);
        var target = await Customers(s.BusinessId).CreateAsync(new UpsertCustomerRequest { FullName = "Ada Obi (main)" });

        var merged = await Customers(s.BusinessId).MergeAsync(s.CustomerId, target.Id);
        Assert.Equal(target.Id, merged.Id);

        Assert.Equal(target.Id, (await Orders(s.BusinessId).GetAsync(order.Id)).CustomerId);
        Assert.Single(await Measures(s.BusinessId).ListSetsAsync(target.Id));
        var source = await Customers(s.BusinessId).GetAsync(s.CustomerId);
        Assert.Equal("Merged", source.Status);
        Assert.Equal(target.Id, source.MergedIntoCustomerId);
    }
}
