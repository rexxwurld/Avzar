using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Common;
using Tailoring.Application.Finance;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Finance;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Tailoring.Application.Abstractions;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Pure money rules. No database.</summary>
public sealed class InvoiceMathTests
{
    [Fact]
    public void A_line_applies_discount_before_tax()
    {
        // 3 x 1000 = 3000, minus 500 = 2500 net; 7.5% tax = 187.50
        var a = InvoiceMath.Line(3, 1000m, 500m, 7.5m);
        Assert.Equal((3000m, 2500m, 187.50m, 2687.50m), (a.Gross, a.Net, a.Tax, a.Total));
    }

    [Theory]
    [InlineData("", 1, 10, 0, 0)]          // no description
    [InlineData("x", 0, 10, 0, 0)]         // zero quantity
    [InlineData("x", 1.234, 10, 0, 0)]     // 3 decimals in quantity
    [InlineData("x", 1, -1, 0, 0)]         // negative price
    [InlineData("x", 1, 10, -1, 0)]        // negative discount
    [InlineData("x", 1, 10, 11, 0)]        // discount bigger than the line
    [InlineData("x", 1, 10, 0, 101)]       // tax over 100%
    [InlineData("x", 1, 10.005, 0, 0)]     // 3 decimals in price
    public void Bad_lines_are_reported(string description, double qty, double price, double discount, double tax)
        => Assert.NotNull(InvoiceMath.ValidateLine(description, (decimal)qty, (decimal)price, (decimal)discount, (decimal)tax));

    [Fact]
    public void A_good_line_is_accepted() => Assert.Null(InvoiceMath.ValidateLine("Hem trousers", 2, 1500m, 100m, 7.5m));

    [Fact]
    public void Discount_allocation_adds_up_to_exactly_the_discount()
    {
        Assert.Equal(new[] { 3.34m, 3.33m, 3.33m }, InvoiceMath.AllocateDiscount([100m, 100m, 100m], 10m));
        Assert.Equal(new[] { 0m, 0m }, InvoiceMath.AllocateDiscount([50m, 70m], 0m));
        Assert.Equal(new[] { 50m, 70m }, InvoiceMath.AllocateDiscount([50m, 70m], 120m)); // 100% discount
    }

    [Fact]
    public void Discount_allocation_never_loses_or_invents_a_cent()
    {
        var rnd = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var grosses = Enumerable.Range(0, rnd.Next(1, 8)).Select(_ => (decimal)rnd.Next(1, 5_000_000) / 100m).ToList();
            var discount = Math.Round(grosses.Sum() * (decimal)rnd.NextDouble(), 2);
            var shares = InvoiceMath.AllocateDiscount(grosses, discount);
            Assert.Equal(discount, shares.Sum());
            for (var j = 0; j < shares.Length; j++) { Assert.True(shares[j] >= 0); Assert.True(shares[j] <= grosses[j], $"#{i} share {j}"); }
        }
    }

    [Theory]
    [InlineData(1000, 0, 0, "Unpaid")]
    [InlineData(1000, 400, 0, "PartPaid")]
    [InlineData(1000, 1000, 0, "Paid")]
    [InlineData(1000, 600, 400, "Paid")]      // part paid, rest credited
    [InlineData(1000, 0, 1000, "Credited")]   // never paid, fully credited
    [InlineData(1000, 0, 300, "Unpaid")]
    public void Payment_status_is_derived_never_set(double total, double paid, double credited, string expected)
        => Assert.Equal(expected, InvoiceMath.PaymentStatus((decimal)total, (decimal)paid, (decimal)credited));
}

