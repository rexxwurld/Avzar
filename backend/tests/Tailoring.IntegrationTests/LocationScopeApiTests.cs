using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tailoring.Domain.Access;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// Location scoping through the real HTTP pipeline (authentication, permission handler, DI wiring), complementing the service-level
/// tests. A member limited to one location signs in with the real login endpoint and must see only that location's orders; a hidden
/// order looks exactly like a missing one; creating an order at another location is refused; the owner still sees everything.
/// </summary>
[Collection("api")]
public sealed class LocationScopeApiTests(ApiFactory factory)
{
    private sealed record Session(HttpClient Http, string Email, Guid BusinessId, string Csrf);

    private sealed record World(Session Owner, Guid LocA, Guid LocB, Guid CustomerId, Guid OrderA, Guid OrderB);

    private static readonly string[] OrderAccess = [Permissions.OrdersView, Permissions.OrdersManage, Permissions.CustomersView, Permissions.TasksView, Permissions.WorkersManage, Permissions.CustomersMerge];

    // ---- tests ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_restricted_member_lists_only_orders_at_their_location()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, [w.LocA]);

        var res = await SendAsync(member.Http, HttpMethod.Get, "/api/orders");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains(w.OrderA.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.OrderB.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_hidden_order_is_a_404_and_a_visible_one_is_a_200()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, [w.LocA]);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(member.Http, HttpMethod.Get, $"/api/orders/{w.OrderA}")).StatusCode);

        var hidden = await SendAsync(member.Http, HttpMethod.Get, $"/api/orders/{w.OrderB}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Contains("order_not_found", await hidden.Content.ReadAsStringAsync());

        // The same id for a real-but-hidden order and a made-up one must be indistinguishable.
        var missing = await SendAsync(member.Http, HttpMethod.Get, $"/api/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_restricted_member_cannot_create_an_order_at_another_location_but_can_at_their_own()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, [w.LocA]);

        var forbidden = await SendAsync(member.Http, HttpMethod.Post, "/api/orders", NewOrder(w.CustomerId, w.LocB), csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains("location_forbidden", await forbidden.Content.ReadAsStringAsync());

        var allowed = await SendAsync(member.Http, HttpMethod.Post, "/api/orders", NewOrder(w.CustomerId, w.LocA), csrf: member.Csrf);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task A_member_with_no_locations_sees_no_orders()
    {
        var w = await SeedWorldAsync();
        var member = await MemberAsync(w, []);

        var res = await SendAsync(member.Http, HttpMethod.Get, "/api/orders");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain(w.OrderA.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.OrderB.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(member.Http, HttpMethod.Get, $"/api/orders/{w.OrderA}")).StatusCode);
    }

    [Fact]
    public async Task A_location_restricted_manager_cannot_list_or_edit_workers_only_assigned_to_another_location()
    {
        var w = await SeedWorldAsync();
        var manager = await MemberAsync(w, [w.LocA]);
        var (_, workerB) = await factory.SeedMemberAsync(w.Owner.BusinessId, Permissions.TasksView);
        await factory.AssignLocationsAsync(w.Owner.BusinessId, workerB, w.LocB);

        using var list = await SendAsync(manager.Http, HttpMethod.Get, "/api/workers");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var visibleIds = doc.RootElement.EnumerateArray().Select(x => x.GetProperty("membershipId").GetGuid()).ToArray();
        Assert.NotEmpty(visibleIds);
        Assert.DoesNotContain(workerB, visibleIds);

        var hiddenSkills = await SendAsync(manager.Http, HttpMethod.Put, $"/api/workers/{workerB}/skills", new { skills = new[] { "sewing" } }, csrf: manager.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, hiddenSkills.StatusCode);
        var hiddenCalendar = await SendAsync(manager.Http, HttpMethod.Get, $"/api/workers/{workerB}/schedule/availability");
        Assert.Equal(HttpStatusCode.NotFound, hiddenCalendar.StatusCode);
    }

    [Fact]
    public async Task A_location_restricted_manager_cannot_merge_customer_profiles_that_rewrite_cross_location_records()
    {
        var w = await SeedWorldAsync();
        var manager = await MemberAsync(w, [w.LocA]);
        Guid targetId;
        targetId = await factory.SeedCustomerAsync(w.Owner.BusinessId, "Merge target");

        var response = await SendAsync(manager.Http, HttpMethod.Post, $"/api/customers/{w.CustomerId}/merge-into/{targetId}", csrf: manager.Csrf);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("customer_merge_requires_all_locations", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_owner_still_sees_every_location()
    {
        var w = await SeedWorldAsync();

        var body = await (await SendAsync(w.Owner.Http, HttpMethod.Get, "/api/orders")).Content.ReadAsStringAsync();
        Assert.Contains(w.OrderA.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(w.OrderB.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.Owner.Http, HttpMethod.Get, $"/api/orders/{w.OrderB}")).StatusCode);
    }

    // ---- setup ---------------------------------------------------------------------------------------------------------------

    private async Task<World> SeedWorldAsync()
    {
        var owner = await RegisterAsync("locscope");

        // A new business starts with one default location; the second one is added directly (setup only).
        var locations = await SendAsync(owner.Http, HttpMethod.Get, "/api/locations");
        Assert.Equal(HttpStatusCode.OK, locations.StatusCode);
        using var locDoc = JsonDocument.Parse(await locations.Content.ReadAsStringAsync());
        var locA = locDoc.RootElement[0].GetProperty("id").GetGuid();
        var locB = await factory.SeedLocationAsync(owner.BusinessId, "Second branch");

        var customer = await SendAsync(owner.Http, HttpMethod.Post, "/api/customers", new { fullName = "Ada Obi" }, csrf: owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
        var customerId = await IdOfAsync(customer);

        var orderA = await SendAsync(owner.Http, HttpMethod.Post, "/api/orders", NewOrder(customerId, locA), csrf: owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, orderA.StatusCode);
        var orderB = await SendAsync(owner.Http, HttpMethod.Post, "/api/orders", NewOrder(customerId, locB), csrf: owner.Csrf);
        Assert.Equal(HttpStatusCode.Created, orderB.StatusCode);

        return new World(owner, locA, locB, customerId, await IdOfAsync(orderA), await IdOfAsync(orderB));
    }

    private async Task<Session> MemberAsync(World w, Guid[] locations)
    {
        var (email, membershipId) = await factory.SeedMemberAsync(w.Owner.BusinessId, OrderAccess);
        if (locations.Length > 0) await factory.AssignLocationsAsync(w.Owner.BusinessId, membershipId, locations);
        return await LoginAsync(email);
    }

    private static object NewOrder(Guid customerId, Guid locationId) => new
    {
        customerId,
        locationId,
        promisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
        garments = new[] { new { name = "Shirt", quantity = 1, unitPrice = 100m } },
    };

    private static async Task<Guid> IdOfAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // ---- HTTP helpers (same approach as ApiAuthorizationTests) -----------------------------------------------------------------

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
