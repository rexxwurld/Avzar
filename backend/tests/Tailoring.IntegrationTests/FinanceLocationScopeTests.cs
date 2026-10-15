using Tailoring.Application.Common;
using Tailoring.Application.Finance;
using Tailoring.Application.Locations;
using Tailoring.Application.Orders;
using Tailoring.Domain.Access;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// An invoice follows the location of the order it was billed from; payments, receipts and credit notes follow their invoice.
/// Staff limited to some locations see only those. A hidden record looks exactly like a missing one, including on idempotent replays.
/// Invoices that are not tied to an order are visible to unrestricted callers only (fail closed).
/// </summary>
public sealed class FinanceLocationScopeTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record World(Guid BusinessId, Guid LocA, Guid LocB, Guid CustomerId, Guid OrderA, Guid OrderB,
        Guid InvoiceA, Guid InvoiceB, Guid StandaloneInvoice, Guid PaymentA, Guid PaymentB, Guid ReceiptB);

    private ILocationScope? ScopeFor(AppDbContext db, FakeUserTenant t, Guid? userId)
        => userId is null ? null : new LocationScope(db, t, new PermissionService(db));

    private OrderService Orders(Guid bid)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, null);
        return new OrderService(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new EntitlementService(db),
            new PricingService(db, t, new AuditLogger(db)), new AllowAllPermissions());
    }

    private InvoiceService Invoices(Guid bid, Guid? userId)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, userId);
        return new InvoiceService(db, t, new AuditLogger(db), new EntitlementService(db), ScopeFor(db, t, userId));
    }

    private PaymentService Payments(Guid bid, Guid? userId)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, userId);
        return new PaymentService(db, t, new AuditLogger(db), new EntitlementService(db), new AllowAllPermissions(), ScopeFor(db, t, userId));
    }

    /// <summary>Setup only: a signed-in id is needed for VerifyNow to take effect, and there is deliberately no location scope.</summary>
    private PaymentService SetupPayments(Guid bid)
    {
        var db = fx.Create(bid);
        return new PaymentService(db, new FakeUserTenant(bid, Guid.NewGuid()), new AuditLogger(db), new EntitlementService(db), new AllowAllPermissions());
    }

    private SearchService Search(Guid bid, Guid userId)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, userId);
        return new SearchService(db, t, new PermissionService(db), ScopeFor(db, t, userId));
    }

    private static CreateOrderRequest NewOrder(Guid customerId, Guid locationId) => new()
    {
        CustomerId = customerId, LocationId = locationId,
        PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
        Garments = [new OrderGarmentInput { Name = "Shirt", Quantity = 1, UnitPrice = 100m }]
    };

    private async Task<World> SeedAsync()
    {
        Guid bid, la, lb, cid;
        await using (var db = fx.Create(null))
        {
            var business = new Business { Name = "FinLoc-" + Guid.NewGuid().ToString("N")[..8], CurrencyCode = "USD", TimeZoneId = "UTC" };
            db.Businesses.Add(business);
            var a = new BusinessLocation { BusinessId = business.Id, Name = "Lekki" };
            var b = new BusinessLocation { BusinessId = business.Id, Name = "Ikeja" };
            var customer = new Customer { BusinessId = business.Id, FullName = "Ada Obi" };
            db.Locations.AddRange(a, b);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            (bid, la, lb, cid) = (business.Id, a.Id, b.Id, customer.Id);
        }

        // Everything is created with no signed-in user, so the setup itself is not location-scoped.
        var orders = Orders(bid);
        var (orderA, _) = await orders.CreateAsync(NewOrder(cid, la), null);
        var (orderB, _) = await orders.CreateAsync(NewOrder(cid, lb), null);
        foreach (var id in new[] { orderA.Id, orderB.Id })
        {
            await Orders(bid).QuoteAsync(id); // Draft -> Quoted -> Confirmed
            await Orders(bid).ConfirmAsync(id);
        }

        var full = new CreateInvoiceFromOrderRequest { Mode = "Full" };
        var (draftA, _) = await Invoices(bid, null).CreateFromOrderAsync(orderA.Id, full, null);
        var (draftB, _) = await Invoices(bid, null).CreateFromOrderAsync(orderB.Id, full, "key-b-" + Guid.NewGuid().ToString("N"));
        var invoiceA = await Invoices(bid, null).IssueAsync(draftA.Id);
        var invoiceB = await Invoices(bid, null).IssueAsync(draftB.Id);

        var (standalone, _) = await Invoices(bid, null).CreateAsync(new CreateInvoiceRequest
        {
            CustomerId = cid, Lines = [new InvoiceLineInput { Description = "Walk-in alteration", Quantity = 1, UnitPrice = 20m }]
        }, null);

        var (payA, _) = await SetupPayments(bid).RecordAsync(invoiceA.Id, new RecordPaymentRequest { Amount = 100m, Method = "Cash" }, null);
        var (payB, _) = await SetupPayments(bid).RecordAsync(invoiceB.Id, new RecordPaymentRequest { Amount = 100m, Method = "Cash", VerifyNow = true }, null);
        return new World(bid, la, lb, cid, orderA.Id, orderB.Id, invoiceA.Id, invoiceB.Id, standalone.Id, payA.Id, payB.Id, payB.ReceiptId!.Value);
    }

    private async Task<Guid> StaffAsync(Guid businessId, string[] permissions, Guid[] locations, bool owner = false)
    {
        await using var db = fx.Create(null);
        var email = $"staff-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, FullName = "Staff" };
        db.Users.Add(user);

        var role = new BusinessRole { BusinessId = businessId, Name = "R-" + Guid.NewGuid().ToString("N")[..8] };
        role.Permissions = permissions.Select(p => new BusinessRolePermission { RoleId = role.Id, BusinessId = businessId, Permission = p }).ToList();
        db.BusinessRoles.Add(role);

        var membership = new BusinessMembership
        {
            BusinessId = businessId, UserId = user.Id, DisplayName = "Staff", IsOwner = owner, Status = MembershipStatus.Active
        };
        db.Memberships.Add(membership);
        db.MembershipRoles.Add(new MembershipRole { MembershipId = membership.Id, RoleId = role.Id, BusinessId = businessId });
        foreach (var loc in locations)
            db.MembershipLocations.Add(new MembershipLocation { MembershipId = membership.Id, LocationId = loc, BusinessId = businessId });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static readonly string[] Finance =
        [Permissions.InvoicesView, Permissions.InvoicesManage, Permissions.PaymentsView, Permissions.PaymentsRecord,
         Permissions.PaymentsVerify, Permissions.OrdersView, Permissions.CustomersView];

    private static async Task<AppException> Fails(Func<Task> action) => await Assert.ThrowsAsync<AppException>(action);

    [Fact]
    public async Task Staff_see_only_invoices_billed_from_orders_at_their_location()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, Finance, [w.LocA]);

        var list = await Invoices(w.BusinessId, user).ListAsync(new InvoiceListQuery());
        Assert.Equal(w.InvoiceA, Assert.Single(list.Items).Id); // not B, and not the standalone invoice
        Assert.Equal(w.InvoiceA, (await Invoices(w.BusinessId, user).GetAsync(w.InvoiceA)).Id);

        var hidden = await Fails(() => Invoices(w.BusinessId, user).GetAsync(w.InvoiceB));
        Assert.Equal(404, hidden.StatusCode);
        Assert.Equal("invoice_not_found", hidden.Code);
    }

    [Fact]
    public async Task Invoices_that_belong_to_no_order_are_hidden_and_cannot_be_created_by_scoped_staff()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, Finance, [w.LocA]);

        Assert.Equal("invoice_not_found", (await Fails(() => Invoices(w.BusinessId, user).GetAsync(w.StandaloneInvoice))).Code);

        var create = await Fails(() => Invoices(w.BusinessId, user).CreateAsync(new CreateInvoiceRequest
        {
            CustomerId = w.CustomerId, Lines = [new InvoiceLineInput { Description = "x", Quantity = 1, UnitPrice = 5m }]
        }, null));
        Assert.Equal(403, create.StatusCode);
        Assert.Equal("location_forbidden", create.Code);
    }

    [Fact]
    public async Task Hidden_invoices_cannot_be_voided_and_hidden_orders_cannot_be_billed()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, Finance, [w.LocA]);

        var voided = await Fails(() => Invoices(w.BusinessId, user).VoidAsync(w.InvoiceB, new VoidInvoiceRequest { Reason = "not allowed" }));
        Assert.Equal("invoice_not_found", voided.Code);

        var bill = await Fails(() => Invoices(w.BusinessId, user)
            .CreateFromOrderAsync(w.OrderB, new CreateInvoiceFromOrderRequest { Mode = "Balance" }, null));
        Assert.Equal("order_not_found", bill.Code);
    }

    [Fact]
    public async Task Replaying_an_idempotency_key_does_not_reveal_an_invoice_at_a_hidden_location()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, Finance, [w.LocA]);

        string key;
        await using (var db = fx.Create(w.BusinessId))
            key = db.Invoices.Where(i => i.Id == w.InvoiceB).Select(i => i.IdempotencyKey).First()!;

        var replay = await Fails(() => Invoices(w.BusinessId, user)
            .CreateFromOrderAsync(w.OrderB, new CreateInvoiceFromOrderRequest { Mode = "Full" }, key));
        Assert.Equal("invoice_not_found", replay.Code);
    }

    [Fact]
    public async Task Payments_follow_their_invoice_and_a_hidden_payment_looks_missing()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, Finance, [w.LocA]);

        var list = await Payments(w.BusinessId, user).ListAsync(new PaymentListQuery());
        Assert.Equal(w.PaymentA, Assert.Single(list.Items).Id);
        Assert.Equal(w.PaymentA, (await Payments(w.BusinessId, user).GetAsync(w.PaymentA)).Id);

        Assert.Equal("payment_not_found", (await Fails(() => Payments(w.BusinessId, user).GetAsync(w.PaymentB))).Code);
        Assert.Equal("payment_not_found", (await Fails(() => Payments(w.BusinessId, user).VerifyAsync(w.PaymentB))).Code);
        Assert.Equal("payment_not_found", (await Fails(() => Payments(w.BusinessId, user)
            .RejectAsync(w.PaymentB, new PaymentReasonRequest { Reason = "not allowed" }))).Code);

        var record = await Fails(() => Payments(w.BusinessId, user)
            .RecordAsync(w.InvoiceB, new RecordPaymentRequest { Amount = 1m, Method = "Cash" }, null));
        Assert.Equal("invoice_not_found", record.Code);
    }

    [Fact]
    public async Task Receipts_follow_their_invoice()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, Finance, [w.LocA]);

        Assert.Equal("receipt_not_found", (await Fails(() => Payments(w.BusinessId, user).GetReceiptAsync(w.ReceiptB))).Code);
        Assert.Equal("payment_not_found", (await Fails(() => Payments(w.BusinessId, user).GetReceiptForPaymentAsync(w.PaymentB))).Code);

        // The same receipt is fine for someone who may see location B.
        var other = await StaffAsync(w.BusinessId, Finance, [w.LocB]);
        Assert.Equal(w.ReceiptB, (await Payments(w.BusinessId, other).GetReceiptAsync(w.ReceiptB)).Id);
    }

    [Fact]
    public async Task Search_returns_only_invoices_the_caller_may_see()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, Finance, [w.LocA]);

        var scoped = await Search(w.BusinessId, user).SearchAsync("Ada");
        Assert.Equal(w.InvoiceA, Assert.Single(scoped.Invoices).Id);
    }

    [Fact]
    public async Task Owners_and_holders_of_locations_all_see_every_invoice_and_payment()
    {
        var w = await SeedAsync();
        var owner = await StaffAsync(w.BusinessId, [], [], owner: true);
        var everywhere = await StaffAsync(w.BusinessId, [.. Finance, Permissions.LocationsAll], []);

        foreach (var user in new[] { owner, everywhere })
        {
            Assert.Equal(3, (await Invoices(w.BusinessId, user).ListAsync(new InvoiceListQuery())).TotalCount);
            Assert.Equal(2, (await Payments(w.BusinessId, user).ListAsync(new PaymentListQuery())).TotalCount);
            Assert.Equal(w.StandaloneInvoice, (await Invoices(w.BusinessId, user).GetAsync(w.StandaloneInvoice)).Id);
        }
    }

    [Fact]
    public async Task A_member_with_no_locations_sees_no_finance_records()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, Finance, []);

        Assert.Equal(0, (await Invoices(w.BusinessId, user).ListAsync(new InvoiceListQuery())).TotalCount);
        Assert.Equal(0, (await Payments(w.BusinessId, user).ListAsync(new PaymentListQuery())).TotalCount);
    }
}
