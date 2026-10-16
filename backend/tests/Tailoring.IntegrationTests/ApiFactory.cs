using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Tailoring.Domain.Access;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// The real API (Program.cs: authentication, CSRF filter, permission handler, rate limiter, DI wiring) on a real PostgreSQL.
/// One instance is shared by every test class in the "api" collection. Development is used because the session cookie is
/// Secure-only otherwise and the test client talks plain HTTP; Development also runs the real migrations and seeds the plans.
/// Configuration goes in through environment variables because Program.cs reads it before the host exists. They are restored as
/// soon as the host has started (configuration is read once at build time).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
    private readonly Dictionary<string, string?> _saved = new();
    private int _ip;

    public const string DefaultPassword = "Str0ng-Passw0rd!";
    /// <summary>Test-only Svix signing secret: "whsec_" + base64("avzar-webhook-test-key-0123456789").</summary>
    public const string WebhookSecret = "whsec_YXZ6YXItd2ViaG9vay10ZXN0LWtleS0wMTIzNDU2Nzg5";

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _pg.StartAsync();
        Set("ConnectionStrings__Default", _pg.GetConnectionString());
        Set("Auth__JwtSigningKey", "avzar-test-signing-key-0123456789-abcdef"); // 32+ chars turns the bearer scheme on
        Set("Email__Resend__WebhookSecret", WebhookSecret);                    // turns the email webhook endpoint on
        Set("Proxy__TrustForwardedHeaders", "true");                           // lets each test client present its own address to the rate limiter
        try { _ = Services; }                                                    // build and start the host now, while the variables are set
        finally { foreach (var (k, v) in _saved) Environment.SetEnvironmentVariable(k, v); }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _pg.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

    private void Set(string key, string value)
    {
        _saved[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    public string NextIp()
    {
        var n = Interlocked.Increment(ref _ip);
        return $"10.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}";
    }

    /// <summary>A client that does not follow redirects. Each client has its own source address so the per-IP auth limiter never trips.</summary>
    public HttpClient NewClient(bool cookies = true)
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = cookies });
        c.DefaultRequestHeaders.Add("X-Forwarded-For", NextIp());
        return c;
    }

    /// <summary>A second member of an existing business whose role grants exactly the given permissions.</summary>
    public async Task<(string Email, Guid MembershipId)> SeedMemberAsync(Guid businessId, params string[] permissions)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // no HTTP request: no tenant, so ids are explicit

        var email = $"staff-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, FullName = "Staff Member", EmailConfirmed = true };
        var created = await users.CreateAsync(user, DefaultPassword);
        if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));

        var role = new BusinessRole { BusinessId = businessId, Name = $"Limited {Guid.NewGuid():N}" };
        foreach (var p in permissions)
            role.Permissions.Add(new BusinessRolePermission { BusinessId = businessId, RoleId = role.Id, Permission = p });
        var membership = new BusinessMembership { BusinessId = businessId, UserId = user.Id, DisplayName = "Staff Member" };
        membership.Roles.Add(new MembershipRole { BusinessId = businessId, MembershipId = membership.Id, RoleId = role.Id });
        db.BusinessRoles.Add(role);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();
        return (email, membership.Id);
    }

    /// <summary>Adds a second location to an existing business (setup only; goes straight to the database).</summary>
    public async Task<Guid> SeedLocationAsync(Guid businessId, string name)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var location = new BusinessLocation { BusinessId = businessId, Name = name };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    /// <summary>Limits a member to the given locations (setup only).</summary>
    public async Task AssignLocationsAsync(Guid businessId, Guid membershipId, params Guid[] locationIds)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var id in locationIds)
            db.MembershipLocations.Add(new MembershipLocation { BusinessId = businessId, MembershipId = membershipId, LocationId = id });
        await db.SaveChangesAsync();
    }

    public async Task DeactivateMemberAsync(Guid businessId, Guid membershipId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var changed = await db.Memberships.IgnoreQueryFilters().Where(x => x.Id == membershipId && x.BusinessId == businessId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MembershipStatus.Deactivated).SetProperty(x => x.DeactivatedAtUtc, now));
        if (changed != 1) throw new InvalidOperationException("Membership not found.");
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
