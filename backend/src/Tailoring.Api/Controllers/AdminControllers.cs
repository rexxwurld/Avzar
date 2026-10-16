using Microsoft.EntityFrameworkCore;
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

[ApiController, Route("api/audit")]
public class AuditController(Tailoring.Application.Audit.IAuditReadService audit) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.AuditView)]
    public async Task<PagedResult<Tailoring.Application.Audit.AuditEntryDto>> List(
        [FromQuery] Tailoring.Application.Audit.AuditListQuery query, CancellationToken ct)
        => await audit.ListAsync(query, ct);
}

[ApiController, Route("api/business/ownership-transfer")]
public class OwnershipController(IOwnershipService ownership) : ControllerBase
{
    /// <summary>Owner only (enforced in the service, after re-entering the password and any second factor).</summary>
    [HttpPost, Authorize]
    public async Task<IActionResult> Transfer(TransferOwnershipRequest request, CancellationToken ct)
    { await ownership.TransferAsync(request, ct); return NoContent(); }
}

/// <summary>Global plan controls are restricted to emails explicitly configured in PlatformAdmin:Emails; business-owner permissions do not grant platform administration.</summary>
[ApiController, Route("api/platform-admin/plans"), Authorize]
public sealed class PlatformPlanAdminController(Microsoft.EntityFrameworkCore.DbContextOptions<Tailoring.Infrastructure.Persistence.AppDbContext> options, Microsoft.Extensions.Configuration.IConfiguration config) : ControllerBase
{
    private Tailoring.Infrastructure.Persistence.AppDbContext CreateContext()
    {
        var allowed = (config.GetSection("PlatformAdmin:Emails").Get<string[]>() ?? Array.Empty<string>()).Select(x => x.Trim()).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.Identity?.Name;
        if (email is null || !allowed.Contains(email)) throw Tailoring.Application.Common.AppException.Forbidden("platform_admin_required", "Platform administrator access is required.");
        return new Tailoring.Infrastructure.Persistence.AppDbContext(options, new PlatformAdminTenantContext());
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        await using var db = CreateContext();
        var plans = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.Set<Tailoring.Domain.Billing.Plan>().Include(x => x.Entitlements).OrderBy(x => x.SortOrder), ct);
        return Ok(plans.Select(p => new { p.Id, p.Code, p.Name, p.Description, p.IsActive, p.SortOrder, p.CurrencyCode, p.MonthlyPrice, p.AnnualPrice, Entitlements = p.Entitlements.ToDictionary(x => x.Key, x => x.Value) }));
    }

    [HttpPut("{code}")]
    public async Task<IActionResult> Update(string code, PlatformPlanUpdateRequest request, CancellationToken ct)
    {
        await using var db = CreateContext();
        var plan = await db.Set<Tailoring.Domain.Billing.Plan>().Include(x => x.Entitlements).SingleOrDefaultAsync(x => x.Code == code, ct)
            ?? throw Tailoring.Application.Common.AppException.NotFound("plan_not_found", "Plan was not found.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80 || request.MonthlyPrice < 0 || request.AnnualPrice < 0 || request.CurrencyCode.Length != 3)
            throw Tailoring.Application.Common.AppException.BadRequest("invalid_plan", "Name, currency and non-negative prices are required.");
        foreach (var pair in request.Entitlements)
        {
            var error = Tailoring.Domain.Billing.EntitlementCatalog.Validate(pair.Key, pair.Value);
            if (error is not null) throw Tailoring.Application.Common.AppException.BadRequest("invalid_entitlement", error);
            var existing = plan.Entitlements.SingleOrDefault(x => x.Key == pair.Key);
            if (existing is null) plan.Entitlements.Add(new Tailoring.Domain.Billing.PlanEntitlement { PlanId = plan.Id, Key = pair.Key, Value = pair.Value });
            else existing.Value = pair.Value;
        }
        plan.Name = request.Name.Trim(); plan.Description = request.Description?.Trim(); plan.IsActive = request.IsActive; plan.SortOrder = request.SortOrder; plan.CurrencyCode = request.CurrencyCode.ToUpperInvariant(); plan.MonthlyPrice = request.MonthlyPrice; plan.AnnualPrice = request.AnnualPrice;
        await db.SaveChangesAsync(ct); return NoContent();
    }
}

public sealed record PlatformPlanUpdateRequest(string Name, string? Description, bool IsActive, int SortOrder, string CurrencyCode, decimal MonthlyPrice, decimal? AnnualPrice, Dictionary<string,string> Entitlements);

internal sealed class PlatformAdminTenantContext : Tailoring.Application.Abstractions.ITenantContext
{
    public Guid? UserId => null;
    public Guid? BusinessId => null;
}
