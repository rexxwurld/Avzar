using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Tailoring.Application.Abstractions;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Api.Security;

/// <summary>Reads user and business ids from the signed session cookie. Never from request bodies or headers.</summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? UserId => Read(ClaimTypes.NameIdentifier);
    public Guid? BusinessId => Read(TenantClaims.BusinessId);

    private Guid? Read(string type)
        => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(type), out var id) ? id : null;
}

public sealed class SessionSigner(SignInManager<AppUser> signIn, UserManager<AppUser> users, IHttpContextAccessor http)
{
    public async Task SignInAsync(Guid userId, Guid businessId)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("User not found.");
        var principal = await signIn.CreateUserPrincipalAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(TenantClaims.BusinessId, businessId.ToString()));
        await http.HttpContext!.SignInAsync(IdentityConstants.ApplicationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });
    }
}

/// <summary>[HasPermission("staff.manage")] - checked against the database on every request.</summary>
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute($"{PolicyPrefix}{permission}")
{
    public const string PolicyPrefix = "perm:";
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[HasPermissionAttribute.PolicyPrefix.Length..]))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

public sealed class PermissionHandler(ITenantContext tenant, IPermissionService permissions)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement req)
    {
        if (tenant.UserId is not { } userId || tenant.BusinessId is not { } businessId) return;

        // Looks up the CURRENT membership, so deactivation and role changes take effect immediately.
        var access = await permissions.GetAccessAsync(userId, businessId);
        if (access is null) return;
        if (access.IsOwner || access.Permissions.Contains(req.Permission)) context.Succeed(req);
    }
}
