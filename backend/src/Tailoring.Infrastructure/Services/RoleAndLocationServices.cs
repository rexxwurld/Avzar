using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Locations;
using Tailoring.Application.Roles;
using Tailoring.Domain.Access;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class RoleService(AppDbContext db, ITenantContext tenant, IAuditLogger audit) : IRoleService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public IReadOnlyList<string> ListAvailablePermissions() => Permissions.All;

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var roles = await db.BusinessRoles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync(ct);
        return roles.Select(ToDto).ToList();
    }

    public async Task<RoleDto> CreateAsync(CreateRoleRequest req, CancellationToken ct = default)
    {
        var name = req.Name.Trim();
        var perms = ValidatePermissions(req.Permissions);
        if (await db.BusinessRoles.AnyAsync(r => r.Name == name, ct))
            throw AppException.Conflict("role_exists", "A role with this name already exists.");

        var role = new BusinessRole { BusinessId = BusinessId, Name = name, IsSystem = false };
        role.Permissions = perms.Select(p => new BusinessRolePermission { RoleId = role.Id, BusinessId = BusinessId, Permission = p }).ToList();
        db.BusinessRoles.Add(role);
        audit.Record(BusinessId, tenant.UserId, "role.created", nameof(BusinessRole), role.Id, new { name, perms });
        await db.SaveChangesAsync(ct);
        return ToDto(role);
    }

    public async Task<RoleDto> SetPermissionsAsync(Guid roleId, SetRolePermissionsRequest req, CancellationToken ct = default)
    {
        var role = await db.BusinessRoles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == roleId, ct)
            ?? throw AppException.NotFound("role_not_found", "Role not found.");
        if (role.IsSystem && role.Name == DefaultRoles.Owner)
            throw AppException.Forbidden("owner_role_protected", "The Owner role cannot be edited.");

        var perms = ValidatePermissions(req.Permissions);
        var before = role.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList();

        db.RolePermissions.RemoveRange(role.Permissions.Where(p => !perms.Contains(p.Permission)).ToList());
        foreach (var p in perms.Where(p => role.Permissions.All(x => x.Permission != p)))
            db.RolePermissions.Add(new BusinessRolePermission { RoleId = role.Id, BusinessId = BusinessId, Permission = p });

        audit.Record(BusinessId, tenant.UserId, "role.permissions_changed", nameof(BusinessRole), role.Id, new { before, after = perms });
        await db.SaveChangesAsync(ct);

        await db.Entry(role).Collection(r => r.Permissions).LoadAsync(ct);
        return ToDto(role);
    }

    private static List<string> ValidatePermissions(IEnumerable<string> input)
    {
        var list = input.Select(p => p.Trim()).Distinct().ToList();
        var bad = list.Where(p => !Permissions.IsValid(p)).ToList();
        if (bad.Count > 0)
            throw AppException.BadRequest("unknown_permission", $"Unknown permission(s): {string.Join(", ", bad)}");
        return list;
    }

    private static RoleDto ToDto(BusinessRole r)
        => new(r.Id, r.Name, r.IsSystem, r.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList());
}

public sealed class LocationService(AppDbContext db, ITenantContext tenant, IAuditLogger audit) : ILocationService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<IReadOnlyList<LocationDto>> ListAsync(CancellationToken ct = default)
        => await db.Locations.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.Name, l.Address, l.IsActive)).ToListAsync(ct);

    public async Task<LocationDto> CreateAsync(CreateLocationRequest req, CancellationToken ct = default)
    {
        var name = req.Name.Trim();
        if (await db.Locations.AnyAsync(l => l.Name == name, ct))
            throw AppException.Conflict("location_exists", "A location with this name already exists.");

        var loc = new BusinessLocation { BusinessId = BusinessId, Name = name, Address = req.Address?.Trim() };
        db.Locations.Add(loc);
        audit.Record(BusinessId, tenant.UserId, "location.created", nameof(BusinessLocation), loc.Id, new { name });
        await db.SaveChangesAsync(ct);
        return new LocationDto(loc.Id, loc.Name, loc.Address, loc.IsActive);
    }
}
