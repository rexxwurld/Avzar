using Tailoring.Domain.Common;
namespace Tailoring.Domain.CustomerPortal;

/// <summary>Expiring, revocable magic-link grant. Only a SHA-256 token hash is persisted.</summary>
public sealed class CustomerPortalGrant : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid CustomerId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public int UseCount { get; set; }
}
