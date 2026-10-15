using Tailoring.Domain.Common;
using Tailoring.Domain.Tenancy;

namespace Tailoring.Domain.Access;

public enum MembershipStatus { Active = 0, Deactivated = 1 }

public class BusinessRole : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>System roles are created with the business. The Owner role cannot be edited.</summary>
    public bool IsSystem { get; set; }
    public List<BusinessRolePermission> Permissions { get; set; } = new();
}

public class BusinessRolePermission : ITenantScoped
{
    public Guid RoleId { get; set; }
    public Guid BusinessId { get; set; }
    public string Permission { get; set; } = "";
}

/// <summary>A user's participation in one business. A user may belong to several businesses.</summary>
public class BusinessMembership : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public bool IsOwner { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public DateTime? DeactivatedAtUtc { get; set; }
    public List<MembershipRole> Roles { get; set; } = new();
    public List<MembershipLocation> Locations { get; set; } = new();
}

/// <summary>A membership can hold several roles; permissions are the union.</summary>
public class MembershipRole : ITenantScoped
{
    public Guid MembershipId { get; set; }
    public Guid RoleId { get; set; }
    public Guid BusinessId { get; set; }
}

public class MembershipLocation : ITenantScoped
{
    public Guid MembershipId { get; set; }
    public Guid LocationId { get; set; }
    public Guid BusinessId { get; set; }
}

public class StaffInvitation : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    /// <summary>SHA-256 of the emailed token. The raw token is never stored.</summary>
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid InvitedByUserId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
    public List<Guid> LocationIds { get; set; } = new();
}
