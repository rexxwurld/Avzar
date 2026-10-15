using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tailoring.Application.Abstractions;

namespace Tailoring.Api.Security;

/// <summary>Short-lived signed access tokens for the mobile app. Contains only ids; permissions are always looked up server-side.</summary>
public sealed class JwtTokenIssuer(IOptions<AuthOptions> options)
{
    public const string SessionClaim = "sid";
    public const string Scheme = "Bearer";

    public (string Token, DateTime ExpiresAtUtc) Issue(Guid userId, Guid businessId, Guid sessionId)
    {
        var o = options.Value;
        if (!o.MobileTokensEnabled)
            throw new Tailoring.Application.Common.AppException(503, "mobile_auth_not_configured", "Mobile sign-in is not enabled on this server.");

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(Math.Max(1, o.AccessTokenMinutes));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.JwtIssuer,
            Audience = o.JwtAudience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(TenantClaims.BusinessId, businessId.ToString()),
                new Claim(SessionClaim, sessionId.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(Key(o), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public static SymmetricSecurityKey Key(AuthOptions o) => new(Encoding.UTF8.GetBytes(o.JwtSigningKey!));
}
