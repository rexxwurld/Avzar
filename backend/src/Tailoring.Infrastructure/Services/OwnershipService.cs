using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Staff;
using Tailoring.Domain.Access;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class OwnershipService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, UserManager<AppUser> users) : IOwnershipService
{
    public async Task TransferAsync(TransferOwnershipRequest req, CancellationToken ct = default)
    {
        var businessId = tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");
        var actorId = tenant.UserId ?? throw AppException.Unauthorized("no_user", "Not signed in.");

        var actor = await users.FindByIdAsync(actorId.ToString()) is { } found && found.LockoutEnd != DateTimeOffset.MaxValue
            ? found : throw AppException.Unauthorized("unknown_user", "Unknown user.");

        // Re-authenticate before touching anything. Failures count toward the same lockout as sign-in.
        if (await users.IsLockedOutAsync(actor))
            throw new AppException(423, "locked_out", "Too many failed attempts. Try again later.");
        if (!await users.CheckPasswordAsync(actor, req.Password))
        {
            await users.AccessFailedAsync(actor);
            throw AppException.BadRequest("wrong_password", "The password is incorrect.");
        }
        if (actor.TwoFactorEnabled && !await SecondFactor.VerifyAsync(db, users, actor, req.TwoFactorCode, req.RecoveryCode))
        {
            await users.AccessFailedAsync(actor);
            throw AppException.BadRequest("invalid_code", "The authentication code is not correct.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Everything below is read AFTER the lock, so two simultaneous transfers cannot both see "I am the owner".
        await Locks.AcquireAsync(db, $"ownership:{businessId:N}", ct);

        var current = await db.Memberships.Include(m => m.Roles).FirstOrDefaultAsync(m => m.UserId == actorId, ct);
        if (current is null || current.Status != MembershipStatus.Active || !current.IsOwner)
            throw AppException.Forbidden("owner_required", "Only the business owner can transfer ownership.");

        // The tenant query filter means another business's membership id simply resolves to "not found".
        var target = await db.Memberships.Include(m => m.Roles).FirstOrDefaultAsync(m => m.Id == req.NewOwnerMembershipId, ct)
            ?? throw AppException.NotFound("staff_not_found", "Staff member not found.");
        if (target.Id == current.Id)
            throw AppException.BadRequest("same_person", "Choose someone other than yourself.");
        if (target.IsOwner)
            throw AppException.Conflict("already_owner", "That person is already the owner.");
        if (target.Status != MembershipStatus.Active)
            throw AppException.BadRequest("target_inactive", "Reactivate this staff member before making them the owner.");
        if (await users.FindByIdAsync(target.UserId.ToString()) is not { } targetUser || targetUser.LockoutEnd == DateTimeOffset.MaxValue)
            throw AppException.BadRequest("target_unavailable", "That account is no longer available.");

        var systemRoles = await db.BusinessRoles.Where(r => r.IsSystem && (r.Name == DefaultRoles.Owner || r.Name == DefaultRoles.Manager)).ToListAsync(ct);
        var ownerRole = systemRoles.SingleOrDefault(r => r.Name == DefaultRoles.Owner)
            ?? throw new InvalidOperationException("The Owner role is missing for this business.");
        var managerRole = systemRoles.SingleOrDefault(r => r.Name == DefaultRoles.Manager);

        target.IsOwner = true;
        if (target.Roles.All(r => r.RoleId != ownerRole.Id))
            db.MembershipRoles.Add(new MembershipRole { BusinessId = businessId, MembershipId = target.Id, RoleId = ownerRole.Id });

        // The previous owner keeps working as a Manager (everything except business settings and role editing).
        current.IsOwner = false;
        db.MembershipRoles.RemoveRange(current.Roles.Where(r => r.RoleId == ownerRole.Id).ToList());
        if (managerRole is not null && current.Roles.All(r => r.RoleId != managerRole.Id))
            db.MembershipRoles.Add(new MembershipRole { BusinessId = businessId, MembershipId = current.Id, RoleId = managerRole.Id });

        audit.Record(businessId, actorId, "business.ownership_transferred", nameof(BusinessMembership), target.Id,
            new { fromMembershipId = current.Id, toMembershipId = target.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
