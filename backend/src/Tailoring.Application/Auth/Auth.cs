using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Auth;

public sealed record RegisterBusinessRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string BusinessName { get; init; } = "";
    [Required, StringLength(120, MinimumLength = 2)] public string OwnerName { get; init; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 10)] public string Password { get; init; } = "";
    [Required, StringLength(64)] public string TimeZoneId { get; init; } = "UTC";
    [Required, StringLength(3, MinimumLength = 3)] public string CurrencyCode { get; init; } = "USD";
    [StringLength(120)] public string? LocationName { get; init; }
}

public sealed record LoginRequest
{
    [Required, EmailAddress] public string Email { get; init; } = "";
    [Required] public string Password { get; init; } = "";
    /// <summary>Required only when the user belongs to more than one business.</summary>
    public Guid? BusinessId { get; init; }
    /// <summary>Authenticator code or recovery code. Required only when two-factor is enabled on the account.</summary>
    public string? TwoFactorCode { get; init; }
    public string? RecoveryCode { get; init; }
}

public sealed record AcceptInvitationRequest
{
    [Required] public string Token { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 10)] public string Password { get; init; } = "";
}

public sealed record SessionIdentity(Guid UserId, Guid BusinessId);
public sealed record BusinessChoiceDto(Guid BusinessId, string BusinessName);

/// <summary>Either a session (Session != null) or a list of businesses to choose from.</summary>
public sealed record AuthResult(SessionIdentity? Session, IReadOnlyList<BusinessChoiceDto> Choices);

public sealed record CurrentUserDto(
    Guid UserId, string Email, string FullName,
    Guid BusinessId, string BusinessName, string TimeZoneId, string CurrencyCode,
    bool IsOwner, IReadOnlyList<string> Permissions);

public interface IAuthService
{
    Task<SessionIdentity> RegisterBusinessAsync(RegisterBusinessRequest request, CancellationToken ct = default);
    Task<AuthResult> AuthenticateAsync(LoginRequest request, CancellationToken ct = default);
    Task<SessionIdentity> AcceptInvitationAsync(AcceptInvitationRequest request, CancellationToken ct = default);
    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, Guid businessId, CancellationToken ct = default);
}
