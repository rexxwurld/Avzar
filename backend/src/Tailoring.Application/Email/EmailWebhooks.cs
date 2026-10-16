using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tailoring.Domain.Email;

namespace Tailoring.Application.Email;

public enum WebhookOutcome { Disabled, InvalidSignature, Ignored, UnknownMessage, Applied, NoChange }

/// <summary>Receives delivery reports from the email provider. Authenticated only by the provider's signature.</summary>
public interface IEmailWebhookService
{
    Task<WebhookOutcome> HandleResendAsync(string body, string? svixId, string? svixTimestamp, string? svixSignature, CancellationToken ct = default);
}

/// <summary>Svix webhook signatures (the scheme Resend uses): HMAC-SHA256 over "{id}.{timestamp}.{body}", key = base64 part of "whsec_...".</summary>
public static class SvixSignature
{
    public const string SecretPrefix = "whsec_";
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static bool TryParseSecret(string? secret, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(secret)) return false;
        var s = secret.Trim();
        if (!s.StartsWith(SecretPrefix, StringComparison.Ordinal)) return false;
        try { key = Convert.FromBase64String(s[SecretPrefix.Length..]); }
        catch (FormatException) { return false; }
        return key.Length >= 16;
    }

    public static string Sign(byte[] key, string id, string timestamp, string body)
    {
        using var h = new HMACSHA256(key);
        return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}")));
    }

    /// <summary>True only for a fresh message (within the tolerance, both directions) carrying at least one valid v1 signature.</summary>
    public static bool Verify(byte[] key, string? id, string? timestamp, string? signatureHeader, string body, DateTimeOffset now, TimeSpan tolerance)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 200 || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signatureHeader) || signatureHeader.Length > 2000) return false;
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
        DateTimeOffset sent;
        try { sent = DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((now - sent).Duration() > tolerance) return false;

        var expected = Encoding.ASCII.GetBytes(Sign(key, id, timestamp, body));
        var valid = false;
        foreach (var part in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var comma = part.IndexOf(',');
            if (comma < 0 || part[..comma] != "v1") continue;
            var candidate = Encoding.ASCII.GetBytes(part[(comma + 1)..]);
            if (candidate.Length == expected.Length && CryptographicOperations.FixedTimeEquals(candidate, expected)) valid = true;
        }
        return valid;
    }
}

public sealed record ResendWebhookEvent(string Type, string? EmailId, string? Detail)
{
    public static bool TryParse(string body, out ResendWebhookEvent ev)
    {
        ev = null!;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) return false;
            string? emailId = null, detail = null;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("email_id", out var id) && id.ValueKind == JsonValueKind.String) emailId = id.GetString();
                if (data.TryGetProperty("bounce", out var b) && b.ValueKind == JsonValueKind.Object
                    && b.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) detail = EmailDeliveryRules.Sanitize(m.GetString(), 300);
            }
            ev = new ResendWebhookEvent(t.GetString() ?? "", emailId, detail);
            return true;
        }
        catch (JsonException) { return false; }
    }
}

/// <summary>Pure rules for turning provider events into a stored status.</summary>
public static class EmailDeliveryRules
{
    public static string? StatusFor(string? eventType) => eventType switch
    {
        "email.delivery_delayed" => EmailDeliveryStatus.Delayed,
        "email.delivered" => EmailDeliveryStatus.Delivered,
        "email.bounced" => EmailDeliveryStatus.Bounced,
        "email.complained" => EmailDeliveryStatus.Complained,
        _ => null, // sent, opened, clicked and anything new: not tracked
    };

    private static int Rank(string? status) => status switch
    {
        EmailDeliveryStatus.Delayed => 1,
        EmailDeliveryStatus.Delivered => 2,
        EmailDeliveryStatus.Bounced or EmailDeliveryStatus.Complained => 3,
        _ => 0,
    };

    /// <summary>Events arrive out of order and are retried, so a report only moves the status up: a late "delayed" never hides a delivery, and a bounce is never undone.</summary>
    public static bool ShouldApply(string? current, string incoming) => Rank(incoming) > Rank(current);

    /// <summary>The stored statuses an incoming status may replace (null, meaning "no report yet", is handled by the caller).</summary>
    public static string[] ReplaceableBy(string incoming) =>
        new[] { EmailDeliveryStatus.Delayed, EmailDeliveryStatus.Delivered, EmailDeliveryStatus.Bounced, EmailDeliveryStatus.Complained }
            .Where(s => Rank(s) < Rank(incoming)).ToArray();

    public static string? Sanitize(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return null;
        return clean.Length <= max ? clean : clean[..max];
    }
}
