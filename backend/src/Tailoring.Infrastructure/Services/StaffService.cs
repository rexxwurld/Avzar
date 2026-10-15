using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Staff;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class StaffService(
    AppDbContext db, ITenantContext tenant, IAuditLogger audit,
    IEmailSender email, IOptions<AppOptions> options, IEntitlementService entitlements) : IStaffService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");
    private Guid ActorId => tenant.UserId ?? throw AppException.Unauthorized("no_user", "Not signed in.");

    public async Task<IReadOnlyList<StaffMemberDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await (
            from m in db.Memberships.AsNoTracking()
            join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            orderby m.DisplayName
            select new
            {
                m.Id, m.UserId, m.DisplayName, u.Email, m.IsOwner, m.Status,
                RoleIds = m.Roles.Select(r => r.RoleId).ToList(),
                LocationIds = m.Locations.Select(l => l.LocationId).ToList()
            }).ToListAsync(ct);

        return rows.Select(r => new StaffMemberDto(r.Id, r.UserId, r.DisplayName, r.Email ?? "",
            r.IsOwner, r.Status.ToString(), r.RoleIds, r.LocationIds)).ToList();
    }

    public async Task<InvitationDto> InviteAsync(InviteStaffRequest req, CancellationToken ct = default)
    {
        var emailAddr = req.Email.Trim().ToLowerInvariant();
        var roleIds = req.RoleIds.Distinct().ToList();
        var locationIds = req.LocationIds.Distinct().ToList();
        await ValidateRolesAsync(roleIds, ct);
        await ValidateLocationsAsync(locationIds, ct);

        var alreadyMember = await (
            from m in db.Memberships
            join u in db.Users on m.UserId equals u.Id
            where u.NormalizedEmail == emailAddr.ToUpperInvariant()
            select m.Id).AnyAsync(ct);
        if (alreadyMember)
            throw AppException.Conflict("already_member", "This person is already on your staff.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Seats = active staff + pending, unexpired invitations (excluding one for this same email, which is replaced below).
        await entitlements.EnsureBelowLimitAsync(BusinessId, EntitlementKeys.UsersMax, async c =>
            await db.Memberships.LongCountAsync(m => m.Status == MembershipStatus.Active, c)
            + await db.Invitations.LongCountAsync(i => i.Email != emailAddr && i.AcceptedAtUtc == null
                && i.RevokedAtUtc == null && i.ExpiresAtUtc > DateTime.UtcNow, c), ct);

        // Replace any pending invitation for the same email.
        var pending = await db.Invitations
            .Where(i => i.Email == emailAddr && i.AcceptedAtUtc == null && i.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var p in pending) p.RevokedAtUtc = DateTime.UtcNow;

        var token = TokenHasher.NewToken();
        var invite = new StaffInvitation
        {
            BusinessId = BusinessId, Email = emailAddr, FullName = req.FullName.Trim(),
            TokenHash = TokenHasher.Hash(token),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(options.Value.InvitationDays),
            InvitedByUserId = ActorId, RoleIds = roleIds, LocationIds = locationIds
        };
        db.Invitations.Add(invite);
        audit.Record(BusinessId, ActorId, "staff.invited", nameof(StaffInvitation), invite.Id, new { emailAddr, roleIds });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // The invitation is committed first; the email goes to the durable outbox, so a provider outage never loses it or fails the request.
        var link = $"{options.Value.WebBaseUrl.TrimEnd('/')}/accept-invite?token={token}";
        var mail = Tailoring.Application.Email.EmailTemplates.StaffInvitation(invite.FullName, link, options.Value.InvitationDays);
        await email.QueueAsync(new Tailoring.Application.Email.EmailRequest(emailAddr, mail.Subject, mail.Text, mail.Html, BusinessId, "staff_invitation"), ct);

        return new InvitationDto(invite.Id, invite.Email, invite.ExpiresAtUtc);
    }

    public async Task DeactivateAsync(Guid membershipId, CancellationToken ct = default)
    {
        var m = await LoadAsync(membershipId, ct);
        if (m.IsOwner) throw AppException.Forbidden("owner_protected", "The owner cannot be deactivated.");
        if (m.UserId == ActorId) throw AppException.Forbidden("self_deactivation", "You cannot deactivate yourself.");
        if (m.Status == MembershipStatus.Deactivated) return;

        m.Status = MembershipStatus.Deactivated;
        m.DeactivatedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, ActorId, "staff.deactivated", nameof(BusinessMembership), m.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReactivateAsync(Guid membershipId, CancellationToken ct = default)
    {
        var m = await LoadAsync(membershipId, ct);
        if (m.Status == MembershipStatus.Active) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await entitlements.EnsureBelowLimitAsync(BusinessId, EntitlementKeys.UsersMax, async c =>
            await db.Memberships.LongCountAsync(x => x.Status == MembershipStatus.Active, c)
            + await db.Invitations.LongCountAsync(i => i.AcceptedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > DateTime.UtcNow, c), ct);

        m.Status = MembershipStatus.Active;
        m.DeactivatedAtUtc = null;
        audit.Record(BusinessId, ActorId, "staff.reactivated", nameof(BusinessMembership), m.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task SetRolesAsync(Guid membershipId, SetMembershipRolesRequest req, CancellationToken ct = default)
    {
        var m = await LoadAsync(membershipId, ct, includeAssignments: true);
        if (m.IsOwner) throw AppException.Forbidden("owner_protected", "The owner's roles cannot be changed.");

        var roleIds = req.RoleIds.Distinct().ToList();
        await ValidateRolesAsync(roleIds, ct);

        var before = m.Roles.Select(r => r.RoleId).ToList();
        db.MembershipRoles.RemoveRange(m.Roles.Where(r => !roleIds.Contains(r.RoleId)).ToList());
        foreach (var rid in roleIds.Where(rid => m.Roles.All(r => r.RoleId != rid)))
            db.MembershipRoles.Add(new MembershipRole { BusinessId = BusinessId, MembershipId = m.Id, RoleId = rid });

        audit.Record(BusinessId, ActorId, "staff.roles_changed", nameof(BusinessMembership), m.Id, new { before, after = roleIds });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetLocationsAsync(Guid membershipId, SetMembershipLocationsRequest req, CancellationToken ct = default)
    {
        var m = await LoadAsync(membershipId, ct, includeAssignments: true);
        var locationIds = req.LocationIds.Distinct().ToList();
        await ValidateLocationsAsync(locationIds, ct);

        var before = m.Locations.Select(l => l.LocationId).ToList();
        db.MembershipLocations.RemoveRange(m.Locations.Where(l => !locationIds.Contains(l.LocationId)).ToList());
        foreach (var lid in locationIds.Where(lid => m.Locations.All(l => l.LocationId != lid)))
            db.MembershipLocations.Add(new MembershipLocation { BusinessId = BusinessId, MembershipId = m.Id, LocationId = lid });

        audit.Record(BusinessId, ActorId, "staff.locations_changed", nameof(BusinessMembership), m.Id, new { before, after = locationIds });
        await db.SaveChangesAsync(ct);
    }

    private async Task<BusinessMembership> LoadAsync(Guid id, CancellationToken ct, bool includeAssignments = false)
    {
        var q = db.Memberships.AsQueryable();
        if (includeAssignments) q = q.Include(x => x.Roles).Include(x => x.Locations);
        // Tenant query filter applies: another business's id simply resolves to "not found".
        return await q.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound("staff_not_found", "Staff member not found.");
    }

    private async Task ValidateRolesAsync(List<Guid> roleIds, CancellationToken ct)
    {
        var roles = await db.BusinessRoles.Where(r => roleIds.Contains(r.Id)).Select(r => new { r.Id, r.Name, r.IsSystem }).ToListAsync(ct);
        if (roles.Count != roleIds.Count)
            throw AppException.BadRequest("unknown_role", "One or more roles do not exist.");
        if (roles.Any(r => r.IsSystem && r.Name == DefaultRoles.Owner))
            throw AppException.Forbidden("owner_role_protected", "The Owner role cannot be assigned.");
    }

    private async Task ValidateLocationsAsync(List<Guid> locationIds, CancellationToken ct)
    {
        if (locationIds.Count == 0) return;
        var count = await db.Locations.CountAsync(l => locationIds.Contains(l.Id) && l.IsActive, ct);
        if (count != locationIds.Count)
            throw AppException.BadRequest("unknown_location", "One or more locations do not exist or are inactive.");
    }
}
