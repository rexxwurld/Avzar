using System.Net.Http.Json;
using DnsClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailoring.Application.Storefront;

namespace Tailoring.Infrastructure.Services;

/// <summary>Real DNS TXT lookups via the DnsClient package. Not exercised by tests (they use a fake): needs a real domain to prove.</summary>
public sealed class DnsClientTxtResolver : IDnsTxtResolver
{
    private readonly LookupClient _client = new(new LookupClientOptions { Timeout = TimeSpan.FromSeconds(5), UseCache = false });

    public async Task<IReadOnlyList<string>> LookupTxtAsync(string name, CancellationToken ct = default)
    {
        var result = await _client.QueryAsync(name, QueryType.TXT, cancellationToken: ct);
        return result.Answers.TxtRecords().Select(r => string.Concat(r.Text)).ToList();
    }
}

/// <summary>
/// Cloudflare Turnstile. Only active when Storefront:CaptchaSecretKey is set; otherwise IsConfigured is false and the
/// honeypot plus rate limits are the only guards. Not tested here: needs a real secret and a browser widget.
/// </summary>
public sealed class TurnstileCaptchaVerifier(HttpClient http, IOptions<StorefrontOptions> options, ILogger<TurnstileCaptchaVerifier> log) : ICaptchaVerifier
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.CaptchaSecretKey);

    public async Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken ct = default)
    {
        if (!IsConfigured) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var form = new Dictionary<string, string> { ["secret"] = options.Value.CaptchaSecretKey!, ["response"] = token };
            if (!string.IsNullOrWhiteSpace(clientIp)) form["remoteip"] = clientIp;
            using var response = await http.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", new FormUrlEncodedContent(form), ct);
            if (!response.IsSuccessStatusCode) return false;
            var body = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken: ct);
            return body?.Success == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Captcha verification could not be completed");
            return false; // fail closed: a broken captcha provider must not become an open door
        }
    }

    private sealed record TurnstileResponse(bool Success);
}
