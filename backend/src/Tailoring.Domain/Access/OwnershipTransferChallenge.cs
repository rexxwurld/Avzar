using Tailoring.Domain.Common;

namespace Tailoring.Domain.Access;

/// <summary>Short-lived, single-use email confirmation for a business ownership transfer. Only a token hash is stored.</summary>
public sealed class OwnershipTransferChallenge : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid RequestingUserId { get; set; }
    public Guid NewOwnerMembershipId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}
