using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Tailoring.Api.Security;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Auth;
using Tailoring.Application.Common;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

/// <summary>Public (not signed in) account recovery. Same CSRF rule as login.</summary>
[ApiController, Route("api/auth")]
public class AccountRecoveryController(IAccountService account) : ControllerBase
{
    [HttpPost("confirm-email"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken ct)
    { await account.ConfirmEmailAsync(request, ct); return NoContent(); }

    /// <summary>Always 202, whether or not the address has an account.</summary>
    [HttpPost("forgot-password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    { await account.RequestPasswordResetAsync(request.Email, ct); return Accepted(); }

    [HttpPost("reset-password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    { await account.ResetPasswordAsync(request, ct); return NoContent(); }
}

[ApiController, Route("api/account"), Authorize]
public class AccountController(
    IAccountService account, ISessionService sessions, ITenantContext tenant, SessionSigner signer) : ControllerBase
{
    private Guid UserId => tenant.UserId!.Value;
    private bool IsBearer => Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

    [HttpGet("security")]
    public async Task<SecuritySettingsDto> Security(CancellationToken ct) => await account.GetSecurityAsync(UserId, ct);

    [HttpPost("resend-verification"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendVerification(CancellationToken ct)
    { await account.SendEmailVerificationAsync(UserId, ct); return Accepted(); }

    [HttpPost("change-password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await account.ChangePasswordAsync(UserId, request, ct);
        // The password change invalidated every cookie (new security stamp). Keep THIS browser signed in.
        if (!IsBearer && tenant.BusinessId is { } businessId) await signer.SignInAsync(UserId, businessId);
        return NoContent();
    }

    [HttpPost("two-factor/setup"), EnableRateLimiting("auth")]
    public async Task<TwoFactorSetupDto> BeginTwoFactor(CancellationToken ct) => await account.BeginTwoFactorSetupAsync(UserId, ct);

    [HttpPost("two-factor/enable"), EnableRateLimiting("auth")]
    public async Task<RecoveryCodesDto> EnableTwoFactor(EnableTwoFactorRequest request, CancellationToken ct)
        => await account.EnableTwoFactorAsync(UserId, request, ct);

    [HttpPost("two-factor/disable"), EnableRateLimiting("auth")]
    public async Task<IActionResult> DisableTwoFactor(DisableTwoFactorRequest request, CancellationToken ct)
    { await account.DisableTwoFactorAsync(UserId, request, ct); return NoContent(); }

    /// <summary>Device (bearer-token) sessions. Browser cookie sessions are not listed.</summary>
    [HttpGet("sessions")]
    public async Task<IReadOnlyList<SessionDto>> Sessions(CancellationToken ct) => await sessions.ListAsync(UserId, ct);

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    { await sessions.RevokeByIdAsync(UserId, sessionId, ct); return NoContent(); }

    [HttpPost("sessions/revoke-all")]
    public async Task<IActionResult> RevokeAll(CancellationToken ct)
    { await sessions.RevokeAllAsync(UserId, "revoked_by_user", ct); return NoContent(); }

    [HttpGet("export")]
    public async Task<AccountExportDto> Export(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return await account.ExportAsync(UserId, ct);
    }

    [HttpPost("delete"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Delete(DeleteAccountRequest request, CancellationToken ct)
    {
        await account.DeleteAccountAsync(UserId, request, ct);
        await HttpContext.SignOutAsync(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
        return NoContent();
    }
}

public sealed record TokenResponse(string AccessToken, DateTime AccessExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc, CurrentUserDto User);

/// <summary>Bearer-token sign-in for the mobile app. Anonymous endpoints, so they opt out of CSRF (no cookie is involved).</summary>
[ApiController, Route("api/auth/token"), IgnoreAntiforgeryToken]
public class MobileAuthController(
    IAuthService auth, ISessionService sessions, JwtTokenIssuer issuer, IOptions<AuthOptions> options) : ControllerBase
{
    private void RequireEnabled()
    {
        if (!options.Value.MobileTokensEnabled)
            throw new AppException(503, "mobile_auth_not_configured", "Mobile sign-in is not enabled on this server.");
        Response.Headers.CacheControl = "no-store";
    }

    [HttpPost, EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> SignIn(TokenLoginRequest request, CancellationToken ct)
    {
        RequireEnabled();
        var result = await auth.AuthenticateAsync(new LoginRequest
        {
            Email = request.Email, Password = request.Password, BusinessId = request.BusinessId,
            TwoFactorCode = request.TwoFactorCode, RecoveryCode = request.RecoveryCode,
        }, ct);
        if (result.Session is null) return Conflict(new { code = "choose_business", choices = result.Choices });

        var tokens = await sessions.CreateAsync(result.Session.UserId, result.Session.BusinessId, request.DeviceName, ct);
        return await BuildAsync(result.Session, tokens, ct);
    }

    [HttpPost("refresh"), EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshTokenRequest request, CancellationToken ct)
    {
        RequireEnabled();
        var result = await sessions.RefreshAsync(request.RefreshToken, ct);
        return await BuildAsync(result.Identity, result.Tokens, ct);
    }

    [HttpPost("revoke"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Revoke(RefreshTokenRequest request, CancellationToken ct)
    { await sessions.RevokeAsync(request.RefreshToken, ct); return NoContent(); }

    private async Task<TokenResponse> BuildAsync(SessionIdentity who, TokenPair tokens, CancellationToken ct)
    {
        var access = issuer.Issue(who.UserId, who.BusinessId, tokens.SessionId);
        var user = await auth.GetCurrentUserAsync(who.UserId, who.BusinessId, ct);
        return new TokenResponse(access.Token, access.ExpiresAtUtc, tokens.RefreshToken, tokens.RefreshExpiresAtUtc, user);
    }
}

[ApiController, Route("api/business/schedule"), HasPermission(Permissions.BusinessSettings)]
public class BusinessScheduleController(IBusinessSettingsService settings) : ControllerBase
{
    [HttpGet]
    public async Task<BusinessScheduleDto> Get(CancellationToken ct) => await settings.GetScheduleAsync(ct);

    [HttpPut("hours")]
    public async Task<BusinessScheduleDto> SetHours(SetBusinessHoursRequest request, CancellationToken ct) => await settings.SetHoursAsync(request, ct);

    [HttpPost("holidays")]
    public async Task<ActionResult<HolidayDto>> AddHoliday(AddHolidayRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await settings.AddHolidayAsync(request, ct));

    [HttpDelete("holidays/{holidayId:guid}")]
    public async Task<IActionResult> RemoveHoliday(Guid holidayId, CancellationToken ct)
    { await settings.RemoveHolidayAsync(holidayId, ct); return NoContent(); }
}
