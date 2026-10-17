using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tailoring.Application.Locations;
using Tailoring.Domain.Access;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Drives the real HTTP pipeline (Program.cs) end to end: who may call what, CSRF, bearer vs cookie, tenant isolation.</summary>
[Collection("api")]
public sealed class ApiAuthorizationTests(ApiFactory factory)
{
    // Controller endpoints that are open WITHOUT an [AllowAnonymous] or [Authorize] attribute. Everything else must carry one of the
    // two. A new unprotected endpoint fails the test below until it is protected or added here on purpose.
    private static readonly HashSet<string> ImplicitlyOpen = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET api/auth/csrf",
        "POST api/auth/register-business",
        "POST api/auth/login",
        "POST api/auth/accept-invitation",
        "POST api/auth/confirm-email",
        "POST api/auth/forgot-password",
        "POST api/auth/reset-password",
        "POST api/auth/token",
        "POST api/auth/token/refresh",
        "POST api/auth/token/revoke",
    };

    private sealed record ControllerEndpoint(string Method, string Pattern, bool Open, bool ExplicitlyAnonymous, bool Multipart = false)
    {
        public string Key => $"{Method} {Pattern}";
    }

    private List<ControllerEndpoint> Endpoints()
    {
        var source = factory.Services.GetRequiredService<EndpointDataSource>();
        var list = new List<ControllerEndpoint>();
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is null) continue; // health checks etc.
            var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "GET";
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var protectedByAttribute = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            // Form-bound actions (IFormFile / [FromForm]) accept only multipart/form-data; the routing layer answers 415 to any other
            // content type BEFORE authentication runs, so the probe must use the content type the endpoint declares.
            var multipart = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes
                .Any(c => c.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) == true
                || endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.Parameters
                    .Any(p => p.BindingInfo?.BindingSource == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Form
                           || p.BindingInfo?.BindingSource == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.FormFile);
            list.Add(new ControllerEndpoint(method, (endpoint.RoutePattern.RawText ?? "").TrimStart('/'), anonymous || !protectedByAttribute, anonymous, multipart));
        }
        return list;
    }

    [Fact]
    public void The_location_scope_service_is_registered()
    {
        // Order, invoice, payment, expense, fitting, search and task services require ILocationScope in their constructors. If the registration
        // is ever dropped, DI activation fails rather than silently disabling location filtering.
        using var scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<ILocationScope>());
    }

    [Fact]
    public void Location_scoping_is_a_required_dependency_for_sensitive_services()
    {
        var services = new[]
        {
            typeof(Tailoring.Infrastructure.Services.OrderService),
            typeof(Tailoring.Infrastructure.Services.TaskService),
            typeof(Tailoring.Infrastructure.Services.InvoiceService),
            typeof(Tailoring.Infrastructure.Services.PaymentService),
            typeof(Tailoring.Infrastructure.Services.ExpenseService),
            typeof(Tailoring.Infrastructure.Services.FittingService),
            typeof(Tailoring.Infrastructure.Services.SearchService),
            typeof(Tailoring.Infrastructure.Services.WorkerService),
            typeof(Tailoring.Infrastructure.Services.CustomerService),
        };

        foreach (var service in services)
        {
            var parameter = Assert.Single(service.GetConstructors().Single().GetParameters(), p => p.ParameterType == typeof(ILocationScope));
            Assert.False(parameter.IsOptional, $"{service.Name} must not be able to start without ILocationScope.");
        }
    }

    [Fact]
    public void Every_controller_endpoint_is_protected_or_deliberately_open()
    {
        var all = Endpoints();
        Assert.True(all.Count > 100, $"Expected the full controller surface, found {all.Count} endpoints.");

        var unexpected = all.Where(e => e is { Open: true, ExplicitlyAnonymous: false } && !ImplicitlyOpen.Contains(e.Key)).Select(e => e.Key).ToList();
        Assert.True(unexpected.Count == 0, "Endpoints with no [Authorize], [HasPermission] or [AllowAnonymous]:\n" + string.Join("\n", unexpected));

        var stale = ImplicitlyOpen.Where(k => all.All(e => !string.Equals(e.Key, k, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(stale.Count == 0, "Allow-list entries that match no endpoint (renamed or removed?):\n" + string.Join("\n", stale));
    }

    [Fact]
    public async Task Anonymous_requests_to_protected_endpoints_get_401()
    {
        using var http = factory.NewClient(cookies: false);
        var failures = new List<string>();
        var protectedEndpoints = Endpoints().Where(e => !e.Open).ToList();
        Assert.NotEmpty(protectedEndpoints);

        foreach (var e in protectedEndpoints)
        {
            using var req = new HttpRequestMessage(new HttpMethod(e.Method), "/" + Fill(e.Pattern));
            req.Headers.Add("X-Forwarded-For", factory.NextIp());
            if (e.Method is "POST" or "PUT" or "PATCH")
                req.Content = e.Multipart
                    ? new MultipartFormDataContent { { new StringContent("x"), "probe" } }
                    : new StringContent("{}", Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req);
            if (res.StatusCode != HttpStatusCode.Unauthorized) failures.Add($"{e.Method} /{e.Pattern} -> {(int)res.StatusCode}");
        }
        Assert.True(failures.Count == 0, "Expected 401 without credentials:\n" + string.Join("\n", failures));
    }

    [Fact]
    public async Task A_cookie_write_needs_the_csrf_token()
    {
        var a = await RegisterAsync("csrf");

        var missing = await SendAsync(a.Http, HttpMethod.Post, "/api/customers", new { fullName = "Ada Obi" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("invalid_csrf", await missing.Content.ReadAsStringAsync());

        var wrong = await SendAsync(a.Http, HttpMethod.Post, "/api/customers", new { fullName = "Ada Obi" }, csrf: "not-a-real-token");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var ok = await SendAsync(a.Http, HttpMethod.Post, "/api/customers", new { fullName = "Ada Obi" }, csrf: a.Csrf);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task A_bearer_write_is_exempt_from_csrf_and_a_bad_bearer_never_falls_back_to_the_cookie()
    {
        var a = await RegisterAsync("bearer");

        using var mobile = factory.NewClient(cookies: false);
        var signIn = await SendAsync(mobile, HttpMethod.Post, "/api/auth/token", new { email = a.Email, password = ApiFactory.DefaultPassword, deviceName = "test phone" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        using var tokens = JsonDocument.Parse(await signIn.Content.ReadAsStringAsync());
        var access = tokens.RootElement.GetProperty("accessToken").GetString()!;

        var me = await SendAsync(mobile, HttpMethod.Get, "/api/auth/me", bearer: access);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var create = await SendAsync(mobile, HttpMethod.Post, "/api/customers", new { fullName = "Bola Ade" }, bearer: access); // no CSRF token
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // The cookie client IS signed in, but a garbage bearer header makes the request a bearer request, and it must fail.
        var garbage = await SendAsync(a.Http, HttpMethod.Get, "/api/auth/me", bearer: "garbage.token.value");
        Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);
    }

    [Fact]
    public async Task SignalR_negotiate_accepts_mobile_bearer_token_from_hub_query_only()
    {
        var account = await RegisterAsync("signalr-token");
        using var mobile = factory.NewClient(cookies: false);
        var signIn = await SendAsync(mobile, HttpMethod.Post, "/api/auth/token", new
        {
            email = account.Email,
            password = ApiFactory.DefaultPassword,
            deviceName = "signalr-test"
        });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        using var tokens = JsonDocument.Parse(await signIn.Content.ReadAsStringAsync());
        var access = tokens.RootElement.GetProperty("accessToken").GetString()!;

        // Browser/WebSocket transports cannot reliably attach an Authorization header. SignalR therefore
        // sends access_token on the hub negotiate/WebSocket URL; the API must accept it only under /hubs.
        using var response = await mobile.PostAsync(
            "/hubs/business-events/negotiate?negotiateVersion=1&access_token=" + Uri.EscapeDataString(access),
            new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("connectionToken", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Another_business_cannot_read_a_customer()
    {
        var a = await RegisterAsync("tenant-a");
        var b = await RegisterAsync("tenant-b");

        var created = await SendAsync(a.Http, HttpMethod.Post, "/api/customers", new { fullName = "Only In A" }, csrf: a.Csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Get, $"/api/customers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(b.Http, HttpMethod.Get, $"/api/customers/{id}")).StatusCode);

        var list = await SendAsync(b.Http, HttpMethod.Get, "/api/customers");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain(id, await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_member_without_the_permission_gets_403_and_loses_access_when_deactivated()
    {
        var owner = await RegisterAsync("perm");
        var (email, membershipId) = await factory.SeedMemberAsync(owner.BusinessId, Permissions.CustomersView);

        var staff = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(staff.Http, HttpMethod.Get, "/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(staff.Http, HttpMethod.Post, "/api/customers", new { fullName = "Not Allowed" }, csrf: staff.Csrf)).StatusCode);

        await factory.DeactivateMemberAsync(owner.BusinessId, membershipId);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(staff.Http, HttpMethod.Get, "/api/customers")).StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------

    private sealed record Session(HttpClient Http, string Email, Guid BusinessId, string Csrf);

    private static string Fill(string pattern) => Regex.Replace(pattern, @"\{\*?(?<name>[^}:?]+)(?::(?<c>[^}?=]+))?[^}]*\}", m =>
    {
        var constraint = m.Groups["c"].Value;
        if (constraint.Contains("guid", StringComparison.OrdinalIgnoreCase)) return Guid.NewGuid().ToString();
        return constraint.Contains("int", StringComparison.OrdinalIgnoreCase) || constraint.Contains("long", StringComparison.OrdinalIgnoreCase) ? "1" : "x";
    });

    private static async Task<string> CsrfAsync(HttpClient http)
    {
        using var res = await http.GetAsync("/api/auth/csrf");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string url, object? body = null, string? csrf = null, string? bearer = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (csrf is not null) req.Headers.Add("X-XSRF-TOKEN", csrf);
        if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
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