public sealed class FinanceTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Staff = Guid.NewGuid();
    private sealed record Seed(Guid BusinessId, Guid LocationId, Guid CustomerId, Guid OrderId, string OrderNumber);

    /// <summary>Order: 2 x 10,000 + 1 x 5,000 = 25,000 less a 2,500 discount = 22,500 (net of tax).</summary>
    private async Task<Seed> SeedAsync(string plan = "professional", OrderStatus status = OrderStatus.Confirmed)
    {
        await using var db = fx.Create(null);
        var p = await db.Plans.FirstAsync(x => x.Code == plan);
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = p.Id });
        var loc = new BusinessLocation { BusinessId = b.Id, Name = "Main" };
        var customer = new Customer { BusinessId = b.Id, FullName = "Ada Obi" };
        db.Locations.Add(loc); db.Customers.Add(customer);
        var order = new Order
        {
            BusinessId = b.Id, LocationId = loc.Id, CustomerId = customer.Id, OrderNumber = "ORD-T-" + Guid.NewGuid().ToString("N")[..6],
            Status = status, PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), CurrencyCode = "NGN",
            Subtotal = 25000m, Discount = 2500m, Total = 22500m,
        };
        order.Garments.Add(new OrderGarment { BusinessId = b.Id, OrderId = order.Id, LineNumber = 1, Name = "Agbada", Quantity = 2, UnitPrice = 10000m, LineTotal = 20000m });
        order.Garments.Add(new OrderGarment { BusinessId = b.Id, OrderId = order.Id, LineNumber = 2, Name = "Cap", Quantity = 1, UnitPrice = 5000m, LineTotal = 5000m });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return new Seed(b.Id, loc.Id, customer.Id, order.Id, order.OrderNumber);
    }

    private InvoiceService Invoices(Guid bid) { var db = fx.Create(bid); return new InvoiceService(db, new FakeUserTenant(bid, Staff), new AuditLogger(db), new EntitlementService(db)); }

    private PaymentService Payments(Guid bid, IPermissionService? perms = null)
    { var db = fx.Create(bid); return new PaymentService(db, new FakeUserTenant(bid, Staff), new AuditLogger(db), new EntitlementService(db), perms ?? new AllowAllPermissions()); }

    private OrderService Orders(Guid bid)
    {
        var db = fx.Create(bid); var t = new FakeUserTenant(bid, Staff);
        return new OrderService(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new EntitlementService(db),
            new PricingService(db, t, new AuditLogger(db)), new AllowAllPermissions());
    }

    private static CreateInvoiceRequest Free(Seed s, decimal price = 1000m, decimal? tax = null) => new()
    { CustomerId = s.CustomerId, TaxPercent = tax, Lines = [new InvoiceLineInput { Description = "Alteration", Quantity = 1, UnitPrice = price }] };

    private async Task<InvoiceDto> IssuedAsync(Seed s, decimal price = 1000m, decimal? tax = null)
    {
        var (draft, _) = await Invoices(s.BusinessId).CreateAsync(Free(s, price, tax), null);
        return await Invoices(s.BusinessId).IssueAsync(draft.Id);
    }

    private static RecordPaymentRequest Pay(decimal amount, bool verifyNow = false, string method = "Cash") =>
        new() { Amount = amount, Method = method, VerifyNow = verifyNow };

    private static async Task<string> CodeOf(Func<Task> action) => (await Assert.ThrowsAsync<AppException>(action)).Code;

    // ------------------------------------------------------------------ invoices

    [Fact]
    public async Task Totals_are_computed_by_the_server_and_numbers_are_minted_only_at_issue()
    {
        var s = await SeedAsync();
        var (draft, created) = await Invoices(s.BusinessId).CreateAsync(new CreateInvoiceRequest
        {
            CustomerId = s.CustomerId, TaxPercent = 7.5m,
            Lines = [new InvoiceLineInput { Description = "Suit", Quantity = 2, UnitPrice = 1000m, Discount = 200m }]
        }, null);
        Assert.True(created);
        Assert.Null(draft.Number);
        Assert.Equal((1800m, 135m, 1935m), (draft.Subtotal, draft.TaxTotal, draft.Total)); // (2000-200)=1800, +7.5%
        Assert.Equal("Draft", draft.Status);
        Assert.Null(draft.PaymentStatus);

        var issued = await Invoices(s.BusinessId).IssueAsync(draft.Id);
        Assert.Matches(@"^INV-\d{4}-00001$", issued.Number);
        Assert.Equal(issued.IssueDate!.Value.AddDays(InvoiceService.DefaultDueDays), issued.DueDate);
        Assert.Equal(("Unpaid", 1935m), (issued.PaymentStatus, issued.Balance));

        var second = await IssuedAsync(s);
        Assert.Matches(@"^INV-\d{4}-00002$", second.Number);

        var other = await SeedAsync();
        Assert.Matches(@"-00001$", (await IssuedAsync(other)).Number); // counters are per business
    }

    [Fact]
    public async Task The_default_tax_comes_from_the_pricing_profile()
    {
        var s = await SeedAsync();
        await using (var db = fx.Create(s.BusinessId))
        {
            db.PricingProfiles.Add(new Tailoring.Domain.Pricing.PricingProfile { BusinessId = s.BusinessId, TaxPercent = 7.5m, OverheadPercent = 10, TargetPercent = 40 });
            await db.SaveChangesAsync();
        }
        var (invoice, _) = await Invoices(s.BusinessId).CreateAsync(Free(s, 1000m), null);
        Assert.Equal(75m, invoice.TaxTotal);
    }

    [Fact]
    public async Task A_repeated_idempotency_key_returns_the_first_invoice()
    {
        var s = await SeedAsync();
        var (a, createdA) = await Invoices(s.BusinessId).CreateAsync(Free(s), "key-123");
        var (b, createdB) = await Invoices(s.BusinessId).CreateAsync(Free(s), "key-123");
        Assert.True(createdA); Assert.False(createdB);
        Assert.Equal(a.Id, b.Id);
        Assert.Single((await Invoices(s.BusinessId).ListAsync(new InvoiceListQuery())).Items);
    }

    [Fact]
    public async Task Bad_lines_and_unknown_customers_are_rejected()
    {
        var s = await SeedAsync();
        Assert.Equal("invalid_invoice_line", await CodeOf(() => Invoices(s.BusinessId).CreateAsync(new CreateInvoiceRequest
        { CustomerId = s.CustomerId, Lines = [new InvoiceLineInput { Description = "x", Quantity = 1, UnitPrice = 10m, Discount = 50m }] }, null)));
        Assert.Equal("unknown_customer", await CodeOf(() => Invoices(s.BusinessId).CreateAsync(Free(s) with { CustomerId = Guid.NewGuid() }, null)));
        var zero = await Invoices(s.BusinessId).CreateAsync(Free(s, 0m), null);
        Assert.Equal("nothing_to_issue", await CodeOf(() => Invoices(s.BusinessId).IssueAsync(zero.Invoice.Id)));
    }

    [Fact]
    public async Task Billing_a_whole_order_spreads_the_discount_and_matches_the_order_total()
    {
        var s = await SeedAsync();
        var (inv, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null);

        Assert.Equal(22500m, inv.Subtotal); // equals Order.Total exactly
        Assert.Equal(s.OrderNumber, inv.OrderNumber);
        Assert.Equal(2, inv.Lines.Count);
        Assert.Equal(25000m - 2500m, inv.Lines.Sum(l => l.NetAmount));
        Assert.Equal((2000m, 250m), (inv.Lines[0].Discount, inv.Lines[1].Discount)); // 2500 split 20000:5000

        Assert.Equal("already_invoiced", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null)));
    }

    [Fact]
    public async Task Only_confirmed_orders_can_be_billed()
    {
        foreach (var status in new[] { OrderStatus.Draft, OrderStatus.Quoted, OrderStatus.Cancelled })
        {
            var s = await SeedAsync(status: status);
            Assert.Equal("order_not_billable", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest(), null)));
        }
        Assert.Equal("order_not_found", await CodeOf(async () =>
        { var s = await SeedAsync(); await Invoices(s.BusinessId).CreateFromOrderAsync(Guid.NewGuid(), new CreateInvoiceFromOrderRequest(), null); }));
    }

    [Fact]
    public async Task Deposit_then_balance_add_up_to_the_order_and_never_more()
    {
        var s = await SeedAsync();
        var (deposit, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Deposit", DepositPercent = 30m }, null);
        Assert.Equal(6750m, deposit.Subtotal);
        Assert.Equal("Deposit", deposit.Kind);

        var (balance, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Balance" }, null);
        Assert.Equal(15750m, balance.Subtotal);
        Assert.Equal(22500m, deposit.Subtotal + balance.Subtotal);

        Assert.Equal("nothing_to_invoice", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Balance" }, null)));
        Assert.Equal("nothing_to_invoice", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Deposit", DepositAmount = 100m }, null)));
    }

    [Fact]
    public async Task Deposit_requests_are_validated()
    {
        var s = await SeedAsync();
        Assert.Equal("deposit_amount_required", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Deposit" }, null)));
        Assert.Equal("deposit_amount_required", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId,
            new CreateInvoiceFromOrderRequest { Mode = "Deposit", DepositAmount = 100m, DepositPercent = 10m }, null)));
        Assert.Equal("deposit_out_of_range", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId,
            new CreateInvoiceFromOrderRequest { Mode = "Deposit", DepositAmount = 99999m }, null)));
        Assert.Equal("nothing_billed_yet", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Balance" }, null)));
        Assert.Equal("invalid_mode", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Half" }, null)));
    }

    [Fact]
    public async Task Deposits_need_a_plan_that_includes_them()
    {
        var s = await SeedAsync("free");
        Assert.Equal("feature_not_in_plan", await CodeOf(() => Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId,
            new CreateInvoiceFromOrderRequest { Mode = "Deposit", DepositPercent = 20m }, null)));
        var (full, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null); // full invoices are fine on Free
        Assert.Equal(22500m, full.Subtotal);
    }

    [Fact]
    public async Task Concurrent_requests_cannot_bill_the_same_order_twice()
    {
        var s = await SeedAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try { await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null); return true; }
            catch (AppException ex) when (ex.Code == "already_invoiced") { return false; }
        }));
        Assert.Equal(1, results.Count(r => r));
        var total = (await Invoices(s.BusinessId).ListAsync(new InvoiceListQuery { OrderId = s.OrderId })).Items.Sum(i => i.Total);
        Assert.Equal(22500m, total);
    }

    [Fact]
    public async Task Draft_rules_order_lines_are_locked_free_form_lines_are_replaced_and_issued_is_frozen()
    {
        var s = await SeedAsync();
        var (orderDraft, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null);
        Assert.Equal("order_invoice_lines_locked", await CodeOf(() => Invoices(s.BusinessId).UpdateDraftAsync(orderDraft.Id, new UpdateInvoiceRequest
        { Version = orderDraft.Version, Lines = [new InvoiceLineInput { Description = "x", UnitPrice = 1m }] })));
        var noted = await Invoices(s.BusinessId).UpdateDraftAsync(orderDraft.Id, new UpdateInvoiceRequest { Version = orderDraft.Version, Notes = "Thank you" });
        Assert.Equal("Thank you", noted.Notes);

        var (free, _) = await Invoices(s.BusinessId).CreateAsync(Free(s, 1000m), null);
        var edited = await Invoices(s.BusinessId).UpdateDraftAsync(free.Id, new UpdateInvoiceRequest
        { Version = free.Version, Lines = [new InvoiceLineInput { Description = "Hem", Quantity = 2, UnitPrice = 300m }, new InvoiceLineInput { Description = "Zip", UnitPrice = 150m }] });
        Assert.Equal((750m, 2), (edited.Total, edited.Lines.Count));
        Assert.Equal("stale_version", await CodeOf(() => Invoices(s.BusinessId).UpdateDraftAsync(free.Id, new UpdateInvoiceRequest { Version = free.Version, Notes = "late" })));

        var issued = await Invoices(s.BusinessId).IssueAsync(free.Id);
        Assert.Equal("invoice_not_draft", await CodeOf(() => Invoices(s.BusinessId).UpdateDraftAsync(issued.Id, new UpdateInvoiceRequest { Version = issued.Version })));
        Assert.Equal("invoice_not_draft", await CodeOf(() => Invoices(s.BusinessId).DeleteDraftAsync(issued.Id)));
        Assert.Equal("invoice_not_draft", await CodeOf(() => Invoices(s.BusinessId).IssueAsync(issued.Id)));
    }

    [Fact]
    public async Task Deleting_a_draft_frees_the_order_to_be_billed_again()
    {
        var s = await SeedAsync();
        var (draft, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null);
        await Invoices(s.BusinessId).DeleteDraftAsync(draft.Id);
        var (again, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null);
        Assert.Equal(22500m, again.Subtotal);
    }

    // ------------------------------------------------------------------ payments

    [Fact]
    public async Task A_recorded_payment_does_not_count_until_it_is_verified()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);

        var (pending, created) = await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(1000m, method: "BankTransfer"), null);
        Assert.True(created);
        Assert.Equal("Pending", pending.Status);
        Assert.Null(pending.ReceiptId);

        var before = await Invoices(s.BusinessId).GetAsync(invoice.Id);
        Assert.Equal((0m, 1000m, 1000m, "Unpaid"), (before.AmountPaid, before.AmountPending, before.Balance, before.PaymentStatus));
        Assert.Equal("payment_not_found", await CodeOf(() => Payments(s.BusinessId).GetReceiptForPaymentAsync(Guid.NewGuid())));
        Assert.Equal("receipt_not_found", await CodeOf(() => Payments(s.BusinessId).GetReceiptForPaymentAsync(pending.Id))); // no receipt for a claim

        var verified = await Payments(s.BusinessId).VerifyAsync(pending.Id);
        Assert.Equal("Verified", verified.Status);
        Assert.Equal(Staff, verified.VerifiedByUserId);
        Assert.Matches(@"^RCT-\d{4}-00001$", verified.ReceiptNumber);

        var after = await Invoices(s.BusinessId).GetAsync(invoice.Id);
        Assert.Equal((1000m, 0m, 0m, "Paid"), (after.AmountPaid, after.AmountPending, after.Balance, after.PaymentStatus));

        var receipt = await Payments(s.BusinessId).GetReceiptAsync(verified.ReceiptId!.Value);
        Assert.Equal((1000m, 0m, false, "Ada Obi"), (receipt.Amount, receipt.BalanceAfter, receipt.IsVoid, receipt.CustomerName));
    }

    [Fact]
    public async Task Partial_payments_accumulate_and_the_receipt_records_the_balance_after_each()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        var first = await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(400m, verifyNow: true), null);
        Assert.Equal("Verified", first.Payment.Status);
        Assert.Equal("PartPaid", (await Invoices(s.BusinessId).GetAsync(invoice.Id)).PaymentStatus);

        var second = await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(600m, verifyNow: true), null);
        var r1 = await Payments(s.BusinessId).GetReceiptAsync(first.Payment.ReceiptId!.Value);
        var r2 = await Payments(s.BusinessId).GetReceiptAsync(second.Payment.ReceiptId!.Value);
        Assert.Equal((600m, 0m), (r1.BalanceAfter, r2.BalanceAfter));
        Assert.Equal("Paid", (await Invoices(s.BusinessId).GetAsync(invoice.Id)).PaymentStatus);
    }

    [Fact]
    public async Task Overpayment_is_refused_when_recording_and_when_verifying()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        Assert.Equal("overpayment", await CodeOf(() => Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(1000.01m), null)));

        // two pending claims of 700 are each fine alone, but the second cannot be verified once the first counts
        var a = (await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(700m), null)).Payment;
        var b = (await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(700m), null)).Payment;
        await Payments(s.BusinessId).VerifyAsync(a.Id);
        Assert.Equal("overpayment", await CodeOf(() => Payments(s.BusinessId).VerifyAsync(b.Id)));
        Assert.Equal(700m, (await Invoices(s.BusinessId).GetAsync(invoice.Id)).AmountPaid);
    }

    [Fact]
    public async Task Verify_now_only_works_for_someone_allowed_to_verify()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        var recorder = new FixedPermissions(Permissions.PaymentsRecord);
        var (claim, _) = await Payments(s.BusinessId, recorder).RecordAsync(invoice.Id, Pay(1000m, verifyNow: true), null);
        Assert.Equal("Pending", claim.Status); // asked to verify, but not allowed: it stays a claim

        var (confirmed, _) = await Payments(s.BusinessId, new FixedPermissions(Permissions.PaymentsRecord, Permissions.PaymentsVerify))
            .RecordAsync(invoice.Id, Pay(1000m, verifyNow: true), "k2");
        Assert.Equal("Verified", confirmed.Status);
    }

    [Fact]
    public async Task A_repeated_idempotency_key_never_records_the_money_twice()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        var (a, createdA) = await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(400m, verifyNow: true), "pay-1");
        var (b, createdB) = await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(400m, verifyNow: true), "pay-1");
        Assert.True(createdA); Assert.False(createdB);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(400m, (await Invoices(s.BusinessId).GetAsync(invoice.Id)).AmountPaid);

        Assert.Equal("idempotency_key_reused", await CodeOf(() => Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(500m), "pay-1")));
    }

    [Fact]
    public async Task Concurrent_full_payments_leave_exactly_one_that_counts()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            try { await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(1000m, verifyNow: true), "race-" + i); return true; }
            catch (AppException ex) when (ex.Code == "overpayment") { return false; }
        }));
        Assert.Equal(1, results.Count(r => r));
        var final = await Invoices(s.BusinessId).GetAsync(invoice.Id);
        Assert.Equal((1000m, 0m), (final.AmountPaid, final.Balance));
        Assert.Single(final.Payments.Where(p => p.Status == "Verified"));
    }

    [Fact]
    public async Task Concurrent_verifies_of_one_payment_issue_one_receipt()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        var pending = (await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(1000m), null)).Payment;
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try { await Payments(s.BusinessId).VerifyAsync(pending.Id); return true; }
            catch (AppException ex) when (ex.Code is "invalid_transition" or "overpayment") { return false; }
        }));
        Assert.Equal(1, results.Count(r => r));
        await using var db = fx.Create(s.BusinessId);
        Assert.Equal(1, await db.Receipts.CountAsync(r => r.PaymentId == pending.Id));
    }

    [Fact]
    public async Task Rejecting_needs_a_pending_payment_and_leaves_the_balance_alone()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        var pending = (await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(1000m, method: "BankTransfer"), null)).Payment;

        var rejected = await Payments(s.BusinessId).RejectAsync(pending.Id, new PaymentReasonRequest { Reason = "Transfer never arrived" });
        Assert.Equal(("Rejected", "Transfer never arrived"), (rejected.Status, rejected.StatusReason));
        Assert.Equal(1000m, (await Invoices(s.BusinessId).GetAsync(invoice.Id)).Balance);
        Assert.Equal("invalid_transition", await CodeOf(() => Payments(s.BusinessId).VerifyAsync(pending.Id)));
        Assert.Equal("invalid_transition", await CodeOf(() => Payments(s.BusinessId).RejectAsync(pending.Id, new PaymentReasonRequest { Reason = "again" })));
    }

    [Fact]
    public async Task Reversing_a_verified_payment_restores_the_balance_and_voids_but_keeps_the_receipt()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        var (paid, _) = await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(1000m, verifyNow: true), null);

        var reversed = await Payments(s.BusinessId).ReverseAsync(paid.Id, new PaymentReasonRequest { Reason = "Refunded to customer" });
        Assert.Equal("Reversed", reversed.Status);
        var after = await Invoices(s.BusinessId).GetAsync(invoice.Id);
        Assert.Equal((0m, 1000m, "Unpaid"), (after.AmountPaid, after.Balance, after.PaymentStatus));

        var receipt = await Payments(s.BusinessId).GetReceiptAsync(paid.ReceiptId!.Value);
        Assert.True(receipt.IsVoid);
        Assert.Equal("invalid_transition", await CodeOf(() => Payments(s.BusinessId).ReverseAsync(paid.Id, new PaymentReasonRequest { Reason = "again" })));
        Assert.Equal("invalid_transition", await CodeOf(() => Payments(s.BusinessId).VerifyAsync(paid.Id)));
    }

    [Fact]
    public async Task Payments_need_an_issued_invoice_and_a_sane_date()
    {
        var s = await SeedAsync();
        var (draft, _) = await Invoices(s.BusinessId).CreateAsync(Free(s), null);
        Assert.Equal("invoice_not_payable", await CodeOf(() => Payments(s.BusinessId).RecordAsync(draft.Id, Pay(100m), null)));

        var issued = await Invoices(s.BusinessId).IssueAsync(draft.Id);
        Assert.Equal("received_in_future", await CodeOf(() => Payments(s.BusinessId).RecordAsync(issued.Id, Pay(100m) with { ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)) }, null)));
        Assert.Equal("invalid_method", await CodeOf(() => Payments(s.BusinessId).RecordAsync(issued.Id, Pay(100m, method: "Bitcoin"), null)));
        Assert.Equal("invalid_amount", await CodeOf(() => Payments(s.BusinessId).RecordAsync(issued.Id, Pay(10.005m), null)));
        Assert.Equal("invoice_not_found", await CodeOf(() => Payments(s.BusinessId).RecordAsync(Guid.NewGuid(), Pay(100m), null)));
    }

    [Fact]
    public async Task On_the_free_plan_a_payment_must_settle_the_invoice_in_full()
    {
        var s = await SeedAsync("free");
        var invoice = await IssuedAsync(s, 1000m);
        Assert.Equal("feature_not_in_plan", await CodeOf(() => Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(500m), null)));
        var (full, _) = await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(1000m, verifyNow: true), null);
        Assert.Equal("Verified", full.Status);
    }

    [Fact]
    public async Task The_database_refuses_a_verified_payment_with_no_verifier()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        await using var db = fx.Create(s.BusinessId);
        db.Payments.Add(new Payment
        {
            BusinessId = s.BusinessId, InvoiceId = invoice.Id, CustomerId = s.CustomerId, Amount = 1000m, CurrencyCode = "NGN",
            Method = PaymentMethod.Cash, ReceivedOn = DateOnly.FromDateTime(DateTime.UtcNow), Status = PaymentStatus.Verified,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ------------------------------------------------------------------ credit notes and voiding

    [Fact]
    public async Task A_credit_note_reduces_the_balance_but_never_below_zero()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(400m, verifyNow: true), null);

        Assert.Equal("credit_exceeds_balance", await CodeOf(() => Invoices(s.BusinessId).AddCreditNoteAsync(invoice.Id, new CreditNoteRequest { Amount = 700m, Reason = "goodwill" })));
        var credited = await Invoices(s.BusinessId).AddCreditNoteAsync(invoice.Id, new CreditNoteRequest { Amount = 600m, Reason = "Fabric flaw" });
        Assert.Equal((600m, 0m, "Paid"), (credited.AmountCredited, credited.Balance, credited.PaymentStatus));
        Assert.Matches(@"^CN-\d{4}-00001$", credited.CreditNotes.Single().Number);
        Assert.Equal("overpayment", await CodeOf(() => Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(1m), null))); // nothing left to pay
    }

    [Fact]
    public async Task A_fully_credited_unpaid_invoice_reads_as_credited_and_cannot_then_be_voided()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        var credited = await Invoices(s.BusinessId).AddCreditNoteAsync(invoice.Id, new CreditNoteRequest { Amount = 1000m, Reason = "Order cancelled by customer" });
        Assert.Equal("Credited", credited.PaymentStatus);
        Assert.Equal("invoice_has_credit_notes", await CodeOf(() => Invoices(s.BusinessId).VoidAsync(invoice.Id, new VoidInvoiceRequest { Reason = "mistake" })));
    }

    [Fact]
    public async Task Credit_notes_need_the_plan_feature_and_an_issued_invoice()
    {
        var free = await SeedAsync("free");
        var freeInvoice = await IssuedAsync(free, 500m);
        Assert.Equal("feature_not_in_plan", await CodeOf(() => Invoices(free.BusinessId).AddCreditNoteAsync(freeInvoice.Id, new CreditNoteRequest { Amount = 100m, Reason = "goodwill" })));

        var s = await SeedAsync();
        var (draft, _) = await Invoices(s.BusinessId).CreateAsync(Free(s), null);
        Assert.Equal("invoice_not_issued", await CodeOf(() => Invoices(s.BusinessId).AddCreditNoteAsync(draft.Id, new CreditNoteRequest { Amount = 100m, Reason = "goodwill" })));
    }

    [Fact]
    public async Task Voiding_is_blocked_by_live_payments_and_frees_the_order_amount_once_allowed()
    {
        var s = await SeedAsync();
        var (draft, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null);
        var invoice = await Invoices(s.BusinessId).IssueAsync(draft.Id);
        var pending = (await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(100m), null)).Payment;

        Assert.Equal("invoice_has_payments", await CodeOf(() => Invoices(s.BusinessId).VoidAsync(invoice.Id, new VoidInvoiceRequest { Reason = "wrong customer" })));
        await Payments(s.BusinessId).RejectAsync(pending.Id, new PaymentReasonRequest { Reason = "never received" });

        var voided = await Invoices(s.BusinessId).VoidAsync(invoice.Id, new VoidInvoiceRequest { Reason = "wrong customer" });
        Assert.Equal(("Void", invoice.Number, "wrong customer"), (voided.Status, voided.Number, voided.VoidReason));
        Assert.Null(voided.PaymentStatus);
        Assert.Equal("invoice_not_payable", await CodeOf(() => Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(100m), null)));
        Assert.Equal("invoice_already_void", await CodeOf(() => Invoices(s.BusinessId).VoidAsync(invoice.Id, new VoidInvoiceRequest { Reason = "again" })));

        var (again, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null); // the order can be billed again
        Assert.Equal(22500m, again.Subtotal);
    }

    [Fact]
    public async Task An_order_with_live_invoices_cannot_be_cancelled_until_they_are_void()
    {
        var s = await SeedAsync();
        var (draft, _) = await Invoices(s.BusinessId).CreateFromOrderAsync(s.OrderId, new CreateInvoiceFromOrderRequest { Mode = "Full" }, null);
        var cancel = new CancelOrderRequest { Reason = "Customer changed their mind" };
        Assert.Equal("order_has_invoices", await CodeOf(() => Orders(s.BusinessId).CancelAsync(s.OrderId, cancel)));

        var issued = await Invoices(s.BusinessId).IssueAsync(draft.Id);
        await Invoices(s.BusinessId).VoidAsync(issued.Id, new VoidInvoiceRequest { Reason = "order cancelled" });
        Assert.Equal("Cancelled", (await Orders(s.BusinessId).CancelAsync(s.OrderId, cancel)).Status);
    }

    // ------------------------------------------------------------------ listing, overdue, isolation

    [Fact]
    public async Task Listing_filters_by_status_outstanding_overdue_and_search()
    {
        var s = await SeedAsync();
        var paid = await IssuedAsync(s, 1000m);
        await Payments(s.BusinessId).RecordAsync(paid.Id, Pay(1000m, verifyNow: true), null);
        var open = await IssuedAsync(s, 2000m);
        var late = await IssuedAsync(s, 3000m);
        await Invoices(s.BusinessId).CreateAsync(Free(s), null); // a draft

        await using (var db = fx.Create(s.BusinessId))
        {
            var row = await db.Invoices.SingleAsync(i => i.Id == late.Id);
            row.DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));
            await db.SaveChangesAsync();
        }

        var all = await Invoices(s.BusinessId).ListAsync(new InvoiceListQuery());
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(new[] { open.Id, late.Id }.Order(), (await Invoices(s.BusinessId).ListAsync(new InvoiceListQuery { Outstanding = true })).Items.Select(i => i.Id).Order());
        var overdue = (await Invoices(s.BusinessId).ListAsync(new InvoiceListQuery { Overdue = true })).Items.Single();
        Assert.Equal((late.Id, true), (overdue.Id, overdue.IsOverdue));
        Assert.Equal(paid.Id, (await Invoices(s.BusinessId).ListAsync(new InvoiceListQuery { Status = "Issued", Search = paid.Number })).Items.Single().Id);
        Assert.Equal(4, (await Invoices(s.BusinessId).ListAsync(new InvoiceListQuery { Search = "ada" })).TotalCount);
        Assert.Equal(1, (await Invoices(s.BusinessId).ListAsync(new InvoiceListQuery { Status = "Draft" })).TotalCount);
    }

    [Fact]
    public async Task Another_business_cannot_see_pay_or_touch_anything()
    {
        var a = await SeedAsync(); var b = await SeedAsync();
        var invoice = await IssuedAsync(a, 1000m);
        var (payment, _) = await Payments(a.BusinessId).RecordAsync(invoice.Id, Pay(1000m, verifyNow: true), null);

        Assert.Equal("invoice_not_found", await CodeOf(() => Invoices(b.BusinessId).GetAsync(invoice.Id)));
        Assert.Equal("invoice_not_found", await CodeOf(() => Invoices(b.BusinessId).IssueAsync(invoice.Id)));
        Assert.Equal("invoice_not_found", await CodeOf(() => Invoices(b.BusinessId).VoidAsync(invoice.Id, new VoidInvoiceRequest { Reason = "nope" })));
        Assert.Equal("invoice_not_found", await CodeOf(() => Payments(b.BusinessId).RecordAsync(invoice.Id, Pay(1m), null)));
        Assert.Equal("payment_not_found", await CodeOf(() => Payments(b.BusinessId).GetAsync(payment.Id)));
        Assert.Equal("payment_not_found", await CodeOf(() => Payments(b.BusinessId).ReverseAsync(payment.Id, new PaymentReasonRequest { Reason = "nope" })));
        Assert.Equal("receipt_not_found", await CodeOf(() => Payments(b.BusinessId).GetReceiptAsync(payment.ReceiptId!.Value)));
        Assert.Empty((await Invoices(b.BusinessId).ListAsync(new InvoiceListQuery())).Items);
        Assert.Empty((await Payments(b.BusinessId).ListAsync(new PaymentListQuery())).Items);
        Assert.Equal("order_not_found", await CodeOf(() => Invoices(b.BusinessId).CreateFromOrderAsync(a.OrderId, new CreateInvoiceFromOrderRequest(), null)));
    }

    [Fact]
    public async Task Payment_listing_filters_work()
    {
        var s = await SeedAsync();
        var invoice = await IssuedAsync(s, 1000m);
        await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(300m, verifyNow: true), null);
        var pending = (await Payments(s.BusinessId).RecordAsync(invoice.Id, Pay(200m), null)).Payment;

        Assert.Equal(2, (await Payments(s.BusinessId).ListAsync(new PaymentListQuery { InvoiceId = invoice.Id })).TotalCount);
        Assert.Equal(pending.Id, (await Payments(s.BusinessId).ListAsync(new PaymentListQuery { Status = "Pending" })).Items.Single().Id);
        Assert.Equal(2, (await Payments(s.BusinessId).ListAsync(new PaymentListQuery { CustomerId = s.CustomerId })).TotalCount);
    }
}

