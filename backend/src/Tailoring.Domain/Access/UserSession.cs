using System.Security.Cryptography;
using System.Text;
using Tailoring.Domain.Common;

namespace Tailoring.Domain.Access;

/// <summary>
/// One signed-in device using bearer tokens (mobile). The refresh token is "{Id:N}.{secret}"; only SHA-256 hashes of the
/// secret are stored. Platform-level, not ITenantScoped: a session belongs to a user, bound to one business at sign-in.
/// </summary>
public class UserSession : Entity
{
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
    public string DeviceName { get; set; } = "";
    public string CurrentTokenHash { get; set; } = "";
    /// <summary>The token that was replaced by the last rotation. Presenting it again means a token was stolen or replayed.</summary>
    public string? PreviousTokenHash { get; set; }
    public DateTime LastUsedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime IdleExpiresAtUtc { get; set; }
    public DateTime AbsoluteExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
    /// <summary>Optimistic concurrency (xmin): two parallel refreshes cannot both rotate the same token.</summary>
    public uint Version { get; set; }
}

public enum RefreshDecision { Rotate, ReuseDetected, Unknown, Expired, Revoked }

/// <summary>Pure rules for refresh-token rotation and reuse detection.</summary>
public static class SessionRules
{
    public static RefreshDecision Decide(UserSession s, string presentedHash, DateTime nowUtc)
    {
        if (s.RevokedAtUtc is not null) return RefreshDecision.Revoked;
        if (s.PreviousTokenHash is not null && Same(s.PreviousTokenHash, presentedHash)) return RefreshDecision.ReuseDetected;
        if (!Same(s.CurrentTokenHash, presentedHash)) return RefreshDecision.Unknown;
        if (nowUtc >= s.AbsoluteExpiresAtUtc || nowUtc >= s.IdleExpiresAtUtc) return RefreshDecision.Expired;
        return RefreshDecision.Rotate;
    }

    /// <summary>Idle window slides on every rotation but can never pass the absolute limit.</summary>
    public static DateTime NextIdleExpiry(DateTime nowUtc, TimeSpan idle, DateTime absoluteExpiryUtc)
    {
        var next = nowUtc + idle;
        return next > absoluteExpiryUtc ? absoluteExpiryUtc : next;
    }

    public static bool TryParseToken(string? token, out Guid sessionId, out string secret)
    {
        sessionId = Guid.Empty; secret = "";
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return false;
        var dot = token.IndexOf('.');
        if (dot != 32) return false;
        if (!Guid.TryParseExact(token[..32], "N", out sessionId)) return false;
        secret = token[33..];
        return secret.Length >= 20;
    }

    private static bool Same(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
