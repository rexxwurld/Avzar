using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Auth;
using Tailoring.Application.Common;
using Tailoring.Application.Email;
using Tailoring.Domain.Access;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class AccountService(
    AppDbContext db, UserManager<AppUser> users, IEmailSender email, ISessionService sessions,
    ITenantContext tenant, IAuditLogger audit,
    IOptions<AppOptions> appOptions, IOptions<AuthOptions> authOptions, ILogger<AccountService> log) : IAccountService
{
    private AuthOptions A => authOptions.Value;
    private string Base => appOptions.Value.WebBaseUrl.TrimEnd('/');

    // ---------- email verification ----------

    public async Task SendEmailVerificationAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId);
        if (user.EmailConfirmed) throw AppException.Conflict("email_already_confirmed", "This email address is already confirmed.");
        await QueueVerificationAsync(user, ct);
    }

    /// <summary>Also used right after registration. Throttled per address so the endpoint cannot be used to flood an inbox.</summary>
    private async Task QueueVerificationAsync(AppUser user, CancellationToken ct = default)
    {
        await ThrottleAsync(user.Email!, "verify_email", ct);
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{Base}/verify-email?userId={user.Id}&token={LinkToken.Encode(token)}";
        var mail = EmailTemplates.VerifyEmail(user.FullName, link, A.EmailVerificationHours);
        await email.QueueAsync(new EmailRequest(user.Email!, mail.Subject, mail.Text, mail.Html, null, "verify_email"), ct);
    }

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || !LinkToken.TryDecode(request.Token, out var token)
            || !(await users.ConfirmEmailAsync(user, token)).Succeeded)
            throw AppException.BadRequest("invalid_token", "This link is invalid or has expired.");
        log.LogInformation("Email confirmed for user {UserId}.", user.Id);
    }

    // ---------- password reset ----------

    public async Task RequestPasswordResetAsync(string emailAddress, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(emailAddress.Trim());
        // Unknown address, locked-out-forever (deleted) account or throttled request: all look identical to the caller.
        if (user is null || user.LockoutEnd == DateTimeOffset.MaxValue) return;
        try { await ThrottleAsync(user.Email!, "password_reset", ct); }
        catch (AppException ex) when (ex.StatusCode == 429) { return; }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = $"{Base}/reset-password?userId={user.Id}&token={LinkToken.Encode(token)}";
        var mail = EmailTemplates.PasswordReset(user.FullName, link, A.PasswordResetMinutes);
        await email.QueueAsync(new EmailRequest(user.Email!, mail.Subject, mail.Text, mail.Html, null, "password_reset"), ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || user.LockoutEnd == DateTimeOffset.MaxValue || !LinkToken.TryDecode(request.Token, out var token))
            throw AppException.BadRequest("invalid_token", "This link is invalid or has expired.");

        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                throw AppException.BadRequest("invalid_token", "This link is invalid or has expired.");
            throw AppException.BadRequest("weak_password", string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // The reset changed the security stamp (web cookies stop validating). Also end every device session and clear lockout.
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await sessions.RevokeAllAsync(user.Id, "password_reset", ct);
        log.LogInformation("Password reset completed for user {UserId}.", user.Id);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId);
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
                throw AppException.BadRequest("wrong_password", "The current password is incorrect.");
            throw AppException.BadRequest("weak_password", string.Join(" ", result.Errors.Select(e => e.Description)));
        }
        await sessions.RevokeAllAsync(user.Id, "password_changed", ct);
        await RecordAsync("account.password_changed", user.Id, ct);
    }

    // ---------- two-factor (TOTP) ----------

    public async Task<SecuritySettingsDto> GetSecurityAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId);
        var codes = user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user) : 0;
        var active = (await sessions.ListAsync(userId, ct)).Count;
        return new SecuritySettingsDto(user.EmailConfirmed, user.TwoFactorEnabled, codes, active);
    }

    public async Task<TwoFactorSetupDto> BeginTwoFactorSetupAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId);
        if (user.TwoFactorEnabled) throw AppException.Conflict("two_factor_already_enabled", "Two-factor authentication is already on.");

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }
        var issuer = Uri.EscapeDataString(A.ProductName);
        var label = Uri.EscapeDataString(user.Email!);
        return new TwoFactorSetupDto(key!, $"otpauth://totp/{issuer}:{label}?secret={key}&issuer={issuer}&digits=6");
    }

    public async Task<RecoveryCodesDto> EnableTwoFactorAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId);
        if (user.TwoFactorEnabled) throw AppException.Conflict("two_factor_already_enabled", "Two-factor authentication is already on.");
        if (string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user)))
            throw AppException.BadRequest("two_factor_not_started", "Start two-factor setup first.");

        var clean = request.Code.Replace(" ", "").Replace("-", "");
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, clean))
            throw AppException.BadRequest("invalid_code", "That code is not correct. Check the time on your phone and try again.");

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)
            ?? throw new InvalidOperationException("Recovery codes could not be generated.");
        await RecordAsync("account.two_factor_enabled", user.Id, ct);
        return new RecoveryCodesDto(codes.ToList());
    }

    public async Task DisableTwoFactorAsync(Guid userId, DisableTwoFactorRequest request, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId);
        if (!user.TwoFactorEnabled) throw AppException.Conflict("two_factor_not_enabled", "Two-factor authentication is not on.");
        await RequirePasswordAndSecondFactorAsync(user, request.Password, request.Code, request.RecoveryCode);

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);              // the old authenticator secret must stop working
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0); // and so must any leftover recovery codes
        await RecordAsync("account.two_factor_disabled", user.Id, ct);
    }

    // ---------- export and deletion ----------

    public async Task<AccountExportDto> ExportAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId);
        var rows = await (
            from m in db.Memberships.IgnoreQueryFilters().AsNoTracking()   // explicit: the user's own rows across all their businesses
            join b in db.Businesses.IgnoreQueryFilters().AsNoTracking() on m.BusinessId equals b.Id
            where m.UserId == userId
            select new { MembershipId = m.Id, BusinessId = b.Id, b.Name, m.DisplayName, m.IsOwner, m.Status }).ToListAsync(ct);
        var membershipIds = rows.Select(r => r.MembershipId).ToList();
        var roleRows = await (
            from mr in db.MembershipRoles.IgnoreQueryFilters().AsNoTracking()
            join r in db.BusinessRoles.IgnoreQueryFilters().AsNoTracking() on mr.RoleId equals r.Id
            where membershipIds.Contains(mr.MembershipId)
            select new { mr.MembershipId, r.Name }).ToListAsync(ct);
        var memberships = rows.Select(x => new AccountMembershipExportDto(x.BusinessId, x.Name, x.DisplayName, x.IsOwner, x.Status.ToString(),
            roleRows.Where(r => r.MembershipId == x.MembershipId).Select(r => r.Name).OrderBy(n => n).ToList())).ToList();

        return new AccountExportDto(user.Id, user.Email!, user.FullName, user.EmailConfirmed, user.TwoFactorEnabled, user.CreatedAtUtc,
            memberships,
            await sessions.ListAsync(userId, ct), DateTime.UtcNow);
    }

    public async Task DeleteAccountAsync(Guid userId, DeleteAccountRequest request, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId);
        await RequirePasswordAndSecondFactorAsync(user, request.Password, request.TwoFactorCode, request.RecoveryCode);

        var ownsBusiness = await db.Memberships.IgnoreQueryFilters()
            .AnyAsync(m => m.UserId == userId && m.IsOwner && m.Status == MembershipStatus.Active, ct);
        if (ownsBusiness)
            throw AppException.Conflict("owner_of_business", "You own a business. Close the business or hand it over before deleting your account.");

        await RecordAsync("account.deleted", userId, ct);   // saved with the transaction below
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;
        // Financial and audit records stay (they reference the user id); the person behind them is anonymised.
        await db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, MembershipStatus.Deactivated)
                .SetProperty(m => m.DeactivatedAtUtc, (DateTime?)now).SetProperty(m => m.DisplayName, "Deleted user"), ct);

        var anon = $"deleted-{userId:N}@deleted.invalid";
        user.FullName = "Deleted user";
        user.PhoneNumber = null;
        await users.SetUserNameAsync(user, anon);
        await users.SetEmailAsync(user, anon);
        await users.RemovePasswordAsync(user);
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(user);
        await sessions.RevokeAllAsync(userId, "account_deleted", ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Account {UserId} deleted (anonymised).", userId);
    }

    // ---------- helpers ----------

    private async Task<AppUser> RequireUserAsync(Guid userId)
        => await users.FindByIdAsync(userId.ToString()) is { } u && u.LockoutEnd != DateTimeOffset.MaxValue
            ? u : throw AppException.Unauthorized("unknown_user", "Unknown user.");

    private async Task RequirePasswordAndSecondFactorAsync(AppUser user, string password, string? code, string? recoveryCode)
    {
        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            throw AppException.BadRequest("wrong_password", "The password is incorrect.");
        }
        if (user.TwoFactorEnabled && !await SecondFactor.VerifyAsync(users, user, code, recoveryCode))
        {
            await users.AccessFailedAsync(user);
            throw AppException.BadRequest("invalid_code", "The authentication code is not correct.");
        }
    }

    private async Task ThrottleAsync(string address, string templateKey, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddSeconds(-Math.Max(0, A.EmailThrottleSeconds));
        if (await db.EmailOutbox.AnyAsync(m => m.ToAddress == address && m.TemplateKey == templateKey && m.CreatedAtUtc > since, ct))
            throw new AppException(429, "too_many_requests", "An email was sent a moment ago. Please wait a couple of minutes and check your inbox.");
    }

    /// <summary>Audit rows are per business, so account events are recorded against the business the caller is signed in to (if any).</summary>
    private async Task RecordAsync(string action, Guid userId, CancellationToken ct)
    {
        if (tenant.BusinessId is { } businessId)
        {
            audit.Record(businessId, userId, action, nameof(AppUser), userId);
            if (action != "account.deleted") await db.SaveChangesAsync(ct);
        }
    }
}
