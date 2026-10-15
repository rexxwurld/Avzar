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
    /// <summary>The signed-in owner hands the business to another active staff member and steps down to the Manager role.</summary>
    Task TransferAsync(TransferOwnershipRequest request, CancellationToken ct = default);
}