public sealed class ExpenseTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Staff = Guid.NewGuid();

    private async Task<Guid> BusinessAsync(string plan = "starter")
    {
        await using var db = fx.Create(null);
        var p = await db.Plans.FirstAsync(x => x.Code == plan);
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = p.Id });
        await db.SaveChangesAsync();
        return b.Id;
    }

    private ExpenseService Svc(Guid bid) { var db = fx.Create(bid); return new ExpenseService(db, new FakeUserTenant(bid, Staff), new AuditLogger(db), new EntitlementService(db)); }
    private static DateOnly Today(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(offset));
    private static async Task<string> CodeOf(Func<Task> action) => (await Assert.ThrowsAsync<AppException>(action)).Code;

    private static UpsertExpenseRequest Req(Guid category, decimal amount = 5000m, int dayOffset = 0, string description = "Ankara fabric") =>
        new() { CategoryId = category, ExpenseDate = Today(dayOffset), Amount = amount, Description = description, Vendor = "Balogun Market" };

    [Fact]
    public async Task Expenses_need_a_plan_that_includes_them()
    {
        var bid = await BusinessAsync("free");
        Assert.Equal("feature_not_in_plan", await CodeOf(() => Svc(bid).ListCategoriesAsync()));
        Assert.Equal("feature_not_in_plan", await CodeOf(() => Svc(bid).SummaryAsync(null, null)));
    }

    [Fact]
    public async Task Default_categories_appear_once_and_can_be_extended_and_deactivated()
    {
        var bid = await BusinessAsync();
        var first = await Svc(bid).ListCategoriesAsync();
        Assert.Equal(ExpenseCategory.Defaults.Count, first.Count);
        Assert.Equal(first.Count, (await Svc(bid).ListCategoriesAsync()).Count); // not duplicated on the second call

        var added = await Svc(bid).CreateCategoryAsync(new UpsertExpenseCategoryRequest { Name = "Packaging" });
        Assert.Equal("category_exists", await CodeOf(() => Svc(bid).CreateCategoryAsync(new UpsertExpenseCategoryRequest { Name = "packaging" })));
        var off = await Svc(bid).UpdateCategoryAsync(added.Id, new UpsertExpenseCategoryRequest { Name = "Packaging", IsActive = false });
        Assert.False(off.IsActive);
        Assert.Equal("category_inactive", await CodeOf(() => Svc(bid).CreateAsync(Req(added.Id))));
    }

    [Fact]
    public async Task Create_update_and_void_keep_a_trail_and_never_delete()
    {
        var bid = await BusinessAsync();
        var cat = (await Svc(bid).ListCategoriesAsync()).First();
        var e = await Svc(bid).CreateAsync(Req(cat.Id, 5000m));
        Assert.Equal(("NGN", cat.Name), (e.CurrencyCode, e.CategoryName));

        Assert.Equal("version_required", await CodeOf(() => Svc(bid).UpdateAsync(e.Id, Req(cat.Id))));
        var updated = await Svc(bid).UpdateAsync(e.Id, Req(cat.Id, 5500m) with { Version = e.Version });
        Assert.Equal(5500m, updated.Amount);
        Assert.Equal("stale_version", await CodeOf(() => Svc(bid).UpdateAsync(e.Id, Req(cat.Id, 9m) with { Version = e.Version })));

        var voided = await Svc(bid).VoidAsync(e.Id, new VoidExpenseRequest { Reason = "Entered twice" });
        Assert.True(voided.IsVoid);
        Assert.Equal("expense_void", await CodeOf(() => Svc(bid).UpdateAsync(e.Id, Req(cat.Id) with { Version = voided.Version })));
        Assert.Equal("expense_void", await CodeOf(() => Svc(bid).VoidAsync(e.Id, new VoidExpenseRequest { Reason = "again" })));
        Assert.Empty((await Svc(bid).ListAsync(new ExpenseListQuery())).Items);
        Assert.Single((await Svc(bid).ListAsync(new ExpenseListQuery { IncludeVoid = true })).Items);
    }

    [Fact]
    public async Task Bad_expenses_are_rejected()
    {
        var bid = await BusinessAsync();
        var cat = (await Svc(bid).ListCategoriesAsync()).First();
        Assert.Equal("expense_in_future", await CodeOf(() => Svc(bid).CreateAsync(Req(cat.Id, dayOffset: 2))));
        Assert.Equal("invalid_amount", await CodeOf(() => Svc(bid).CreateAsync(Req(cat.Id, 10.005m))));
        Assert.Equal("unknown_category", await CodeOf(() => Svc(bid).CreateAsync(Req(Guid.NewGuid()))));
    }

    [Fact]
    public async Task The_summary_totals_by_category_for_a_period_and_ignores_voided_expenses()
    {
        var bid = await BusinessAsync();
        var cats = await Svc(bid).ListCategoriesAsync();
        var (fabric, rent) = (cats[0], cats[2]);
        await Svc(bid).CreateAsync(Req(fabric.Id, 5000m, -1));
        await Svc(bid).CreateAsync(Req(fabric.Id, 2500.50m, -2));
        await Svc(bid).CreateAsync(Req(rent.Id, 40000m, -3));
        await Svc(bid).CreateAsync(Req(fabric.Id, 99999m, -40));                          // outside the period
        var bad = await Svc(bid).CreateAsync(Req(rent.Id, 7777m, -1));
        await Svc(bid).VoidAsync(bad.Id, new VoidExpenseRequest { Reason = "duplicate" });  // voided

        var summary = await Svc(bid).SummaryAsync(Today(-10), Today());
        Assert.Equal(47500.50m, summary.Total);
        Assert.Equal(2, summary.ByCategory.Count);
        Assert.Equal((rent.Name, 40000m), (summary.ByCategory[0].CategoryName, summary.ByCategory[0].Total));
        Assert.Equal((7500.50m, 2), (summary.ByCategory[1].Total, summary.ByCategory[1].Count));
        Assert.Equal("NGN", summary.CurrencyCode);

        Assert.Equal(2, (await Svc(bid).ListAsync(new ExpenseListQuery { CategoryId = fabric.Id, From = Today(-10) })).TotalCount);
        Assert.Empty((await Svc(bid).ListAsync(new ExpenseListQuery { Search = "ankara", CategoryId = rent.Id })).Items);
    }

    [Fact]
    public async Task Another_business_sees_none_of_it()
    {
        var a = await BusinessAsync(); var b = await BusinessAsync();
        var cat = (await Svc(a).ListCategoriesAsync()).First();
        var e = await Svc(a).CreateAsync(Req(cat.Id));

        Assert.Equal("expense_not_found", await CodeOf(() => Svc(b).GetAsync(e.Id)));
        Assert.Equal("expense_not_found", await CodeOf(() => Svc(b).VoidAsync(e.Id, new VoidExpenseRequest { Reason = "nope" })));
        Assert.Equal("unknown_category", await CodeOf(() => Svc(b).CreateAsync(Req(cat.Id)))); // A's category id is invisible to B
        Assert.Empty((await Svc(b).ListAsync(new ExpenseListQuery())).Items);
        Assert.Equal(0m, (await Svc(b).SummaryAsync(null, null)).Total);
    }
}
