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

    [Fact]
    public async Task Expenses_and_categories_are_isolated_between_businesses_for_reads_and_writes()
    {
        var a = await RegisterAsync("expense-tenant-a");
        var b = await RegisterAsync("expense-tenant-b");
        var locationA = await FirstLocationAsync(a);
        var categoryResponse = await SendAsync(a.Http, HttpMethod.Post, "/api/expense-categories", new
        {
            name = "Tenant A materials", isActive = true, sortOrder = 1,
        }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        using var categoryDoc = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var categoryId = categoryDoc.RootElement.GetProperty("id").GetGuid();

        var expenseResponse = await SendAsync(a.Http, HttpMethod.Post, "/api/expenses", new
        {
            categoryId, expenseDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), amount = 1250m,
            description = "Fabric purchase for tenant A", vendor = "Local supplier", locationId = locationA,
        }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, expenseResponse.StatusCode);
        using var expenseDoc = JsonDocument.Parse(await expenseResponse.Content.ReadAsStringAsync());
        var expenseId = expenseDoc.RootElement.GetProperty("id").GetGuid();
        var version = expenseDoc.RootElement.GetProperty("version").GetUInt32();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Get, $"/api/expenses/{expenseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Get, $"/api/expenses/{expenseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Put, $"/api/expenses/{expenseId}", new
        {
            categoryId, expenseDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), amount = 9999m,
            description = "Unauthorized edit", version,
        }, b.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Post, $"/api/expenses/{expenseId}/void", new
        {
            reason = "Unauthorized void attempt",
        }, b.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Put, $"/api/expense-categories/{categoryId}", new
        {
            name = "Hijacked category", isActive = true, sortOrder = 1,
        }, b.Csrf)).StatusCode);

        var exportA = await SendAsync(a.Http, HttpMethod.Get, "/api/expenses/export");
        Assert.Equal(HttpStatusCode.OK, exportA.StatusCode);
        Assert.Contains("Fabric purchase for tenant A", await exportA.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var exportB = await SendAsync(b.Http, HttpMethod.Get, "/api/expenses/export");
        Assert.Equal(HttpStatusCode.OK, exportB.StatusCode);
        Assert.DoesNotContain("Fabric purchase for tenant A", await exportB.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var foreignExpenseList = await SendAsync(b.Http, HttpMethod.Get, "/api/expenses");
        Assert.Equal(HttpStatusCode.OK, foreignExpenseList.StatusCode);
        Assert.DoesNotContain(expenseId.ToString(), await foreignExpenseList.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var foreignCategories = await SendAsync(b.Http, HttpMethod.Get, "/api/expense-categories");
        Assert.Equal(HttpStatusCode.OK, foreignCategories.StatusCode);
        Assert.DoesNotContain(categoryId.ToString(), await foreignCategories.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // Verify the denied writes did not mutate the owning tenant's records.
        var unchanged = await SendAsync(a.Http, HttpMethod.Get, $"/api/expenses/{expenseId}");
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        var unchangedBody = await unchanged.Content.ReadAsStringAsync();
        Assert.Contains("Fabric purchase for tenant A", unchangedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Unauthorized edit", unchangedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worker_availability_and_leave_are_isolated_between_businesses()
    {
        var a = await RegisterAsync("schedule-tenant-a");
        var b = await RegisterAsync("schedule-tenant-b");
        var workersA = await SendAsync(a.Http, HttpMethod.Get, "/api/workers");
        Assert.Equal(HttpStatusCode.OK, workersA.StatusCode);
        using var workerDoc = JsonDocument.Parse(await workersA.Content.ReadAsStringAsync());
        Assert.True(workerDoc.RootElement.GetArrayLength() > 0, "The registered business should have an owner membership.");
        var memberId = workerDoc.RootElement[0].GetProperty("membershipId").GetGuid();
        var start = new DateTimeOffset(DateTime.UtcNow.AddDays(8).Date.AddHours(9), TimeSpan.Zero);
        var created = await SendAsync(a.Http, HttpMethod.Post, $"/api/workers/{memberId}/schedule/leave", new
        {
            startsAtUtc = start, endsAtUtc = start.AddHours(2), reason = "Isolation test leave",
        }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var leaveDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var leaveId = leaveDoc.RootElement.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Get, $"/api/workers/{memberId}/schedule/availability")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Get, $"/api/workers/{memberId}/schedule/availability")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Get,
            $"/api/workers/{memberId}/schedule/leave?fromUtc={Uri.EscapeDataString(start.AddDays(-1).ToString("O"))}&toUtc={Uri.EscapeDataString(start.AddDays(1).ToString("O"))}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Delete,
            $"/api/workers/{memberId}/schedule/leave/{leaveId}", csrf: b.Csrf)).StatusCode);

        var ownLeave = await SendAsync(a.Http, HttpMethod.Get,
            $"/api/workers/{memberId}/schedule/leave?fromUtc={Uri.EscapeDataString(start.AddDays(-1).ToString("O"))}&toUtc={Uri.EscapeDataString(start.AddDays(1).ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, ownLeave.StatusCode);
        Assert.Contains(leaveId.ToString(), await ownLeave.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inventory_items_and_movements_are_isolated_between_businesses()
    {
        var a = await RegisterAsync("inventory-tenant-a");
        var b = await RegisterAsync("inventory-tenant-b");
        var locationA = await FirstLocationAsync(a);
        var created = await SendAsync(a.Http, HttpMethod.Post, "/api/inventory", new
        {
            sku = "FAB-" + Guid.NewGuid().ToString("N")[..8], name = "Tenant A fabric", unit = "metre", locationId = locationA,
            openingStock = 25m, reorderLevel = 5m, notes = "Isolation test fixture",
        }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var itemDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var itemId = itemDoc.RootElement.GetProperty("id").GetGuid();

        var ownList = await SendAsync(a.Http, HttpMethod.Get, "/api/inventory");
        Assert.Contains(itemId.ToString(), await ownList.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var foreignList = await SendAsync(b.Http, HttpMethod.Get, "/api/inventory");
        Assert.Equal(HttpStatusCode.OK, foreignList.StatusCode);
        Assert.DoesNotContain(itemId.ToString(), await foreignList.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Get, $"/api/inventory/{itemId}/movements")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Post, $"/api/inventory/{itemId}/receive", new
        {
            quantity = 100m, note = "Unauthorized stock increase", expectedVersion = 1,
        }, b.Csrf)).StatusCode);

        var ownerHistory = await SendAsync(a.Http, HttpMethod.Get, $"/api/inventory/{itemId}/movements");
        Assert.Equal(HttpStatusCode.OK, ownerHistory.StatusCode);
        Assert.DoesNotContain("Unauthorized stock increase", await ownerHistory.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Storefront_design_and_collection_resources_are_isolated_between_businesses()
    {
        var a = await RegisterAsync("catalog-tenant-a");
        var b = await RegisterAsync("catalog-tenant-b");

        var designResponse = await SendAsync(a.Http, HttpMethod.Post, "/api/designs", new
        {
            name = "Private Tenant A Design", kind = "ReadyMade",
        }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, designResponse.StatusCode);
        var designId = await IdOfAsync(designResponse);

        var collectionResponse = await SendAsync(a.Http, HttpMethod.Post, "/api/collections", new
        {
            name = "Private Tenant A Collection", visibility = "Private",
        }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, collectionResponse.StatusCode);
        var collectionId = await IdOfAsync(collectionResponse);

        var storefrontResponse = await SendAsync(a.Http, HttpMethod.Post, "/api/storefronts", new
        {
            name = "Tenant A Atelier", slug = "tenant-a-" + Guid.NewGuid().ToString("N")[..8], templateKey = "standard",
        }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, storefrontResponse.StatusCode);
        var storefrontId = await IdOfAsync(storefrontResponse);

        foreach (var url in new[] { $"/api/designs/{designId}", $"/api/collections/{collectionId}", $"/api/storefronts/{storefrontId}", $"/api/storefronts/{storefrontId}/draft" })
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Get, url)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Put, $"/api/collections/{collectionId}", new
        {
            name = "Hijacked", visibility = "Private", version = 1,
        }, b.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Post, $"/api/designs/{designId}/visibility", new
        {
            visibility = "Public",
        }, b.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Put, $"/api/storefronts/{storefrontId}/settings", new
        {
            version = 1, name = "Hijacked Atelier", workflowInquiry = true, workflowContact = true,
        }, b.Csrf)).StatusCode);

        foreach (var (url, foreignId) in new[] { ("/api/designs", designId), ("/api/collections", collectionId), ("/api/storefronts", storefrontId) })
        {
            var response = await SendAsync(b.Http, HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(foreignId.ToString(), await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Customer_portal_link_revocation_cannot_target_another_business_customer()
    {
        var a = await RegisterAsync("portal-tenant-a");
        var b = await RegisterAsync("portal-tenant-b");
        var customer = await SendAsync(a.Http, HttpMethod.Post, "/api/customers", new
        {
            fullName = "Portal Customer A", email = "portal-a@example.test",
        }, a.Csrf);
        Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
        var customerId = await IdOfAsync(customer);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(a.Http, HttpMethod.Delete, $"/api/customer-portal/customers/{customerId}/links", csrf: a.Csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Delete, $"/api/customer-portal/customers/{customerId}/links", csrf: b.Csrf)).StatusCode);
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
