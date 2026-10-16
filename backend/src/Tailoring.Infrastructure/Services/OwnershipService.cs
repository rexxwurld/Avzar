using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Email;
using Tailoring.Application.Staff;
using Tailoring.Domain.Access;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class OwnershipService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, UserManager<AppUser> users, IEmailSender email) : IOwnershipService
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
        var targetUser = await users.FindByIdAsync(target.UserId.ToString());
        if (targetUser is null || targetUser.LockoutEnd == DateTimeOffset.MaxValue)
            throw AppException.BadRequest("target_unavailable", "That account is no longer available.");

        var systemRoles = await db.BusinessRoles.Where(r => r.IsSystem && (r.Name == DefaultRoles.Owner || r.Name == DefaultRoles.Manager)).ToListAsync(ct);
        var ownerRole = systemRoles.SingleOrDefault(r => r.Name == DefaultRoles.Owner)
            ?? throw new InvalidOperationException("The Owner role is missing for this business.");
        var managerRole = systemRoles.SingleOrDefault(r => r.Name == DefaultRoles.Manager);

        // The database allows one owner per business (UX_Memberships_OneOwner) and checks it statement by statement, so the outgoing
        // owner is saved first. Both saves are inside the same transaction: nobody else ever sees a business without an owner.
        // The previous owner keeps working as a Manager (everything except business settings and role editing).
        current.IsOwner = false;
        db.MembershipRoles.RemoveRange(current.Roles.Where(r => r.RoleId == ownerRole.Id).ToList());
        if (managerRole is not null && current.Roles.All(r => r.RoleId != managerRole.Id))
            db.MembershipRoles.Add(new MembershipRole { BusinessId = businessId, MembershipId = current.Id, RoleId = managerRole.Id });
        await db.SaveChangesAsync(ct);

        target.IsOwner = true;
        if (target.Roles.All(r => r.RoleId != ownerRole.Id))
            db.MembershipRoles.Add(new MembershipRole { BusinessId = businessId, MembershipId = target.Id, RoleId = ownerRole.Id });

        audit.Record(businessId, actorId, "business.ownership_transferred", nameof(BusinessMembership), target.Id,
            new { fromMembershipId = current.Id, toMembershipId = target.Id });
        await db.SaveChangesAsync(ct);

        // The notices go to the durable outbox INSIDE the transaction: either the transfer and both notices are saved, or none of them.
        // A provider outage cannot fail the transfer, because delivery happens later in the background.
        await QueueNoticesAsync(businessId, current, actor, target, targetUser, managerRole is not null, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>One notice to the new owner and one (also a security notice) to the former owner. An address that is not a usable
    /// email address is skipped rather than blocking the transfer.</summary>
    private async Task QueueNoticesAsync(Guid businessId, BusinessMembership former, AppUser formerUser, BusinessMembership heir, AppUser heirUser,
        bool formerIsManager, CancellationToken ct)
    {
        var businessName = await db.Businesses.Where(b => b.Id == businessId).Select(b => b.Name).FirstOrDefaultAsync(ct) ?? "";
        var formerName = Pick(former.DisplayName, formerUser.FullName);
        var heirName = Pick(heir.DisplayName, heirUser.FullName);

        if (EmailAddressRules.TryNormalize(heirUser.Email, out var heirAddress))
        {
            var mail = EmailTemplates.OwnershipReceived(heirName, businessName, formerName);
            await email.QueueAsync(new EmailRequest(heirAddress, mail.Subject, mail.Text, mail.Html, businessId, "ownership_received"), ct);
        }
        if (EmailAddressRules.TryNormalize(formerUser.Email, out var formerAddress))
        {
            var mail = EmailTemplates.OwnershipHandedOver(formerName, businessName, heirName, formerIsManager);
            await email.QueueAsync(new EmailRequest(formerAddress, mail.Subject, mail.Text, mail.Html, businessId, "ownership_handed_over"), ct);
        }
    }

    private static string Pick(string? preferred, string? fallback) => !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback ?? "";
}
