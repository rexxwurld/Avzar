using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tailoring.Infrastructure.Persistence;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>A valid session cookie that has a user but no business claim (a stale cookie) must read as signed out, not crash.</summary>
[Collection("api")]
public sealed class AuthNoBusinessClaimTests(ApiFactory factory)
{
    [Fact]
    public async Task Me_returns_401_when_the_session_cookie_has_no_business_claim()
    {
        string cookie;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>();
            var email = $"nobiz-{Guid.NewGuid():N}@test.local";
            var user = new AppUser { UserName = email, Email = email, FullName = "No Business", EmailConfirmed = true };
            var created = await users.CreateAsync(user, ApiFactory.DefaultPassword);
            Assert.True(created.Succeeded);

            var principal = await signIn.CreateUserPrincipalAsync(user); // deliberately NO business claim added
            var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), IdentityConstants.ApplicationScheme);
            cookie = options.TicketDataFormat.Protect(ticket);
        }

        using var http = factory.NewClient(cookies: false);
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Add("Cookie", $"tailoring.session={cookie}");
        using var res = await http.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
