using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Staff;

public sealed record StaffMemberDto(
    Guid MembershipId, Guid UserId, string DisplayName, string Email,
    bool IsOwner, string Status, IReadOnlyList<Guid> RoleIds, IReadOnlyList<Guid> LocationIds);

public sealed record InviteStaffRequest
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; init; } = "";
    [Required, StringLength(120, MinimumLength = 2)] public string FullName { get; init; } = "";
    [Required, MinLength(1)] public List<Guid> RoleIds { get; init; } = new();
    public List<Guid> LocationIds { get; init; } = new();
}

public sealed record InvitationDto(Guid Id, string Email, DateTime ExpiresAtUtc);

public sealed record SetMembershipRolesRequest
{
    [Required, MinLength(1)] public List<Guid> RoleIds { get; init; } = new();
}

public sealed record SetMembershipLocationsRequest
{
    public List<Guid> LocationIds { get; init; } = new();
}

public interface IStaffService
{
    Task<IReadOnlyList<StaffMemberDto>> ListAsync(CancellationToken ct = default);
    Task<InvitationDto> InviteAsync(InviteStaffRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid membershipId, CancellationToken ct = default);
    Task ReactivateAsync(Guid membershipId, CancellationToken ct = default);
    Task SetRolesAsync(Guid membershipId, SetMembershipRolesRequest request, CancellationToken ct = default);
    Task SetLocationsAsync(Guid membershipId, SetMembershipLocationsRequest request, CancellationToken ct = default);
}
