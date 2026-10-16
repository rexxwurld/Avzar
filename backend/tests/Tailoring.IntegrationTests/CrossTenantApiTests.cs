using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// Business-to-business isolation through the real HTTP pipeline. The owner of business B holds a valid session and every permission in
/// their own business, and probes the customer, order, invoice, payment, receipt and task ids of business A. Every probe must behave
/// exactly like a missing record (404, same error code), writes must change nothing, and list endpoints must not leak A's ids.
/// A client-supplied foreign customer id on a write must be refused. Complements <c>TenantIsolationTests</c> (DbContext level) and the
/// location-scope API tests (same business, different branch).
/// </summary>
[Collection("api")]
public sealed class CrossTenantApiTests(ApiFactory factory)
{
    private sealed record Session(HttpClient Http, Guid BusinessId, string Csrf);

    private sealed record World(Session A, Session B, Guid Customer, Guid Order, Guid Invoice, Guid Payment, Guid Receipt, Guid Task);

    [Fact]
    public async Task Owner_of_one_business_cannot_read_another_businesss_records_by_id()
    {
        var w = await SeedWorldAsync();

        // Control: the real owner can read every record, so the 404s below are isolation and not a wrong route.
        foreach (var url in OwnReadRoutes(w))
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.A.Http, HttpMethod.Get, url)).StatusCode);

        var probes = new (string Url, string? Code)[]
        {
            ($"/api/customers/{w.Customer}", null),
            ($"/api/orders/{w.Order}", "order_not_found"),
            ($"/api/invoices/{w.Invoice}", "invoice_not_found"),
            ($"/api/payments/{w.Payment}", "payment_not_found"),
            ($"/api/payments/{w.Payment}/receipt", "payment_not_found"),
            ($"/api/receipts/{w.Receipt}", "receipt_not_found"),
            ($"/api/tasks/{w.Task}", "task_not_found"),
        };
        foreach (var (url, code) in probes)
        {
            var res = await SendAsync(w.B.Http, HttpMethod.Get, url);
            Assert.True(res.StatusCode == HttpStatusCode.NotFound, $"{url} returned {(int)res.StatusCode}, expected 404");
            if (code is not null) Assert.Contains(code, await res.Content.ReadAsStringAsync());

            // Indistinguishable from an id that never existed.
            var missing = await SendAsync(w.B.Http, HttpMethod.Get, url.Replace(IdIn(url), Guid.NewGuid().ToString()));
            Assert.Equal(res.StatusCode, missing.StatusCode);
        }
    }

    [Fact]
    public async Task Pdf_and_job_sheet_endpoints_do_not_cross_businesses()
    {
        var w = await SeedWorldAsync();
        foreach (var url in new[] { $"/api/invoices/{w.Invoice}/pdf", $"/api/receipts/{w.Receipt}/pdf", $"/api/tasks/{w.Task}/job-sheet-pdf" })
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.B.Http, HttpMethod.Get, url)).StatusCode);
    }

    [Fact]
    public async Task List_endpoints_never_contain_another_businesss_ids()
    {
        var w = await SeedWorldAsync();
        var ids = new[] { w.Customer, w.Order, w.Invoice, w.Payment, w.Receipt, w.Task };
        foreach (var url in new[] { "/api/customers", "/api/orders", "/api/invoices", "/api/payments", "/api/tasks" })
        {
            var res = await SendAsync(w.B.Http, HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            foreach (var id in ids) Assert.DoesNotContain(id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }
        // Filtering by a foreign id must not reach the foreign rows either.
        var filtered = await SendAsync(w.B.Http, HttpMethod.Get, $"/api/tasks?orderId={w.Order}");
        Assert.DoesNotContain(w.Task.ToString(), await filtered.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Writes_against_another_businesss_records_are_404_and_change_nothing()
    {
        var w = await SeedWorldAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.B.Http, HttpMethod.Post, $"/api/invoices/{w.Invoice}/void", new { reason = "Isolation probe" }, w.B.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.B.Http, HttpMethod.Post, $"/api/invoices/{w.Invoice}/payments", new { amount = 10m, method = "Cash" }, w.B.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.B.Http, HttpMethod.Post, $"/api/payments/{w.Payment}/reverse", new { reason = "Isolation probe" }, w.B.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.B.Http, HttpMethod.Post, $"/api/payments/{w.Payment}/reject", new { reason = "Isolation probe" }, w.B.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.B.Http, HttpMethod.Post, $"/api/tasks/{w.Task}/cancel", new { reason = "Isolation probe" }, w.B.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.B.Http, HttpMethod.Post, $"/api/orders/{w.Order}/generate-tasks", new { }, w.B.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.B.Http, HttpMethod.Post, $"/api/orders/{w.Order}/cancel", new { reason = "Isolation probe" }, w.B.Csrf)).StatusCode);

        // Business A's data is exactly as it was.
        var invoice = await (await SendAsync(w.A.Http, HttpMethod.Get, $"/api/invoices/{w.Invoice}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"Void\"", invoice);
        var payment = await (await SendAsync(w.A.Http, HttpMethod.Get, $"/api/payments/{w.Payment}")).Content.ReadAsStringAsync();
        Assert.Contains("\"Verified\"", payment);
        var task = await (await SendAsync(w.A.Http, HttpMethod.Get, $"/api/tasks/{w.Task}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"Cancelled\"", task);
    }

    [Fact]
    public async Task A_foreign_customer_id_supplied_by_the_client_is_refused_on_create()
    {
        var w = await SeedWorldAsync();
        var locationB = await FirstLocationAsync(w.B);

        var order = await SendAsync(w.B.Http, HttpMethod.Post, "/api/orders", new
        {
            customerId = w.Customer,
            locationId = locationB,
            promisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            garments = new[] { new { name = "Shirt", quantity = 1, unitPrice = 100m } },
        }, w.B.Csrf);
        Assert.True((int)order.StatusCode is >= 400 and < 500, $"Creating an order for a foreign customer returned {(int)order.StatusCode}.");

        var invoice = await SendAsync(w.B.Http, HttpMethod.Post, "/api/invoices", new
        {
            customerId = w.Customer,
            lines = new[] { new { description = "Isolation probe", quantity = 1, unitPrice = 5m } },
        }, w.B.Csrf);
        Assert.True((int)invoice.StatusCode is >= 400 and < 500, $"Creating an invoice for a foreign customer returned {(int)invoice.StatusCode}.");

        // Nothing was created in either business.
        var ordersA = await (await SendAsync(w.A.Http, HttpMethod.Get, "/api/orders")).Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(ordersA);
        Assert.Equal(1, doc.RootElement.GetProperty("items").GetArrayLength());
        var ordersB = await (await SendAsync(w.B.Http, HttpMethod.Get, "/api/orders")).Content.ReadAsStringAsync();
        using var docB = JsonDocument.Parse(ordersB);
        Assert.Equal(0, docB.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_business_id_in_the_request_does_not_grant_access_to_another_business()
    {
        var w = await SeedWorldAsync();
        // Whatever a client sends for tenancy must not matter: only the session decides the business.
        var viaQuery = await SendAsync(w.B.Http, HttpMethod.Get, $"/api/invoices?businessId={w.A.BusinessId}");
        Assert.DoesNotContain(w.Invoice.ToString(), await viaQuery.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{w.Invoice}");
        req.Headers.Add("X-Business-Id", w.A.BusinessId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await w.B.Http.SendAsync(req)).StatusCode);
    }

    // ---- setup ---------------------------------------------------------------------------------------------------------------

    private static string[] OwnReadRoutes(World w) =>
    [
        $"/api/customers/{w.Customer}", $"/api/orders/{w.Order}", $"/api/invoices/{w.Invoice}", $"/api/payments/{w.Payment}",
        $"/api/receipts/{w.Receipt}", $"/api/tasks/{w.Task}",
    ];

    private static string IdIn(string url) =>
        url.Split('/').First(part => Guid.TryParse(part, out _));

    private async Task<World> SeedWorldAsync()
    {
        var a = await RegisterAsync("tenant-a");
        var b = await RegisterAsync("tenant-b");

        var customer = await SendAsync(a.Http, HttpMethod.Post, "/api/customers", new { fullName = "Ada Obi" }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
        var customerId = await IdOfAsync(customer);

        var order = await SendAsync(a.Http, HttpMethod.Post, "/api/orders", new
        {
            customerId,
            locationId = await FirstLocationAsync(a),
            promisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            garments = new[] { new { name = "Shirt", quantity = 1, unitPrice = 100m } },
        }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        var orderId = await IdOfAsync(order);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Post, $"/api/orders/{orderId}/quote", csrf: a.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Post, $"/api/orders/{orderId}/confirm", csrf: a.Csrf)).StatusCode);

        var draft = await SendAsync(a.Http, HttpMethod.Post, $"/api/invoices/from-order/{orderId}", new { mode = "Full" }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var invoiceId = await IdOfAsync(draft);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Post, $"/api/invoices/{invoiceId}/issue", csrf: a.Csrf)).StatusCode);

        var pay = await SendAsync(a.Http, HttpMethod.Post, $"/api/invoices/{invoiceId}/payments", new { amount = 100m, method = "Cash", verifyNow = true }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, pay.StatusCode);
        using var payDoc = JsonDocument.Parse(await pay.Content.ReadAsStringAsync());
        var paymentId = payDoc.RootElement.GetProperty("id").GetGuid();
        var receiptId = payDoc.RootElement.GetProperty("receiptId").GetGuid();

        var tasks = await SendAsync(a.Http, HttpMethod.Get, $"/api/tasks?orderId={orderId}");
        Assert.Equal(HttpStatusCode.OK, tasks.StatusCode);
        using var taskDoc = JsonDocument.Parse(await tasks.Content.ReadAsStringAsync());
        var items = taskDoc.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Confirming the order should have generated production tasks.");

        return new World(a, b, customerId, orderId, invoiceId, paymentId, receiptId, items[0].GetProperty("id").GetGuid());
    }

    private static async Task<Guid> FirstLocationAsync(Session s)
    {
        var res = await SendAsync(s.Http, HttpMethod.Get, "/api/locations");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement[0].GetProperty("id").GetGuid();
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // ---- HTTP helpers (same approach as FinanceTaskLocationScopeApiTests) ----------------------------------------------------

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
        }, await CsrfAsync(http));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var businessId = doc.RootElement.GetProperty("businessId").GetGuid();
        return new Session(http, businessId, await CsrfAsync(http)); // a CSRF token is bound to the signed-in user: fetch a new one
    }
}
