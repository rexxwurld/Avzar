using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tailoring.Api.Security;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Auth;

namespace Tailoring.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(IAuthService auth, SessionSigner signer, IAntiforgery antiforgery, ITenantContext tenant) : ControllerBase
{
    /// <summary>Returns a CSRF request token. Send it back in the X-XSRF-TOKEN header on every POST/PUT/PATCH/DELETE. Re-fetch after login/logout.</summary>
    [HttpGet("csrf"), IgnoreAntiforgeryToken]
    public IActionResult Csrf() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("register-business"), EnableRateLimiting("auth")]
    public async Task<ActionResult<CurrentUserDto>> RegisterBusiness(RegisterBusinessRequest request, CancellationToken ct)
    {
        var session = await auth.RegisterBusinessAsync(request, ct);
        await signer.SignInAsync(session.UserId, session.BusinessId);
        return StatusCode(StatusCodes.Status201Created, await auth.GetCurrentUserAsync(session.UserId, session.BusinessId, ct));
    }

    /// <summary>200 with the signed-in user, or 409 {code:"choose_business", choices:[...]} when the user belongs to several businesses.</summary>
    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<ActionResult<CurrentUserDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.AuthenticateAsync(request, ct);
        if (result.Session is null)
            return Conflict(new { code = "choose_business", choices = result.Choices });

        await signer.SignInAsync(result.Session.UserId, result.Session.BusinessId);
        return await auth.GetCurrentUserAsync(result.Session.UserId, result.Session.BusinessId, ct);
    }

    [HttpPost("accept-invitation"), EnableRateLimiting("auth")]
    public async Task<ActionResult<CurrentUserDto>> AcceptInvitation(AcceptInvitationRequest request, CancellationToken ct)
    {
        var session = await auth.AcceptInvitationAsync(request, ct);
        await signer.SignInAsync(session.UserId, session.BusinessId);
        return await auth.GetCurrentUserAsync(session.UserId, session.BusinessId, ct);
    }

    /// <summary>Join a business with the account you are already signed in to. The account email must match the invitation.</summary>
    [HttpPost("accept-invitation/existing"), Authorize, EnableRateLimiting("auth")]
    public async Task<ActionResult<CurrentUserDto>> AcceptInvitationExisting(AcceptExistingInvitationRequest request, CancellationToken ct)
    {
        // A session cookie that carries no user id is treated as signed out, not as a server error.
        if (tenant.UserId is not { } userId) return Unauthorized();
        var session = await auth.AcceptInvitationAsExistingUserAsync(userId, request, ct);
        await signer.SignInAsync(session.UserId, session.BusinessId);
        return await auth.GetCurrentUserAsync(session.UserId, session.BusinessId, ct);
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    [HttpGet("me"), Authorize]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        // A stale or partial session cookie (no user id or no business id) means "signed out": 401, never a 500.
        if (tenant.UserId is not { } userId || tenant.BusinessId is not { } businessId)
            return Unauthorized();
        return await auth.GetCurrentUserAsync(userId, businessId, ct);
    }
}
