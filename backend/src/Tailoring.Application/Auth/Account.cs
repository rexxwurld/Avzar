using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Auth;

public sealed record ConfirmEmailRequest
{
    [Required] public Guid UserId { get; init; }
    [Required, StringLength(2000)] public string Token { get; init; } = "";
}

public sealed record ForgotPasswordRequest
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; init; } = "";
}

public sealed record ResetPasswordRequest
{
    [Required] public Guid UserId { get; init; }
    [Required, StringLength(2000)] public string Token { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 10)] public string NewPassword { get; init; } = "";
}

public sealed record ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 10)] public string NewPassword { get; init; } = "";
}

public sealed record SecuritySettingsDto(bool EmailConfirmed, bool TwoFactorEnabled, int RecoveryCodesLeft, int ActiveSessions);

/// <summary>SharedKey is shown once for manual entry; OtpAuthUri is what a QR code should encode.</summary>
public sealed record TwoFactorSetupDto(string SharedKey, string OtpAuthUri);

public sealed record EnableTwoFactorRequest
{
    [Required, StringLength(12, MinimumLength = 6)] public string Code { get; init; } = "";
}

/// <summary>Shown exactly once. Each code works once.</summary>
public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);

public sealed record DisableTwoFactorRequest
{
    [Required] public string Password { get; init; } = "";
    public string? Code { get; init; }
    public string? RecoveryCode { get; init; }
}

public sealed record DeleteAccountRequest
{
    [Required] public string Password { get; init; } = "";
    public string? TwoFactorCode { get; init; }
    public string? RecoveryCode { get; init; }
}

public sealed record SessionDto(Guid Id, string DeviceName, DateTime CreatedAtUtc, DateTime LastUsedAtUtc, DateTime ExpiresAtUtc);

public sealed record AccountExportDto(
    Guid UserId, string Email, string FullName, bool EmailConfirmed, bool TwoFactorEnabled, DateTime CreatedAtUtc,
    IReadOnlyList<AccountMembershipExportDto> Memberships, IReadOnlyList<SessionDto> Sessions, DateTime ExportedAtUtc);

public sealed record AccountMembershipExportDto(Guid BusinessId, string BusinessName, string DisplayName, bool IsOwner, string Status, IReadOnlyList<string> Roles);

public sealed record TokenLoginRequest
{
    [Required, EmailAddress] public string Email { get; init; } = "";
    [Required] public string Password { get; init; } = "";
    public Guid? BusinessId { get; init; }
    public string? TwoFactorCode { get; init; }
    public string? RecoveryCode { get; init; }
    [StringLength(80)] public string? DeviceName { get; init; }
}

public sealed record RefreshTokenRequest
{
    [Required, StringLength(200)] public string RefreshToken { get; init; } = "";
}

public sealed record TokenPair(Guid SessionId, string RefreshToken, DateTime RefreshExpiresAtUtc);
public sealed record RefreshResult(SessionIdentity Identity, TokenPair Tokens);

public interface IAccountService
{
    Task SendEmailVerificationAsync(Guid userId, CancellationToken ct = default);
    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default);
    /// <summary>Always completes the same way whether or not the address has an account (no account enumeration).</summary>
    Task RequestPasswordResetAsync(string email, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<SecuritySettingsDto> GetSecurityAsync(Guid userId, CancellationToken ct = default);
    Task<TwoFactorSetupDto> BeginTwoFactorSetupAsync(Guid userId, CancellationToken ct = default);
    Task<RecoveryCodesDto> EnableTwoFactorAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken ct = default);
    Task DisableTwoFactorAsync(Guid userId, DisableTwoFactorRequest request, CancellationToken ct = default);
    Task<AccountExportDto> ExportAsync(Guid userId, CancellationToken ct = default);
    Task DeleteAccountAsync(Guid userId, DeleteAccountRequest request, CancellationToken ct = default);
}

public interface ISessionService
{
    Task<TokenPair> CreateAsync(Guid userId, Guid businessId, string? deviceName, CancellationToken ct = default);
    /// <summary>Rotates the refresh token. Presenting an already-rotated token revokes the whole session.</summary>
    Task<RefreshResult> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task RevokeAsync(string refreshToken, CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> ListAsync(Guid userId, CancellationToken ct = default);
    Task RevokeByIdAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
    Task RevokeAllAsync(Guid userId, string reason, CancellationToken ct = default);
    /// <summary>Used on every bearer request so revocation takes effect immediately.</summary>
    Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken ct = default);
}
