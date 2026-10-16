using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Common;
using Tailoring.Application.Finance;
using Tailoring.Application.Locations;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// An expense can be tied to a location. Staff limited to some locations see only expenses at those locations (list, get, summary);
/// an expense with no location is business-wide and visible only to callers who see every location. A hidden expense looks exactly
/// like a missing one. Staff limited to some locations must pick one of their own locations when they record or move an expense.
/// </summary>
public sealed class ExpenseLocationScopeTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record World(Guid BusinessId, Guid LocA, Guid LocB, Guid Category, Guid ExpenseA, Guid ExpenseB, Guid ExpenseWide);

    private ExpenseService Svc(Guid bid, Guid? userId)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, userId);
        ILocationScope? scope = userId is null ? null : new LocationScope(db, t, new PermissionService(db));
        return new ExpenseService(db, t, new AuditLogger(db), new EntitlementService(db), scope);
    }

    private static UpsertExpenseRequest Req(Guid category, Guid? location, decimal amount = 100m, string description = "Thread and buttons") =>
        new() { CategoryId = category, ExpenseDate = DateOnly.FromDateTime(DateTime.UtcNow), Amount = amount, Description = description, LocationId = location };

    private async Task<World> SeedAsync()
    {
        Guid bid, la, lb;
        await using (var db = fx.Create(null))
        {
            var plan = await db.Plans.FirstAsync(x => x.Code == "starter");
            var business = new Business { Name = "ExpLoc-" + Guid.NewGuid().ToString("N")[..8], CurrencyCode = "NGN", TimeZoneId = "UTC" };
            db.Businesses.Add(business);
            db.Subscriptions.Add(new TenantSubscription { BusinessId = business.Id, PlanId = plan.Id });
            var a = new BusinessLocation { BusinessId = business.Id, Name = "Lekki" };
            var b = new BusinessLocation { BusinessId = business.Id, Name = "Ikeja" };
            db.Locations.AddRange(a, b);
            await db.SaveChangesAsync();
            (bid, la, lb) = (business.Id, a.Id, b.Id);
        }

        // Setup runs with no signed-in user, so it is not location-scoped.
        var category = (await Svc(bid, null).ListCategoriesAsync()).First().Id;
        var ea = await Svc(bid, null).CreateAsync(Req(category, la, 100m, "Lekki rent"));
        var eb = await Svc(bid, null).CreateAsync(Req(category, lb, 200m, "Ikeja rent"));
        var ew = await Svc(bid, null).CreateAsync(Req(category, null, 400m, "Head office insurance"));
        return new World(bid, la, lb, category, ea.Id, eb.Id, ew.Id);
    }

    private async Task<Guid> StaffAsync(Guid businessId, string[] permissions, Guid[] locations, bool owner = false)
    {
        await using var db = fx.Create(null);
        var email = $"staff-{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, FullName = "Staff" };
        db.Users.Add(user);

        var role = new BusinessRole { BusinessId = businessId, Name = "R-" + Guid.NewGuid().ToString("N")[..8] };
        role.Permissions = permissions.Select(p => new BusinessRolePermission { RoleId = role.Id, BusinessId = businessId, Permission = p }).ToList();
        db.BusinessRoles.Add(role);

        var membership = new BusinessMembership
        {
            BusinessId = businessId, UserId = user.Id, DisplayName = "Staff", IsOwner = owner, Status = MembershipStatus.Active
        };
        db.Memberships.Add(membership);
        db.MembershipRoles.Add(new MembershipRole { MembershipId = membership.Id, RoleId = role.Id, BusinessId = businessId });
        foreach (var loc in locations)
            db.MembershipLocations.Add(new MembershipLocation { MembershipId = membership.Id, LocationId = loc, BusinessId = businessId });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static readonly string[] ExpenseAccess = [Permissions.ExpensesView, Permissions.ExpensesManage];

    private static async Task<AppException> Fails(Func<Task> action) => await Assert.ThrowsAsync<AppException>(action);

    [Fact]
    public async Task Staff_see_only_expenses_at_their_locations()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ExpenseAccess, [w.LocA]);

        var list = await Svc(w.BusinessId, user).ListAsync(new ExpenseListQuery());
        Assert.Equal(w.ExpenseA, Assert.Single(list.Items).Id); // not B, and not the business-wide one
        Assert.Equal(w.LocA, list.Items[0].LocationId);
        Assert.Equal(w.ExpenseA, (await Svc(w.BusinessId, user).GetAsync(w.ExpenseA)).Id);

        var hiddenB = await Fails(() => Svc(w.BusinessId, user).GetAsync(w.ExpenseB));
        Assert.Equal(404, hiddenB.StatusCode);
        Assert.Equal("expense_not_found", hiddenB.Code);
        Assert.Equal("expense_not_found", (await Fails(() => Svc(w.BusinessId, user).GetAsync(w.ExpenseWide))).Code);
    }

    [Fact]
    public async Task The_summary_only_counts_what_the_caller_can_see()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ExpenseAccess, [w.LocA]);

        var scoped = await Svc(w.BusinessId, user).SummaryAsync(null, null);
        Assert.Equal(100m, scoped.Total);

        var everything = await Svc(w.BusinessId, null).SummaryAsync(null, null);
        Assert.Equal(700m, everything.Total);

        var onlyB = await Svc(w.BusinessId, null).SummaryAsync(null, null, default, w.LocB);
        Assert.Equal(200m, onlyB.Total);

        // Asking for a hidden location does not reveal anything: restricted staff just get nothing for it.
        var hidden = await Svc(w.BusinessId, user).SummaryAsync(null, null, default, w.LocB);
        Assert.Equal(0m, hidden.Total);
    }

    [Fact]
    public async Task The_list_can_be_filtered_by_location()
    {
        var w = await SeedAsync();
        var onlyB = await Svc(w.BusinessId, null).ListAsync(new ExpenseListQuery { LocationId = w.LocB });
        Assert.Equal(w.ExpenseB, Assert.Single(onlyB.Items).Id);
        Assert.Equal(3, (await Svc(w.BusinessId, null).ListAsync(new ExpenseListQuery())).TotalCount);
    }

    [Fact]
    public async Task Scoped_staff_must_record_expenses_at_one_of_their_locations()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ExpenseAccess, [w.LocA]);

        var created = await Svc(w.BusinessId, user).CreateAsync(Req(w.Category, w.LocA, 55m, "Needles"));
        Assert.Equal(w.LocA, created.LocationId);

        var other = await Fails(() => Svc(w.BusinessId, user).CreateAsync(Req(w.Category, w.LocB)));
        Assert.Equal(403, other.StatusCode);
        Assert.Equal("location_forbidden", other.Code);

        var none = await Fails(() => Svc(w.BusinessId, user).CreateAsync(Req(w.Category, null)));
        Assert.Equal("location_forbidden", none.Code); // fail closed: a business-wide expense is not theirs to record
    }

    [Fact]
    public async Task Unrestricted_callers_can_record_business_wide_expenses_but_not_at_unknown_locations()
    {
        var w = await SeedAsync();
        var wide = await Svc(w.BusinessId, null).CreateAsync(Req(w.Category, null, 10m, "Accounting software"));
        Assert.Null(wide.LocationId);

        var unknown = await Fails(() => Svc(w.BusinessId, null).CreateAsync(Req(w.Category, Guid.NewGuid())));
        Assert.Equal(400, unknown.StatusCode);
        Assert.Equal("unknown_location", unknown.Code);
    }

    [Fact]
    public async Task Hidden_expenses_cannot_be_edited_or_voided_and_a_move_must_stay_inside_the_callers_locations()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ExpenseAccess, [w.LocA]);

        var voided = await Fails(() => Svc(w.BusinessId, user).VoidAsync(w.ExpenseB, new VoidExpenseRequest { Reason = "not allowed" }));
        Assert.Equal("expense_not_found", voided.Code);

        var edit = await Fails(() => Svc(w.BusinessId, user).UpdateAsync(w.ExpenseB, Req(w.Category, w.LocB) with { Version = 1 }));
        Assert.Equal("expense_not_found", edit.Code);

        var own = await Svc(w.BusinessId, user).GetAsync(w.ExpenseA);
        var move = await Fails(() => Svc(w.BusinessId, user).UpdateAsync(w.ExpenseA, Req(w.Category, w.LocB) with { Version = own.Version }));
        Assert.Equal("location_forbidden", move.Code);
    }

    [Fact]
    public async Task Omitting_the_location_on_update_keeps_it()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ExpenseAccess, [w.LocA]);

        var current = await Svc(w.BusinessId, user).GetAsync(w.ExpenseA);
        var updated = await Svc(w.BusinessId, user).UpdateAsync(w.ExpenseA, Req(w.Category, null, 150m, "Lekki rent (revised)") with { Version = current.Version });
        Assert.Equal(w.LocA, updated.LocationId);
        Assert.Equal(150m, updated.Amount);
    }

    [Fact]
    public async Task Owners_and_holders_of_locations_all_see_every_expense_including_business_wide_ones()
    {
        var w = await SeedAsync();
        var owner = await StaffAsync(w.BusinessId, [], [], owner: true);
        var all = await StaffAsync(w.BusinessId, [.. ExpenseAccess, Permissions.LocationsAll], []);

        foreach (var user in new[] { owner, all })
        {
            Assert.Equal(3, (await Svc(w.BusinessId, user).ListAsync(new ExpenseListQuery())).TotalCount);
            Assert.Equal(w.ExpenseWide, (await Svc(w.BusinessId, user).GetAsync(w.ExpenseWide)).Id);
            Assert.Equal(700m, (await Svc(w.BusinessId, user).SummaryAsync(null, null)).Total);
        }
    }

    [Fact]
    public async Task A_member_with_no_locations_sees_no_expenses()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ExpenseAccess, []);

        Assert.Empty((await Svc(w.BusinessId, user).ListAsync(new ExpenseListQuery())).Items);
        Assert.Equal(0m, (await Svc(w.BusinessId, user).SummaryAsync(null, null)).Total);
        Assert.Equal("expense_not_found", (await Fails(() => Svc(w.BusinessId, user).GetAsync(w.ExpenseA))).Code);
    }
}
