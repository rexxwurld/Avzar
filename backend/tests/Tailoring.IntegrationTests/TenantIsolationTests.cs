using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Tailoring.Application.Abstractions;
using Tailoring.Domain.Access;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

public sealed class FakeTenant(Guid? businessId) : ITenantContext
{
    public Guid? UserId => null;
    public Guid? BusinessId { get; } = businessId;
}

/// <summary>Real PostgreSQL via Testcontainers (requires Docker). EnsureCreated is for tests only; production uses migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();

    public AppDbContext Create(Guid? tenantId) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_pg.GetConnectionString()).Options,
        new FakeTenant(tenantId));

    /// <summary>Context whose tenant (business AND user) is supplied by the caller. Named differently from Create to avoid a null-literal overload clash.</summary>
    public AppDbContext CreateFor(ITenantContext tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_pg.GetConnectionString()).Options, tenant);

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var ctx = Create(null);
        await ctx.Database.EnsureCreatedAsync();
        await PlanSeeder.SyncAsync(ctx); // businesses without a subscription fall back to the seeded Free plan
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();
}

public sealed class TenantIsolationTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private async Task<(Guid BusinessId, Guid LocationId, Guid RoleId, Guid MembershipId)> SeedBusinessAsync(string name)
    {
        await using var db = fx.Create(null); // no tenant in scope: ids must be explicit
        var user = new AppUser { UserName = $"{name}@test.local", Email = $"{name}@test.local", FullName = name };
        db.Users.Add(user);

        var business = new Business { Name = name };
        db.Businesses.Add(business);
        var location = new BusinessLocation { BusinessId = business.Id, Name = $"{name} workshop" };
        var role = new BusinessRole { BusinessId = business.Id, Name = "Tailor" };
        db.Locations.Add(location);
        db.BusinessRoles.Add(role);
        var membership = new BusinessMembership { BusinessId = business.Id, UserId = user.Id, DisplayName = name };
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();
        return (business.Id, location.Id, role.Id, membership.Id);
    }

    [Fact]
    public async Task Queries_only_return_the_current_business_rows()
    {
        var a = await SeedBusinessAsync("alpha-" + Guid.NewGuid().ToString("N")[..6]);
        var b = await SeedBusinessAsync("beta-" + Guid.NewGuid().ToString("N")[..6]);

        await using var asA = fx.Create(a.BusinessId);
        var locations = await asA.Locations.ToListAsync();
        Assert.Single(locations);
        Assert.Equal(a.LocationId, locations[0].Id);

        // Direct lookup of B's row by its real id still finds nothing.
        Assert.Null(await asA.Locations.FirstOrDefaultAsync(l => l.Id == b.LocationId));
        Assert.Null(await asA.Memberships.FirstOrDefaultAsync(m => m.Id == b.MembershipId));
    }

    [Fact]
    public async Task No_tenant_in_scope_means_no_tenant_rows_are_visible()
    {
        await SeedBusinessAsync("gamma-" + Guid.NewGuid().ToString("N")[..6]);
        await using var anon = fx.Create(null);
        Assert.Empty(await anon.Locations.ToListAsync());
    }

    [Fact]
    public async Task Writing_a_row_for_another_business_is_blocked_by_the_context()
    {
        var a = await SeedBusinessAsync("delta-" + Guid.NewGuid().ToString("N")[..6]);
        var b = await SeedBusinessAsync("eps-" + Guid.NewGuid().ToString("N")[..6]);

        await using var asA = fx.Create(a.BusinessId);
        asA.Locations.Add(new BusinessLocation { BusinessId = b.BusinessId, Name = "sneaky" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => asA.SaveChangesAsync());
    }

    [Fact]
    public async Task New_rows_are_stamped_with_the_current_business()
    {
        var a = await SeedBusinessAsync("zeta-" + Guid.NewGuid().ToString("N")[..6]);
        await using var asA = fx.Create(a.BusinessId);
        var loc = new BusinessLocation { Name = "Second workshop" }; // BusinessId left empty
        asA.Locations.Add(loc);
        await asA.SaveChangesAsync();
        Assert.Equal(a.BusinessId, loc.BusinessId);
    }

    [Fact]
    public async Task Database_rejects_a_membership_role_that_links_two_businesses()
    {
        var a = await SeedBusinessAsync("eta-" + Guid.NewGuid().ToString("N")[..6]);
        var b = await SeedBusinessAsync("theta-" + Guid.NewGuid().ToString("N")[..6]);

        // Bypass the app-level guard (no tenant) to prove the composite FK itself holds the line.
        await using var raw = fx.Create(null);
        raw.MembershipRoles.Add(new MembershipRole
        {
            BusinessId = a.BusinessId, MembershipId = a.MembershipId, RoleId = b.RoleId // role belongs to B
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
    }

    [Fact]
    public void Owner_role_holds_every_permission_and_other_defaults_are_not_owner()
    {
        var roles = DefaultRoles.Create(Guid.NewGuid());
        var owner = roles.Single(r => r.Name == DefaultRoles.Owner);
        Assert.Equal(Permissions.All.Count, owner.Permissions.Count);
        Assert.DoesNotContain(roles.Where(r => r.Name != DefaultRoles.Owner),
            r => r.Permissions.Any(p => p.Permission == Permissions.BusinessSettings));
    }
}
