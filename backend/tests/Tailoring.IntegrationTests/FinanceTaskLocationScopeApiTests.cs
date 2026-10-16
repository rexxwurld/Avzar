using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tailoring.Domain.Access;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// Location scoping for invoices, payments, receipts and production tasks through the real HTTP pipeline (login, permission handler,
/// DI wiring), complementing the service-level <c>FinanceLocationScopeTests</c> and <c>TaskLocationScopeTests</c>. A member limited to
/// one location must see only that location's records; a hidden record looks exactly like a missing one (404, same code); writes
/// against a hidden record are 404 too; an invoice that is not tied to an order is visible to unrestricted callers only; the owner
/// still sees everything. PDF endpoints are probed only for hidden records (the 404 comes before rendering, so no PDF licence is needed).
/// </summary>
[Collection("api")]
public sealed class FinanceTaskLocationScopeApiTests(ApiFactory factory)
{
    private sealed record Session(HttpClient Http, string Email, Guid BusinessId, string Csrf);

    private sealed record World(
        Session Owner, Guid LocA, Guid LocB, Guid OrderA, Guid OrderB,
        Guid InvoiceA, Guid InvoiceB, Guid StandaloneInvoice,
        Guid PaymentA, Guid PaymentB, Guid ReceiptB, Guid TaskA, Guid TaskB);

    private static readonly string[] FinanceAccess =
    [
        Permissions.InvoicesView, Permissions.InvoicesManage, Permissions.PaymentsView, Permissions.PaymentsRecord,
        Permissions.PaymentsVerify, Permissions.OrdersView, Permissions.CustomersView,
    ];

    private static readonly string[] TaskAccess = [Permissions.TasksView, Permissions.TasksManage, Permissions.OrdersView, Permissions.CustomersView];

    // ---- invoices ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_restricted_member_lists_only_invoices_at_their_location()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, FinanceAccess, [w.LocA]);

