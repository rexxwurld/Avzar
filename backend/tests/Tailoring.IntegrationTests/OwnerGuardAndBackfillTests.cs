using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Access;
using Tailoring.Domain.Audit;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Persistence.Migrations;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// The database allows at most one owner per business, and the "locations.all" back-fill SQL from the migration does what it says.
/// (The backend tests build the schema with EnsureCreated, so the migration's own SQL is run here directly.)
/// </summary>
public sealed class OwnerGuardAndBackfillTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private async Task<Guid> NewBusinessAsync()
    {
        await using var db = fx.Create(null);
        var b = new Business { Name = "Biz " + Tag() };
        db.Businesses.Add(b);
        await db.SaveChangesAsync();
        return b.Id;
    }

    private async Task<Guid> NewMembershipAsync(Guid businessId, bool owner)
    {
        await using var db = fx.Create(null);
        var t = Tag();
        var user = new AppUser { UserName = $"{t}@test.local", Email = $"{t}@test.local", FullName = t };
        db.Users.Add(user);
        var m = new BusinessMembership { BusinessId = businessId, UserId = user.Id, DisplayName = t, IsOwner = owner };
        db.Memberships.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    [Fact]
    public async Task A_second_owner_in_the_same_business_is_rejected_by_the_database()
    {
        var business = await NewBusinessAsync();
        await NewMembershipAsync(business, owner: true);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => NewMembershipAsync(business, owner: true));
        Assert.Contains("UX_Memberships_OneOwner", ex.ToString());
    }

    [Fact]
    public async Task Other_businesses_may_each_have_an_owner_and_any_number_of_non_owners()
    {
        var a = await NewBusinessAsync();
        var b = await NewBusinessAsync();
        await NewMembershipAsync(a, owner: true);
        await NewMembershipAsync(b, owner: true);
        for (var i = 0; i < 3; i++) await NewMembershipAsync(a, owner: false);

        await using var db = fx.Create(null);
        Assert.Equal(1, await db.Memberships.IgnoreQueryFilters().CountAsync(m => m.BusinessId == a && m.IsOwner));
        Assert.Equal(4, await db.Memberships.IgnoreQueryFilters().CountAsync(m => m.BusinessId == a));
    }

    [Fact]
    public async Task Ownership_can_move_when_the_old_owner_is_saved_first_inside_a_transaction()
    {
        var business = await NewBusinessAsync();
        var from = await NewMembershipAsync(business, owner: true);
        var to = await NewMembershipAsync(business, owner: false);

        await using var db = fx.Create(null);
        await using var tx = await db.Database.BeginTransactionAsync();
        var oldOwner = await db.Memberships.IgnoreQueryFilters().AsTracking().SingleAsync(m => m.Id == from);
        var heir = await db.Memberships.IgnoreQueryFilters().AsTracking().SingleAsync(m => m.Id == to);
        oldOwner.IsOwner = false;
        await db.SaveChangesAsync();
        heir.IsOwner = true;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await using var check = fx.Create(null);
        var owner = await check.Memberships.IgnoreQueryFilters().SingleAsync(m => m.BusinessId == business && m.IsOwner);
        Assert.Equal(to, owner.Id);
    }

    // ---------- locations.all back-fill ----------

    /// <summary>A business whose system roles are as they were before "locations.all" existed: Manager and Accountant without it.</summary>
    private async Task<(Guid BusinessId, Guid ManagerRoleId)> OldBusinessAsync(bool customManager = false, bool managerEditedViaApi = false)
    {
        var businessId = await NewBusinessAsync();
        await using var db = fx.Create(null);
        var roles = DefaultRoles.Create(businessId);
        BusinessRole manager;
        if (customManager)
        {
            roles.RemoveAll(r => r.Name == DefaultRoles.Manager);
            manager = new BusinessRole { BusinessId = businessId, Name = DefaultRoles.Manager, IsSystem = false };
            manager.Permissions = [new BusinessRolePermission { RoleId = manager.Id, BusinessId = businessId, Permission = Permissions.OrdersView }];
            roles.Add(manager);
        }
        else
        {
            manager = roles.Single(r => r.Name == DefaultRoles.Manager);
            manager.Permissions.RemoveAll(p => p.Permission == Permissions.LocationsAll);
        }
        var accountant = roles.Single(r => r.Name == DefaultRoles.Accountant);
        accountant.Permissions.RemoveAll(p => p.Permission == Permissions.LocationsAll);
        db.BusinessRoles.AddRange(roles);
        if (managerEditedViaApi)
            db.AuditLogs.Add(new AuditLog { BusinessId = businessId, Action = "role.permissions_changed", EntityType = nameof(BusinessRole), EntityId = manager.Id });
        await db.SaveChangesAsync();
        return (businessId, manager.Id);
    }

    private async Task<List<string>> HoldersOfLocationsAllAsync(Guid businessId)
    {
        await using var db = fx.Create(null);
        return await (from p in db.RolePermissions.IgnoreQueryFilters()
                      join r in db.BusinessRoles.IgnoreQueryFilters() on p.RoleId equals r.Id
                      where p.BusinessId == businessId && p.Permission == Permissions.LocationsAll
                      orderby r.Name
                      select r.Name).ToListAsync();
    }

    private async Task RunBackfillAsync()
    {
        await using var db = fx.Create(null);
        await db.Database.ExecuteSqlRawAsync(AddOneOwnerGuardAndLocationsAllBackfill.BackfillLocationsAllSql);
    }

    [Fact]
    public async Task The_backfill_gives_untouched_Manager_and_Accountant_system_roles_the_permission_and_is_repeatable()
    {
        var (business, _) = await OldBusinessAsync();
        Assert.Equal(new[] { "Owner" }, await HoldersOfLocationsAllAsync(business));

        await RunBackfillAsync();
        Assert.Equal(new[] { "Accountant", "Manager", "Owner" }, await HoldersOfLocationsAllAsync(business));

        await RunBackfillAsync(); // idempotent: no duplicate-key error, no change
        Assert.Equal(new[] { "Accountant", "Manager", "Owner" }, await HoldersOfLocationsAllAsync(business));
    }

    [Fact]
    public async Task A_role_whose_permissions_were_edited_through_the_API_is_left_alone()
    {
        var (business, _) = await OldBusinessAsync(managerEditedViaApi: true);
        await RunBackfillAsync();
        Assert.Equal(new[] { "Accountant", "Owner" }, await HoldersOfLocationsAllAsync(business)); // Manager skipped: the omission may be deliberate
    }

    [Fact]
    public async Task A_custom_role_that_merely_has_a_system_role_name_is_left_alone()
    {
        var (business, _) = await OldBusinessAsync(customManager: true);
        await RunBackfillAsync();
        Assert.Equal(new[] { "Accountant", "Owner" }, await HoldersOfLocationsAllAsync(business));
    }

    [Fact]
    public async Task The_backfill_never_touches_other_roles()
    {
        var (business, _) = await OldBusinessAsync();
        await RunBackfillAsync();
        await using var db = fx.Create(null);
        var tailor = await db.BusinessRoles.IgnoreQueryFilters().Include(r => r.Permissions).SingleAsync(r => r.BusinessId == business && r.Name == DefaultRoles.Tailor);
        Assert.Equal(new[] { Permissions.TasksWork }, tailor.Permissions.Select(p => p.Permission).ToList());
    }
}
