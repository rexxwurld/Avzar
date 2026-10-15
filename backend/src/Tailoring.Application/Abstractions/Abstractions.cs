namespace Tailoring.Application.Abstractions;

public static class TenantClaims
{
    public const string BusinessId = "business_id";
}

/// <summary>Who is calling and for which business. Populated from the authenticated session only.</summary>
public interface ITenantContext
{
    Guid? UserId { get; }
    Guid? BusinessId { get; }
}

public interface IEmailSender
{
    /// <summary>Plain-text convenience. Queues the message durably; it is delivered (and retried) in the background.</summary>
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);

    /// <summary>Queues a message (optionally with an HTML part). Throws AppException invalid_email for an unusable address.</summary>
    Task QueueAsync(Tailoring.Application.Email.EmailRequest request, CancellationToken ct = default);
}

/// <summary>Adds an audit row to the current unit of work; the caller's SaveChanges persists it atomically.</summary>
public interface IAuditLogger
{
    void Record(Guid businessId, Guid? actorUserId, string action, string entityType, Guid? entityId, object? details = null);
}

public sealed record MembershipAccess(bool IsOwner, IReadOnlySet<string> Permissions);

public interface IPermissionService
{
    /// <summary>Returns null when the user has no ACTIVE membership in the business.</summary>
    Task<MembershipAccess?> GetAccessAsync(Guid userId, Guid businessId, CancellationToken ct = default);
}

public sealed class AppOptions
{
    public string WebBaseUrl { get; set; } = "http://localhost:3000";
    public int InvitationDays { get; set; } = 7;
}

/// <summary>Account security settings (configuration section "Auth").</summary>
public sealed class AuthOptions
{
    /// <summary>Shown in authenticator apps.</summary>
    public string ProductName { get; set; } = "Avzar";
    /// <summary>When true, password sign-in is refused until the email address is confirmed. Off by default so existing accounts are not locked out.</summary>
    public bool RequireVerifiedEmail { get; set; }
    public int EmailVerificationHours { get; set; } = 24;
    public int PasswordResetMinutes { get; set; } = 30;
    /// <summary>Minimum gap between two verification or reset emails to the same address.</summary>
    public int EmailThrottleSeconds { get; set; } = 120;

    // Bearer tokens for the mobile app. Disabled unless JwtSigningKey (at least 32 characters) is configured.
    public string? JwtSigningKey { get; set; }
    public string JwtIssuer { get; set; } = "avzar-api";
    public string JwtAudience { get; set; } = "avzar-mobile";
    public int AccessTokenMinutes { get; set; } = 10;
    public int RefreshIdleDays { get; set; } = 30;
    public int RefreshAbsoluteDays { get; set; } = 90;
    public int MaxSessionsPerUser { get; set; } = 10;

    public bool MobileTokensEnabled => !string.IsNullOrWhiteSpace(JwtSigningKey) && JwtSigningKey.Length >= 32;
}
