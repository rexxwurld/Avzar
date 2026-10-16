using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace Avzar.Mobile;
public sealed class BusinessSelectionRequiredException(IReadOnlyList<MobileBusinessChoice> choices) : Exception("Choose a business to continue.") { public IReadOnlyList<MobileBusinessChoice> Choices { get; } = choices; }

public sealed class AvzarApi
{
    // Set AVZAR_API_URL in the platform build configuration; never ship a real production API URL hard-coded into debug builds.
    private static readonly string BaseUrl = (Environment.GetEnvironmentVariable("AVZAR_API_URL") ?? "https://avzar-api.onrender.com").TrimEnd('/');
    private readonly HttpClient _http = new() { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(20) };
    private const string AccessKey = "avzar.mobile.access"; private const string RefreshKey = "avzar.mobile.refresh"; private const string ExpiryKey = "avzar.mobile.access-expiry";
    public async Task<MobileTokens> SignInAsync(string email, string password, Guid? businessId = null, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("/api/auth/token", new { email, password, businessId, deviceName = DeviceInfo.Name }, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                try { using var doc=JsonDocument.Parse(body); if(doc.RootElement.TryGetProperty("code",out var code)&&code.GetString()=="choose_business"&&doc.RootElement.TryGetProperty("choices",out var choices)) throw new BusinessSelectionRequiredException(choices.Deserialize<List<MobileBusinessChoice>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??new()); } catch(JsonException) { }
            }
            throw new InvalidOperationException(await Error(response, ct));
        }
        var result = await response.Content.ReadFromJsonAsync<MobileTokens>(cancellationToken: ct) ?? throw new InvalidOperationException("Empty sign-in response.");
        await StoreTokens(result); return result;
    }
    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        await RefreshIfNeeded(ct); using var req = new HttpRequestMessage(HttpMethod.Get, path); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await SecureStorage.Default.GetAsync(AccessKey));
        using var response = await _http.SendAsync(req, ct); if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await Error(response, ct));
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new InvalidOperationException("Empty API response.");
    }
    public async Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        await RefreshIfNeeded(ct); using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await SecureStorage.Default.GetAsync(AccessKey));
        using var response = await _http.SendAsync(req, ct); if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await Error(response, ct));
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new InvalidOperationException("Empty API response.");
    }
    public async Task SignOutAsync(){var refresh=await SecureStorage.Default.GetAsync(RefreshKey);if(!string.IsNullOrWhiteSpace(refresh)){try{await _http.PostAsJsonAsync("/api/auth/token/revoke",new{refreshToken=refresh});}catch{}}SecureStorage.Default.Remove(AccessKey);SecureStorage.Default.Remove(RefreshKey);SecureStorage.Default.Remove(ExpiryKey);SecureStorage.Default.Remove("avzar.mobile.my-tasks-cache");}
    private async Task RefreshIfNeeded(CancellationToken ct)
    {
        var access=await SecureStorage.Default.GetAsync(AccessKey);if(string.IsNullOrWhiteSpace(access))throw new InvalidOperationException("Sign in to continue.");
        var rawExpiry=await SecureStorage.Default.GetAsync(ExpiryKey);
        if(!DateTime.TryParse(rawExpiry,null,System.Globalization.DateTimeStyles.RoundtripKind,out var expiry)||expiry.ToUniversalTime()>DateTime.UtcNow.AddMinutes(1))return;
        var refresh=await SecureStorage.Default.GetAsync(RefreshKey);if(string.IsNullOrWhiteSpace(refresh))throw new InvalidOperationException("Your session expired. Sign in again.");
        using var response=await _http.PostAsJsonAsync("/api/auth/token/refresh",new{refreshToken=refresh},ct);
        if(!response.IsSuccessStatusCode){SecureStorage.Default.Remove(AccessKey);SecureStorage.Default.Remove(RefreshKey);SecureStorage.Default.Remove(ExpiryKey);SecureStorage.Default.Remove("avzar.mobile.my-tasks-cache");SecureStorage.Default.Remove(ExpiryKey);throw new InvalidOperationException("Your session expired. Sign in again.");}
        var tokens=await response.Content.ReadFromJsonAsync<MobileTokens>(cancellationToken:ct)??throw new InvalidOperationException("Empty token refresh response.");await StoreTokens(tokens);
    }
    private static async Task StoreTokens(MobileTokens tokens){await SecureStorage.Default.SetAsync(AccessKey,tokens.AccessToken);await SecureStorage.Default.SetAsync(RefreshKey,tokens.RefreshToken);await SecureStorage.Default.SetAsync(ExpiryKey,tokens.AccessExpiresAtUtc.ToUniversalTime().ToString("O"));}
    private static async Task<string> Error(HttpResponseMessage response,CancellationToken ct){var text=await response.Content.ReadAsStringAsync(ct);try{using var doc=JsonDocument.Parse(text);if(doc.RootElement.TryGetProperty("detail",out var d))return d.GetString()??$"API returned {(int)response.StatusCode}.";if(doc.RootElement.TryGetProperty("title",out var t))return t.GetString()??$"API returned {(int)response.StatusCode}.";}catch{}return $"API returned {(int)response.StatusCode}. Check your connection and account permissions.";}
}
