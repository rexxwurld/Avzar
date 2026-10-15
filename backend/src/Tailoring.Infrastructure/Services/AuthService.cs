using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Auth;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Measurements;
using Tailoring.Domain.Production;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class AuthService(
    AppDbContext db,
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    IPermissionService permissions,
    IAuditLogger audit,
    IEntitlementService entitlements) : IAuthService
{
    public async Task<SessionIdentity> RegisterBusinessAsync(RegisterBusinessRequest req, CancellationToken ct = default)
    {
        var currency = req.CurrencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
            throw AppException.BadRequest("invalid_currency", "Currency must be a 3-letter ISO 4217 code.");
        try { TimeZoneInfo.FindSystemTimeZoneById(req.TimeZoneId); }
        catch (TimeZoneNotFoundException) { throw AppException.BadRequest("invalid_time_zone", "Unknown time zone id."); }

        var email = req.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
            throw AppException.Conflict("email_in_use", "An account with this email already exists.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var user = new AppUser { UserName = email, Email = email, FullName = req.OwnerName.Trim() };
        var created = await users.CreateAsync(user, req.Password);
        if (!created.Succeeded)
            throw AppException.BadRequest("weak_password", string.Join(" ", created.Errors.Select(e => e.Description)));

        var business = new Business
        {
            Name = req.BusinessName.Trim(), TimeZoneId = req.TimeZoneId, CurrencyCode = currency
        };
        db.Businesses.Add(business);

        var location = new BusinessLocation
        {
            BusinessId = business.Id,
            Name = string.IsNullOrWhiteSpace(req.LocationName) ? "Main workshop" : req.LocationName.Trim()
        };
        db.Locations.Add(location);

        var roles = DefaultRoles.Create(business.Id);
        db.BusinessRoles.AddRange(roles);
        db.MeasurementTemplates.AddRange(DefaultMeasurementTemplates.Create(business.Id));
        var (workflow, stageLinks) = DefaultWorkflows.Create(business.Id);
        db.Workflows.Add(workflow);
        db.StageDependencies.AddRange(stageLinks);

        var membership = new BusinessMembership
        {
            BusinessId = business.Id, UserId = user.Id, DisplayName = user.FullName, IsOwner = true
        };
        membership.Roles.Add(new MembershipRole
        {
            BusinessId = business.Id, MembershipId = membership.Id,
            RoleId = roles.Single(r => r.Name == DefaultRoles.Owner).Id
        });
        membership.Locations.Add(new MembershipLocation
        {
            BusinessId = business.Id, MembershipId = membership.Id, LocationId = location.Id
        });
        db.Memberships.Add(membership);

        var freePlan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == DefaultPlans.FreeCode && p.IsActive, ct)
            ?? throw new AppException(503, "plans_not_seeded", "Subscription plans are not configured on this server.");
        db.Subscriptions.Add(new TenantSubscription { BusinessId = business.Id, PlanId = freePlan.Id });

        audit.Record(business.Id, user.Id, "business.registered", nameof(Business), business.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new SessionIdentity(user.Id, business.Id);
    }

    public async Task<AuthResult> AuthenticateAsync(LoginRequest req, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(req.Email.Trim());
        if (user is null)
            throw AppException.Unauthorized("invalid_credentials", "Email or password is incorrect.");

        var check = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
            throw new AppException(423, "locked_out", "Too many failed attempts. Try again later.");
        if (!check.Succeeded)
            throw AppException.Unauthorized("invalid_credentials", "Email or password is incorrect.");

        var choices = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Status == MembershipStatus.Active)
            .Join(db.Businesses.IgnoreQueryFilters().Where(x => x.IsActive),
                  m => m.BusinessId, x => x.Id, (m, x) => new BusinessChoiceDto(x.Id, x.Name))
            .ToListAsync(ct);

        if (choices.Count == 0)
            throw AppException.Forbidden("no_active_membership", "This account has no active business membership.");

        if (req.BusinessId is { } wanted)
        {
            if (choices.All(c => c.BusinessId != wanted))
                throw AppException.Forbidden("not_a_member", "You are not an active member of that business.");
            return new AuthResult(new SessionIdentity(user.Id, wanted), choices);
        }

        return choices.Count == 1
            ? new AuthResult(new SessionIdentity(user.Id, choices[0].BusinessId), choices)
            : new AuthResult(null, choices);
    }

    public async Task<SessionIdentity> AcceptInvitationAsync(AcceptInvitationRequest req, CancellationToken ct = default)
    {
        var hash = TokenHasher.Hash(req.Token.Trim());
        var invite = await db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

        if (invite is null || invite.AcceptedAtUtc is not null || invite.RevokedAtUtc is not null
            || invite.ExpiresAtUtc < DateTime.UtcNow)
            throw AppException.BadRequest("invalid_invitation", "This invitation is invalid or has expired.");

        // Joining with an EXISTING account must be done from an authenticated session. Not built yet (see PROGRESS.md).
        if (await users.FindByEmailAsync(invite.Email) is not null)
            throw AppException.Conflict("account_exists", "An account with this email already exists. Joining with an existing account is not implemented yet.");

        // Re-validate the roles/locations that were chosen at invite time.
        var roleIds = await db.BusinessRoles.IgnoreQueryFilters()
            .Where(r => r.BusinessId == invite.BusinessId && invite.RoleIds.Contains(r.Id) && r.Name != DefaultRoles.Owner)
            .Select(r => r.Id).ToListAsync(ct);
        var locationIds = await db.Locations.IgnoreQueryFilters()
            .Where(l => l.BusinessId == invite.BusinessId && l.IsActive && invite.LocationIds.Contains(l.Id))
            .Select(l => l.Id).ToListAsync(ct);
        if (roleIds.Count == 0)
            throw AppException.BadRequest("invalid_invitation", "The roles for this invitation no longer exist.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // The plan may have been downgraded since the invite went out. This invitation already holds a seat,
        // so compare ACTIVE staff only.
        await entitlements.EnsureBelowLimitAsync(invite.BusinessId, EntitlementKeys.UsersMax,
            c => db.Memberships.IgnoreQueryFilters().LongCountAsync(m => m.BusinessId == invite.BusinessId && m.Status == MembershipStatus.Active, c), ct);

        var user = new AppUser { UserName = invite.Email, Email = invite.Email, EmailConfirmed = true, FullName = invite.FullName };
        var created = await users.CreateAsync(user, req.Password);
        if (!created.Succeeded)
            throw AppException.BadRequest("weak_password", string.Join(" ", created.Errors.Select(e => e.Description)));

        var membership = new BusinessMembership
        {
            BusinessId = invite.BusinessId, UserId = user.Id, DisplayName = invite.FullName
        };
        foreach (var rid in roleIds)
            membership.Roles.Add(new MembershipRole { BusinessId = invite.BusinessId, MembershipId = membership.Id, RoleId = rid });
        foreach (var lid in locationIds)
            membership.Locations.Add(new MembershipLocation { BusinessId = invite.BusinessId, MembershipId = membership.Id, LocationId = lid });
        db.Memberships.Add(membership);

        invite.AcceptedAtUtc = DateTime.UtcNow;
        audit.Record(invite.BusinessId, user.Id, "staff.invitation_accepted", nameof(BusinessMembership), membership.Id);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new SessionIdentity(user.Id, invite.BusinessId);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, Guid businessId, CancellationToken ct = default)
    {
        var access = await permissions.GetAccessAsync(userId, businessId, ct)
            ?? throw AppException.Unauthorized("membership_inactive", "Your membership in this business is no longer active.");

        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw AppException.Unauthorized("unknown_user", "Unknown user.");
        var business = await db.Businesses.IgnoreQueryFilters().AsNoTracking().FirstAsync(b => b.Id == businessId, ct);

        var perms = (access.IsOwner ? Permissions.All : access.Permissions.ToList()).OrderBy(p => p).ToList();
        return new CurrentUserDto(user.Id, user.Email!, user.FullName, business.Id, business.Name,
            business.TimeZoneId, business.CurrencyCode, access.IsOwner, perms);
    }
}