        var res = await SendAsync(member.Http, HttpMethod.Get, "/api/invoices");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains(w.InvoiceA.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.InvoiceB.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.StandaloneInvoice.ToString(), body, StringComparison.OrdinalIgnoreCase); // fail closed: no order, no location
    }

    [Fact]
    public async Task A_hidden_invoice_is_a_404_and_looks_like_a_missing_one()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, FinanceAccess, [w.LocA]);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(member.Http, HttpMethod.Get, $"/api/invoices/{w.InvoiceA}")).StatusCode);

        var hidden = await SendAsync(member.Http, HttpMethod.Get, $"/api/invoices/{w.InvoiceB}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Contains("invoice_not_found", await hidden.Content.ReadAsStringAsync());

        var standalone = await SendAsync(member.Http, HttpMethod.Get, $"/api/invoices/{w.StandaloneInvoice}");
        Assert.Equal(HttpStatusCode.NotFound, standalone.StatusCode);

        var missing = await SendAsync(member.Http, HttpMethod.Get, $"/api/invoices/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("invoice_not_found", await missing.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Writes_against_a_hidden_invoice_are_404_and_change_nothing()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, FinanceAccess, [w.LocA]);

        // Void (issued invoice at the other location), delete (the draft that has no location) and a payment claim.
        var voided = await SendAsync(member.Http, HttpMethod.Post, $"/api/invoices/{w.InvoiceB}/void", new { reason = "Scope probe" }, csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, voided.StatusCode);
        Assert.Contains("invoice_not_found", await voided.Content.ReadAsStringAsync());

        var deleted = await SendAsync(member.Http, HttpMethod.Delete, $"/api/invoices/{w.StandaloneInvoice}", csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);

        var paid = await SendAsync(member.Http, HttpMethod.Post, $"/api/invoices/{w.InvoiceB}/payments",
            new { amount = 10m, method = "Cash" }, csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, paid.StatusCode);
        Assert.Contains("invoice_not_found", await paid.Content.ReadAsStringAsync());

        // The owner still sees the invoice as issued (not void) and the standalone draft as present.
        var invoice = await SendAsync(w.Owner.Http, HttpMethod.Get, $"/api/invoices/{w.InvoiceB}");
        Assert.Equal(HttpStatusCode.OK, invoice.StatusCode);
        Assert.DoesNotContain("\"Void\"", await invoice.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.Owner.Http, HttpMethod.Get, $"/api/invoices/{w.StandaloneInvoice}")).StatusCode);
    }

    [Fact]
    public async Task A_hidden_invoice_or_receipt_pdf_is_a_404()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, FinanceAccess, [w.LocA]);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(member.Http, HttpMethod.Get, $"/api/invoices/{w.InvoiceB}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(member.Http, HttpMethod.Get, $"/api/receipts/{w.ReceiptB}/pdf")).StatusCode);
    }

    [Fact]
    public async Task A_member_with_no_locations_sees_no_invoices_or_payments()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, FinanceAccess, []);

        var invoices = await (await SendAsync(member.Http, HttpMethod.Get, "/api/invoices")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(w.InvoiceA.ToString(), invoices, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.InvoiceB.ToString(), invoices, StringComparison.OrdinalIgnoreCase);

        var payments = await (await SendAsync(member.Http, HttpMethod.Get, "/api/payments")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(w.PaymentA.ToString(), payments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.PaymentB.ToString(), payments, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(member.Http, HttpMethod.Get, $"/api/invoices/{w.InvoiceA}")).StatusCode);
    }

    // ---- payments and receipts -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_restricted_member_lists_only_payments_at_their_location()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, FinanceAccess, [w.LocA]);

        var res = await SendAsync(member.Http, HttpMethod.Get, "/api/payments");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains(w.PaymentA.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.PaymentB.ToString(), body, StringComparison.OrdinalIgnoreCase);

        // Filtering by a hidden invoice must not reach its payments either.
        var filtered = await (await SendAsync(member.Http, HttpMethod.Get, $"/api/payments?invoiceId={w.InvoiceB}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(w.PaymentB.ToString(), filtered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_hidden_payment_and_its_receipt_are_404_through_every_route()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, FinanceAccess, [w.LocA]);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(member.Http, HttpMethod.Get, $"/api/payments/{w.PaymentA}")).StatusCode);

        var payment = await SendAsync(member.Http, HttpMethod.Get, $"/api/payments/{w.PaymentB}");
        Assert.Equal(HttpStatusCode.NotFound, payment.StatusCode);
        Assert.Contains("payment_not_found", await payment.Content.ReadAsStringAsync());

        var viaPayment = await SendAsync(member.Http, HttpMethod.Get, $"/api/payments/{w.PaymentB}/receipt");
        Assert.Equal(HttpStatusCode.NotFound, viaPayment.StatusCode);
        Assert.Contains("payment_not_found", await viaPayment.Content.ReadAsStringAsync());

        var receipt = await SendAsync(member.Http, HttpMethod.Get, $"/api/receipts/{w.ReceiptB}");
        Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
        Assert.Contains("receipt_not_found", await receipt.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Verifying_or_reversing_a_hidden_payment_is_404_and_leaves_it_unchanged()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, FinanceAccess, [w.LocA]);

        // PaymentB is verified already; PaymentA is pending, so the probe on A's sibling would be a different path. Use B for reverse/reject.
        var reversed = await SendAsync(member.Http, HttpMethod.Post, $"/api/payments/{w.PaymentB}/reverse", new { reason = "Scope probe" }, csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, reversed.StatusCode);
        Assert.Contains("payment_not_found", await reversed.Content.ReadAsStringAsync());

        var rejected = await SendAsync(member.Http, HttpMethod.Post, $"/api/payments/{w.PaymentB}/reject", new { reason = "Scope probe" }, csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);

        var verified = await SendAsync(member.Http, HttpMethod.Post, $"/api/payments/{w.PaymentB}/verify", csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, verified.StatusCode);

        var after = await SendAsync(w.Owner.Http, HttpMethod.Get, $"/api/payments/{w.PaymentB}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Contains("\"Verified\"", await after.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_owner_still_sees_every_invoice_payment_and_receipt()
    {
        var w = await SeedWorldAsync();

        var invoices = await (await SendAsync(w.Owner.Http, HttpMethod.Get, "/api/invoices")).Content.ReadAsStringAsync();
        Assert.Contains(w.InvoiceA.ToString(), invoices, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(w.InvoiceB.ToString(), invoices, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(w.StandaloneInvoice.ToString(), invoices, StringComparison.OrdinalIgnoreCase);

        var payments = await (await SendAsync(w.Owner.Http, HttpMethod.Get, "/api/payments")).Content.ReadAsStringAsync();
        Assert.Contains(w.PaymentA.ToString(), payments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(w.PaymentB.ToString(), payments, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.Owner.Http, HttpMethod.Get, $"/api/receipts/{w.ReceiptB}")).StatusCode);
    }

    // ---- production tasks ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_restricted_member_lists_only_tasks_at_their_location()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, TaskAccess, [w.LocA]);

        var body = await (await SendAsync(member.Http, HttpMethod.Get, "/api/tasks")).Content.ReadAsStringAsync();
        Assert.Contains(w.TaskA.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.TaskB.ToString(), body, StringComparison.OrdinalIgnoreCase);

        // Filtering by the hidden order must not reach its tasks.
        var filtered = await (await SendAsync(member.Http, HttpMethod.Get, $"/api/tasks?orderId={w.OrderB}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(w.TaskB.ToString(), filtered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_hidden_task_is_404_for_read_cancel_and_job_sheet()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, TaskAccess, [w.LocA]);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(member.Http, HttpMethod.Get, $"/api/tasks/{w.TaskA}")).StatusCode);

        var hidden = await SendAsync(member.Http, HttpMethod.Get, $"/api/tasks/{w.TaskB}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Contains("task_not_found", await hidden.Content.ReadAsStringAsync());

        var cancel = await SendAsync(member.Http, HttpMethod.Post, $"/api/tasks/{w.TaskB}/cancel", new { reason = "Scope probe" }, csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Contains("task_not_found", await cancel.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(member.Http, HttpMethod.Get, $"/api/tasks/{w.TaskB}/job-sheet-pdf")).StatusCode);

        // Still open for the owner afterwards (the cancel did not go through).
        var after = await SendAsync(w.Owner.Http, HttpMethod.Get, $"/api/tasks/{w.TaskB}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.DoesNotContain("\"Cancelled\"", await after.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Generating_tasks_for_a_hidden_order_is_404()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, TaskAccess, [w.LocA]);

        var res = await SendAsync(member.Http, HttpMethod.Post, $"/api/orders/{w.OrderB}/generate-tasks", new { }, csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains("order_not_found", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_member_with_no_locations_sees_no_tasks_and_the_owner_sees_all()
    {
        var w = await SeedWorldAsync();
        var none = await MemberAsync(w, TaskAccess, []);

        var hidden = await (await SendAsync(none.Http, HttpMethod.Get, "/api/tasks")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(w.TaskA.ToString(), hidden, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.TaskB.ToString(), hidden, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(none.Http, HttpMethod.Get, $"/api/tasks/{w.TaskA}")).StatusCode);

        var all = await (await SendAsync(w.Owner.Http, HttpMethod.Get, "/api/tasks")).Content.ReadAsStringAsync();
        Assert.Contains(w.TaskA.ToString(), all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(w.TaskB.ToString(), all, StringComparison.OrdinalIgnoreCase);
    }

    // ---- setup ---------------------------------------------------------------------------------------------------------------

    private async Task<World> SeedWorldAsync()
    {
        var owner = await RegisterAsync("finscope");

        var locations = await SendAsync(owner.Http, HttpMethod.Get, "/api/locations");
        Assert.Equal(HttpStatusCode.OK, locations.StatusCode);
        using var locDoc = JsonDocument.Parse(await locations.Content.ReadAsStringAsync());
        var locA = locDoc.RootElement[0].GetProperty("id").GetGuid();
        var locB = await factory.SeedLocationAsync(owner.BusinessId, "Second branch");

        var customer = await SendAsync(owner.Http, HttpMethod.Post, "/api/customers", new { fullName = "Ada Obi" }, csrf: owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
        var customerId = await IdOfAsync(customer);

        var orderA = await ConfirmedOrderAsync(owner, customerId, locA);
        var orderB = await ConfirmedOrderAsync(owner, customerId, locB);

        var invoiceA = await IssuedInvoiceAsync(owner, orderA);
        var invoiceB = await IssuedInvoiceAsync(owner, orderB);

        var standalone = await SendAsync(owner.Http, HttpMethod.Post, "/api/invoices", new
        {
            customerId,
            lines = new[] { new { description = "Walk-in alteration", quantity = 1, unitPrice = 20m } },
        }, csrf: owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, standalone.StatusCode);

        // A is left Pending, B is verified on the spot (which issues its receipt).
        var payA = await RecordPaymentAsync(owner, invoiceA, verifyNow: false);
        var payB = await RecordPaymentAsync(owner, invoiceB, verifyNow: true);
        Assert.NotNull(payB.ReceiptId);

        var taskA = await FirstTaskAsync(owner, orderA);
        var taskB = await FirstTaskAsync(owner, orderB);

        return new World(owner, locA, locB, orderA, orderB, invoiceA, invoiceB, await IdOfAsync(standalone),
            payA.Id, payB.Id, payB.ReceiptId!.Value, taskA, taskB);
    }

    private async Task<Guid> ConfirmedOrderAsync(Session owner, Guid customerId, Guid locationId)
    {
        var created = await SendAsync(owner.Http, HttpMethod.Post, "/api/orders", new
        {
            customerId,
            locationId,
            promisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            garments = new[] { new { name = "Shirt", quantity = 1, unitPrice = 100m } },
        }, csrf: owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = await IdOfAsync(created);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner.Http, HttpMethod.Post, $"/api/orders/{id}/quote", csrf: owner.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner.Http, HttpMethod.Post, $"/api/orders/{id}/confirm", csrf: owner.Csrf)).StatusCode); // generates the tasks
        return id;
    }

    private static async Task<Guid> IssuedInvoiceAsync(Session owner, Guid orderId)
    {
        var draft = await SendAsync(owner.Http, HttpMethod.Post, $"/api/invoices/from-order/{orderId}", new { mode = "Full" }, csrf: owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var id = await IdOfAsync(draft);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(owner.Http, HttpMethod.Post, $"/api/invoices/{id}/issue", csrf: owner.Csrf)).StatusCode);
        return id;
    }

    private sealed record PaymentRef(Guid Id, Guid? ReceiptId);

    private static async Task<PaymentRef> RecordPaymentAsync(Session owner, Guid invoiceId, bool verifyNow)
    {
        var res = await SendAsync(owner.Http, HttpMethod.Post, $"/api/invoices/{invoiceId}/payments",
            new { amount = 100m, method = "Cash", verifyNow }, csrf: owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var receipt = doc.RootElement.TryGetProperty("receiptId", out var r) && r.ValueKind == JsonValueKind.String ? r.GetGuid() : (Guid?)null;
        return new PaymentRef(doc.RootElement.GetProperty("id").GetGuid(), receipt);
    }

    private static async Task<Guid> FirstTaskAsync(Session owner, Guid orderId)
    {
        var res = await SendAsync(owner.Http, HttpMethod.Get, $"/api/tasks?orderId={orderId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Confirming the order should have generated production tasks.");
        return items[0].GetProperty("id").GetGuid();
    }

    private async Task<Session> MemberAsync(World w, string[] permissions, Guid[] locations)
    {
        var (email, membershipId) = await factory.SeedMemberAsync(w.Owner.BusinessId, permissions);
        if (locations.Length > 0) await factory.AssignLocationsAsync(w.Owner.BusinessId, membershipId, locations);
        return await LoginAsync(email);
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // ---- HTTP helpers (same approach as LocationScopeApiTests) ---------------------------------------------------------------

    private static async Task<string> CsrfAsync(HttpClient http)
    {
        using var res = await http.GetAsync("/api/auth/csrf");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string url, object? body = null, string? csrf = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (csrf is not null) req.Headers.Add("X-XSRF-TOKEN", csrf);
        return await http.SendAsync(req);
    }

    private async Task<Session> RegisterAsync(string name)
    {
        var http = factory.NewClient();
        var email = $"{name}-{Guid.NewGuid():N}@test.local";
        var res = await SendAsync(http, HttpMethod.Post, "/api/auth/register-business", new
        {
            businessName = $"{name} Atelier", ownerName = $"{name} Owner", email, password = ApiFactory.DefaultPassword, timeZoneId = "UTC", currencyCode = "NGN",
        }, csrf: await CsrfAsync(http));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var businessId = doc.RootElement.GetProperty("businessId").GetGuid();
        return new Session(http, email, businessId, await CsrfAsync(http)); // a CSRF token is bound to the signed-in user: fetch a new one
    }

    private async Task<Session> LoginAsync(string email)
    {
        var http = factory.NewClient();
        var res = await SendAsync(http, HttpMethod.Post, "/api/auth/login", new { email, password = ApiFactory.DefaultPassword }, csrf: await CsrfAsync(http));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return new Session(http, email, doc.RootElement.GetProperty("businessId").GetGuid(), await CsrfAsync(http));
    }
}
