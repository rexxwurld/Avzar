using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tailoring.Api.Security;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Locations;
using Tailoring.Application.Roles;
using Tailoring.Application.Staff;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

[ApiController, Route("api/staff")]
public class StaffController(IStaffService staff) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.StaffView)]
    public async Task<IReadOnlyList<StaffMemberDto>> List(CancellationToken ct) => await staff.ListAsync(ct);

    [HttpPost("invitations"), HasPermission(Permissions.StaffManage)]
    public async Task<ActionResult<InvitationDto>> Invite(InviteStaffRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await staff.InviteAsync(request, ct));

    [HttpPost("{membershipId:guid}/deactivate"), HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> Deactivate(Guid membershipId, CancellationToken ct)
    { await staff.DeactivateAsync(membershipId, ct); return NoContent(); }

    [HttpPost("{membershipId:guid}/reactivate"), HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> Reactivate(Guid membershipId, CancellationToken ct)
    { await staff.ReactivateAsync(membershipId, ct); return NoContent(); }

    [HttpPut("{membershipId:guid}/roles"), HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> SetRoles(Guid membershipId, SetMembershipRolesRequest request, CancellationToken ct)
    { await staff.SetRolesAsync(membershipId, request, ct); return NoContent(); }

    [HttpPut("{membershipId:guid}/locations"), HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> SetLocations(Guid membershipId, SetMembershipLocationsRequest request, CancellationToken ct)
    { await staff.SetLocationsAsync(membershipId, request, ct); return NoContent(); }
}

[ApiController, Route("api/roles")]
public class RolesController(IRoleService roles) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.StaffView)]
    public async Task<IReadOnlyList<RoleDto>> List(CancellationToken ct) => await roles.ListAsync(ct);

    [HttpGet("permissions"), HasPermission(Permissions.RolesManage)]
    public IReadOnlyList<string> AvailablePermissions() => roles.ListAvailablePermissions();

    [HttpPost, HasPermission(Permissions.RolesManage)]
    public async Task<ActionResult<RoleDto>> Create(CreateRoleRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await roles.CreateAsync(request, ct));

    [HttpPut("{roleId:guid}/permissions"), HasPermission(Permissions.RolesManage)]
    public async Task<RoleDto> SetPermissions(Guid roleId, SetRolePermissionsRequest request, CancellationToken ct)
        => await roles.SetPermissionsAsync(roleId, request, ct);
}

[ApiController, Route("api/locations")]
public class LocationsController(ILocationService locations) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.StaffView)]
    public async Task<IReadOnlyList<LocationDto>> List(CancellationToken ct) => await locations.ListAsync(ct);

    [HttpPost, HasPermission(Permissions.LocationsManage)]
    public async Task<ActionResult<LocationDto>> Create(CreateLocationRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await locations.CreateAsync(request, ct));
}

[ApiController]
public class BillingController(
    IEntitlementService entitlements, IPlanCatalogService plans, ITenantContext tenant, IPermissionService permissions) : ControllerBase
{
    /// <summary>Public: feeds the pricing page from the same database config the backend enforces.</summary>
    [HttpGet("api/plans"), AllowAnonymous]
    public async Task<PlanCatalogDto> Plans(CancellationToken ct) => await plans.GetPublicAsync(ct);

    /// <summary>The caller's effective entitlements and current usage. Any ACTIVE member of the business may read it.</summary>
    [HttpGet("api/billing/entitlements"), Authorize]
    public async Task<TenantEntitlementsDto> Mine(CancellationToken ct)
    {
        var (userId, businessId) = (tenant.UserId, tenant.BusinessId);
        if (userId is null || businessId is null || await permissions.GetAccessAsync(userId.Value, businessId.Value, ct) is null)
            throw AppException.Forbidden("not_a_member", "You are not an active member of this business.");
        return await entitlements.DescribeAsync(businessId.Value, ct);
    }
}
