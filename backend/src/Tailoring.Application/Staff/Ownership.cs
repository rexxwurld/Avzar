using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Staff;

public sealed record TransferOwnershipRequest
{
    /// <summary>The active staff member who becomes the owner.</summary>
    [Required] public Guid NewOwnerMembershipId { get; init; }
    /// <summary>The CURRENT owner's password. Re-entered because a stolen session must not be able to give the business away.</summary>
    [Required] public string Password { get; init; } = "";
    /// <summary>Required when the current owner has two-factor authentication on.</summary>
    public string? TwoFactorCode { get; init; }
    public string? RecoveryCode { get; init; }
}

public interface IOwnershipService
{
    /// <summary>Re-authenticates the current owner and emails a short-lived confirmation link. Does not transfer ownership yet.</summary>
    Task RequestVerificationAsync(TransferOwnershipRequest request, CancellationToken ct = default);
    /// <summary>Consumes a single-use emailed token and performs the transfer atomically.</summary>
    Task ConfirmVerificationAsync(string token, CancellationToken ct = default);
    /// <summary>Internal/service-level transfer operation retained for trusted workflows and tests. Public API uses request/confirm.</summary>
    Task TransferAsync(TransferOwnershipRequest request, CancellationToken ct = default);
}
