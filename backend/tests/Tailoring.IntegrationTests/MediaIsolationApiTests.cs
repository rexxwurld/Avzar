using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// Media isolation through the real HTTP pipeline. The owner of business B holds a valid session and every permission in their own
/// business, and probes the media id of an image uploaded by business A: metadata, every stored variant, the list, delete and the
/// anonymous public route must all behave like a missing record. A client-supplied <c>businessId</c> on upload must be ignored, and
/// an anonymous client must get nothing. Complements <c>MediaTests</c> (service level) and <c>CrossTenantApiTests</c>.
/// </summary>
[Collection("api")]
public sealed class MediaIsolationApiTests(ApiFactory factory)
{
    private sealed record Session(HttpClient Http, Guid BusinessId, string Csrf);

    private static readonly string[] Variants = ["sm", "md", "orig"];

    [Fact]
    public async Task Another_business_cannot_read_list_or_open_an_uploaded_image_by_id()
    {
        var (a, b) = await TwoBusinessesAsync();
        var mediaId = await UploadAsync(a, TestImages.Png(400, 300));

        // Control: the owner can use every route, so the 404s below are isolation and not a wrong URL.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Get, $"/api/media/{mediaId}")).StatusCode);
        foreach (var v in Variants)
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Get, $"/api/media/{mediaId}/file/{v}")).StatusCode);
        var ownList = await SendAsync(a.Http, HttpMethod.Get, "/api/media");
        Assert.Contains(mediaId.ToString(), await ownList.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var probes = new List<string> { $"/api/media/{mediaId}" };
        probes.AddRange(Variants.Select(v => $"/api/media/{mediaId}/file/{v}"));
        foreach (var url in probes)
        {
            var res = await SendAsync(b.Http, HttpMethod.Get, url);
            Assert.True(res.StatusCode == HttpStatusCode.NotFound, $"{url} returned {(int)res.StatusCode}, expected 404");
            Assert.Contains("media_not_found", await res.Content.ReadAsStringAsync());

            // Indistinguishable from an id that never existed.
            var missing = await SendAsync(b.Http, HttpMethod.Get, url.Replace(mediaId.ToString(), Guid.NewGuid().ToString()));
            Assert.Equal(res.StatusCode, missing.StatusCode);
        }

        var foreignList = await SendAsync(b.Http, HttpMethod.Get, "/api/media");
        Assert.Equal(HttpStatusCode.OK, foreignList.StatusCode);
        Assert.DoesNotContain(mediaId.ToString(), await foreignList.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var foreignUsage = await SendAsync(b.Http, HttpMethod.Get, "/api/media/usage");
        Assert.Equal(HttpStatusCode.OK, foreignUsage.StatusCode);
        using var usage = JsonDocument.Parse(await foreignUsage.Content.ReadAsStringAsync());
        Assert.Equal(0, usage.RootElement.GetProperty("usedBytes").GetInt64());
    }

    [Fact]
    public async Task Another_business_cannot_delete_an_image_even_when_it_knows_the_id()
    {
        var (a, b) = await TwoBusinessesAsync();
        var mediaId = await UploadAsync(a, TestImages.Png(400, 300));

        var res = await SendAsync(b.Http, HttpMethod.Delete, $"/api/media/{mediaId}", csrf: b.Csrf);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains("media_not_found", await res.Content.ReadAsStringAsync());

        // Business A's image and every variant are untouched.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Get, $"/api/media/{mediaId}")).StatusCode);
        foreach (var v in Variants)
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Get, $"/api/media/{mediaId}/file/{v}")).StatusCode);
    }

    [Fact]
    public async Task A_business_id_supplied_by_the_client_on_upload_is_ignored()
    {
        var (a, b) = await TwoBusinessesAsync();

        // B tries to plant an image into A's media library by naming A in the form and the query string.
        var planted = await UploadAsync(b, TestImages.Png(320, 240), extraForm: new Dictionary<string, string>
        {
            ["businessId"] = a.BusinessId.ToString(),
            ["BusinessId"] = a.BusinessId.ToString(),
        }, query: $"?businessId={a.BusinessId}");

        var ownList = await SendAsync(b.Http, HttpMethod.Get, "/api/media");
        Assert.Contains(planted.ToString(), await ownList.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var foreignList = await SendAsync(a.Http, HttpMethod.Get, "/api/media");
        Assert.Equal(HttpStatusCode.OK, foreignList.StatusCode);
        Assert.DoesNotContain(planted.ToString(), await foreignList.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(a.Http, HttpMethod.Get, $"/api/media/{planted}")).StatusCode);
    }

    [Fact]
    public async Task A_business_id_header_does_not_grant_access_to_another_businesss_media()
    {
        var (a, b) = await TwoBusinessesAsync();
        var mediaId = await UploadAsync(a, TestImages.Png(400, 300));

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{mediaId}?businessId={a.BusinessId}");
        req.Headers.Add("X-Business-Id", a.BusinessId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_clients_get_nothing_from_the_authenticated_or_public_media_routes()
    {
        var (a, _) = await TwoBusinessesAsync();
        var mediaId = await UploadAsync(a, TestImages.Png(400, 300));
        using var anon = factory.NewClient();

        foreach (var url in new[] { $"/api/media/{mediaId}", $"/api/media/{mediaId}/file/md", "/api/media", "/api/media/usage" })
        {
            var res = await SendAsync(anon, HttpMethod.Get, url);
            Assert.True(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"{url} returned {(int)res.StatusCode} to an anonymous client.");
        }

        var del = await SendAsync(anon, HttpMethod.Delete, $"/api/media/{mediaId}");
        Assert.False(del.IsSuccessStatusCode, $"Anonymous delete returned {(int)del.StatusCode}.");

        // The public route serves only images attached to a Public, Active design. This one is attached to nothing.
        foreach (var v in Variants)
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(anon, HttpMethod.Get, $"/public/media/{mediaId}/{v}")).StatusCode);

        // Still intact for the owner.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Get, $"/api/media/{mediaId}")).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_member_without_media_permissions_cannot_upload_or_delete()
    {
        var a = await RegisterAsync("media-perm-a");
        await factory.SeedPlanAsync(a.BusinessId, "professional");
        var mediaId = await UploadAsync(a, TestImages.Png(400, 300));

        // A member of the same business whose role grants nothing media-related.
        var (email, _) = await factory.SeedMemberAsync(a.BusinessId, "customers.view");
        var member = factory.NewClient();
        var login = await SendAsync(member, HttpMethod.Post, "/api/auth/login", new { email, password = ApiFactory.DefaultPassword }, await CsrfAsync(member));
        Assert.True(login.IsSuccessStatusCode, $"Member login returned {(int)login.StatusCode}.");
        var csrf = await CsrfAsync(member);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(member, HttpMethod.Get, $"/api/media/{mediaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(member, HttpMethod.Delete, $"/api/media/{mediaId}", csrf: csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadRawAsync(member, csrf, TestImages.Png(320, 240))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(a.Http, HttpMethod.Get, $"/api/media/{mediaId}")).StatusCode);
    }

    // ---- setup ---------------------------------------------------------------------------------------------------------------

    private async Task<(Session A, Session B)> TwoBusinessesAsync()
    {
        var a = await RegisterAsync("media-tenant-a");
        var b = await RegisterAsync("media-tenant-b");
        await factory.SeedPlanAsync(a.BusinessId, "professional");
        await factory.SeedPlanAsync(b.BusinessId, "professional");
        return (a, b);
    }

    private static async Task<Guid> UploadAsync(Session s, byte[] png, Dictionary<string, string>? extraForm = null, string query = "")
    {
        var res = await UploadRawAsync(s.Http, s.Csrf, png, extraForm, query);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> UploadRawAsync(HttpClient http, string csrf, byte[] png, Dictionary<string, string>? extraForm = null, string query = "")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "dress.png");
        form.Add(new StringContent("DesignImage"), "purpose");
        foreach (var (key, value) in extraForm ?? []) form.Add(new StringContent(value), key);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/media" + query) { Content = form };
        req.Headers.Add("X-XSRF-TOKEN", csrf);
        return await http.SendAsync(req);
    }

    // ---- HTTP helpers (same approach as CrossTenantApiTests) -------------------------------------------------------------------

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
