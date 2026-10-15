using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Application.Locations;
using Tailoring.Application.Orders;
using Tailoring.Domain.Access;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// Orders and fittings are the records that carry a location. Staff who are neither the owner nor hold locations.all
/// only see the locations they are assigned to; a hidden record looks exactly like a missing one.
/// </summary>
public sealed class LocationScopeTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record World(Guid BusinessId, Guid LocA, Guid LocB, Guid CustomerId, Guid OrderA, Guid OrderB);

    private ILocationScope? ScopeFor(AppDbContext db, FakeUserTenant t, Guid? userId)
        => userId is null ? null : new LocationScope(db, t, new PermissionService(db));

    private OrderService Orders(Guid bid, Guid? userId)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, userId);
        return new OrderService(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new EntitlementService(db),
            new PricingService(db, t, new AuditLogger(db)), new AllowAllPermissions(), ScopeFor(db, t, userId));
    }

    private FittingService Fittings(Guid bid, Guid? userId)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, userId);
        return new FittingService(db, t, new AuditLogger(db), new EntitlementService(db), ScopeFor(db, t, userId));
    }

    private SearchService Search(Guid bid, Guid userId)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, userId);
        return new SearchService(db, t, new PermissionService(db), ScopeFor(db, t, userId));
    }

    private static CreateOrderRequest NewOrder(Guid customerId, Guid locationId) => new()
    {
        CustomerId = customerId, LocationId = locationId,
        PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
        Garments = [new OrderGarmentInput { Name = "Shirt", Quantity = 1, UnitPrice = 100m }]
    };

    private async Task<World> SeedAsync()
    {
        Guid bid, la, lb, cid;
        await using (var db = fx.Create(null))
        {
            var business = new Business { Name = "Loc-" + Guid.NewGuid().ToString("N")[..8], CurrencyCode = "USD", TimeZoneId = "UTC" };
            db.Businesses.Add(business);
            var a = new BusinessLocation { BusinessId = business.Id, Name = "Lekki" };
            var b = new BusinessLocation { BusinessId = business.Id, Name = "Ikeja" };
            var customer = new Customer { BusinessId = business.Id, FullName = "Ada Obi" };
            db.Locations.AddRange(a, b);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            (bid, la, lb, cid) = (business.Id, a.Id, b.Id, customer.Id);
        }

        // Created with no signed-in user, so the setup itself is not location-scoped.
        var setup = Orders(bid, null);
        var (orderA, _) = await setup.CreateAsync(NewOrder(cid, la), null);
        var (orderB, _) = await setup.CreateAsync(NewOrder(cid, lb), null);
        return new World(bid, la, lb, cid, orderA.Id, orderB.Id);
    }

    private async Task<Guid> StaffAsync(Guid businessId, string[] permissions, Guid[] locations,
        bool owner = false, MembershipStatus status = MembershipStatus.Active)
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
            BusinessId = businessId, UserId = user.Id, DisplayName = "Staff", IsOwner = owner, Status = status,
            DeactivatedAtUtc = status == MembershipStatus.Deactivated ? DateTime.UtcNow : null
        };
        db.Memberships.Add(membership);
        db.MembershipRoles.Add(new MembershipRole { MembershipId = membership.Id, RoleId = role.Id, BusinessId = businessId });
        foreach (var loc in locations)
            db.MembershipLocations.Add(new MembershipLocation { MembershipId = membership.Id, LocationId = loc, BusinessId = businessId });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static readonly string[] ViewOrders = [Permissions.OrdersView, Permissions.CustomersView, Permissions.FittingsManage, Permissions.OrdersManage];

    [Fact]
    public async Task Staff_see_only_orders_at_their_own_location()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ViewOrders, [w.LocA]);

        var list = await Orders(w.BusinessId, user).ListAsync(new OrderListQuery());
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(w.OrderA, Assert.Single(list.Items).Id);

        Assert.Equal(w.OrderA, (await Orders(w.BusinessId, user).GetAsync(w.OrderA)).Id);
    }

    [Fact]
    public async Task An_order_at_another_location_looks_missing_not_forbidden()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ViewOrders, [w.LocA]);

        var get = await Assert.ThrowsAsync<AppException>(() => Orders(w.BusinessId, user).GetAsync(w.OrderB));
        Assert.Equal(404, get.StatusCode);
        Assert.Equal("order_not_found", get.Code);

        var pricing = await Assert.ThrowsAsync<AppException>(() => Orders(w.BusinessId, user).GetPricingAsync(w.OrderB));
        Assert.Equal("order_not_found", pricing.Code);
    }

    [Fact]
    public async Task Hidden_orders_cannot_be_confirmed_or_cancelled()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ViewOrders, [w.LocA]);

        var confirm = await Assert.ThrowsAsync<AppException>(() => Orders(w.BusinessId, user).ConfirmAsync(w.OrderB));
        Assert.Equal("order_not_found", confirm.Code);
        var cancel = await Assert.ThrowsAsync<AppException>(
            () => Orders(w.BusinessId, user).CancelAsync(w.OrderB, new CancelOrderRequest { Reason = "not allowed" }));
        Assert.Equal("order_not_found", cancel.Code);

        // The order is untouched.
        Assert.Equal("Draft", (await Orders(w.BusinessId, null).GetAsync(w.OrderB)).Status);
    }

    [Fact]
    public async Task Staff_cannot_create_an_order_at_another_location()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ViewOrders, [w.LocA]);

        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(w.BusinessId, user).CreateAsync(NewOrder(w.CustomerId, w.LocB), null));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("location_forbidden", ex.Code);

        var ok = await Orders(w.BusinessId, user).CreateAsync(NewOrder(w.CustomerId, w.LocA), null);
        Assert.True(ok.Created);
    }

    [Fact]
    public async Task Staff_cannot_move_an_order_into_a_location_they_do_not_have()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ViewOrders, [w.LocA]);
        var order = await Orders(w.BusinessId, user).GetAsync(w.OrderA);

        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(w.BusinessId, user).UpdateAsync(w.OrderA, new UpdateOrderRequest
        {
            Version = order.Version, LocationId = w.LocB, PromisedDate = order.PromisedDate,
            Garments = [new OrderGarmentInput { Name = "Shirt", Quantity = 1, UnitPrice = 100m }]
        }));
        Assert.Equal("location_forbidden", ex.Code);
        Assert.Equal(w.LocA, (await Orders(w.BusinessId, null).GetAsync(w.OrderA)).LocationId);
    }

    [Fact]
    public async Task A_replayed_idempotency_key_does_not_reveal_an_order_at_a_hidden_location()
    {
        var w = await SeedAsync();
        var admin = await StaffAsync(w.BusinessId, ViewOrders, [], owner: true);
        var user = await StaffAsync(w.BusinessId, ViewOrders, [w.LocA]);

        var key = "key-" + Guid.NewGuid().ToString("N");
        var (created, _) = await Orders(w.BusinessId, admin).CreateAsync(NewOrder(w.CustomerId, w.LocB), key);

        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(w.BusinessId, user).CreateAsync(NewOrder(w.CustomerId, w.LocB), key));
        Assert.Equal("location_forbidden", ex.Code);
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task Staff_with_no_locations_see_nothing()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ViewOrders, []);

        Assert.Equal(0, (await Orders(w.BusinessId, user).ListAsync(new OrderListQuery())).TotalCount);
        await Assert.ThrowsAsync<AppException>(() => Orders(w.BusinessId, user).GetAsync(w.OrderA));
        var ex = await Assert.ThrowsAsync<AppException>(() => Orders(w.BusinessId, user).CreateAsync(NewOrder(w.CustomerId, w.LocA), null));
        Assert.Equal("location_forbidden", ex.Code);
    }

    [Fact]
    public async Task The_all_locations_permission_sees_every_location()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, [.. ViewOrders, Permissions.LocationsAll], []);

        Assert.Equal(2, (await Orders(w.BusinessId, user).ListAsync(new OrderListQuery())).TotalCount);
        Assert.Equal(w.OrderB, (await Orders(w.BusinessId, user).GetAsync(w.OrderB)).Id);
    }

    [Fact]
    public async Task The_owner_sees_every_location()
    {
        var w = await SeedAsync();
        var owner = await StaffAsync(w.BusinessId, [], [], owner: true);

        Assert.Equal(2, (await Orders(w.BusinessId, owner).ListAsync(new OrderListQuery())).TotalCount);
    }

    [Fact]
    public async Task A_deactivated_member_sees_nothing_even_with_locations_assigned()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, [.. ViewOrders, Permissions.LocationsAll], [w.LocA, w.LocB], status: MembershipStatus.Deactivated);

        Assert.Equal(0, (await Orders(w.BusinessId, user).ListAsync(new OrderListQuery())).TotalCount);
    }

    [Fact]
    public async Task A_user_with_no_membership_in_this_business_sees_nothing()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        var outsider = await StaffAsync(other.BusinessId, [.. ViewOrders, Permissions.LocationsAll], []);

        Assert.Equal(0, (await Orders(w.BusinessId, outsider).ListAsync(new OrderListQuery())).TotalCount);
    }

    [Fact]
    public async Task A_membership_and_its_locations_apply_only_to_their_own_business()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ViewOrders, [w.LocA]);
        Assert.Equal(1, (await Orders(w.BusinessId, user).ListAsync(new OrderListQuery())).TotalCount);
        Assert.Equal(0, (await Orders(other.BusinessId, user).ListAsync(new OrderListQuery())).TotalCount);
    }

    [Fact]
    public async Task Calls_with_no_signed_in_user_are_unscoped()
    {
        var w = await SeedAsync();
        Assert.Equal(2, (await Orders(w.BusinessId, null).ListAsync(new OrderListQuery())).TotalCount);
    }

    [Fact]
    public async Task Fittings_are_scoped_to_the_staff_members_locations()
    {
        var w = await SeedAsync();
        var admin = await StaffAsync(w.BusinessId, ViewOrders, [], owner: true);
        var when = DateTimeOffset.UtcNow.AddDays(3);
        var fa = await Fittings(w.BusinessId, admin).CreateAsync(new CreateFittingRequest { CustomerId = w.CustomerId, LocationId = w.LocA, ScheduledAt = when });
        var fb = await Fittings(w.BusinessId, admin).CreateAsync(new CreateFittingRequest { CustomerId = w.CustomerId, LocationId = w.LocB, ScheduledAt = when });

        var user = await StaffAsync(w.BusinessId, ViewOrders, [w.LocA]);
        var list = await Fittings(w.BusinessId, user).ListAsync(new FittingListQuery());
        Assert.Equal(fa.Id, Assert.Single(list.Items).Id);

        var hidden = await Assert.ThrowsAsync<AppException>(
            () => Fittings(w.BusinessId, user).SetStatusAsync(fb.Id, new SetFittingStatusRequest { Status = "Cancelled" }));
        Assert.Equal("fitting_not_found", hidden.Code);

        var create = await Assert.ThrowsAsync<AppException>(() => Fittings(w.BusinessId, user)
            .CreateAsync(new CreateFittingRequest { CustomerId = w.CustomerId, LocationId = w.LocB, ScheduledAt = when }));
        Assert.Equal("location_forbidden", create.Code);

        var linked = await Assert.ThrowsAsync<AppException>(() => Fittings(w.BusinessId, user)
            .CreateAsync(new CreateFittingRequest { CustomerId = w.CustomerId, LocationId = w.LocA, OrderId = w.OrderB, ScheduledAt = when }));
        Assert.Equal("unknown_order", linked.Code);
    }

    [Fact]
    public async Task Global_search_does_not_return_orders_from_hidden_locations()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, ViewOrders, [w.LocA]);

        var scoped = await Search(w.BusinessId, user).SearchAsync("Ada");
        Assert.Equal(w.OrderA, Assert.Single(scoped.Orders).Id);

        var everyone = await StaffAsync(w.BusinessId, [.. ViewOrders, Permissions.LocationsAll], []);
        var all = await Search(w.BusinessId, everyone).SearchAsync("Ada");
        Assert.Equal(2, all.Orders.Count);
    }

    [Fact]
    public void Default_roles_keep_business_wide_access_where_they_had_it()
    {
        var roles = DefaultRoles.Create(Guid.NewGuid());
        bool Has(string role) => roles.Single(r => r.Name == role).Permissions.Any(p => p.Permission == Permissions.LocationsAll);

        Assert.True(Has(DefaultRoles.Owner));
        Assert.True(Has(DefaultRoles.Manager));
        Assert.True(Has(DefaultRoles.Accountant));
        Assert.False(Has(DefaultRoles.Tailor));
        Assert.Contains(Permissions.LocationsAll, Permissions.All);
    }
}
