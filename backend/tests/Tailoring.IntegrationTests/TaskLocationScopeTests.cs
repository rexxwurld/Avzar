using Tailoring.Application.Common;
using Tailoring.Application.Locations;
using Tailoring.Application.Orders;
using Tailoring.Application.Production;
using Tailoring.Domain.Access;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Production;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// A production task follows the location of its order. Staff limited to some locations (not the owner, no locations.all)
/// see and change only those; a hidden task or order looks exactly like a missing one, and a rejected change leaves the task untouched.
/// </summary>
public sealed class TaskLocationScopeTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record World(Guid BusinessId, Guid LocA, Guid LocB, Guid OrderA, Guid OrderB,
        List<TaskSummaryDto> TasksA, List<TaskSummaryDto> TasksB);

    private static readonly string[] TaskViewers = [Permissions.TasksView, Permissions.TasksManage, Permissions.TasksAssign, Permissions.OrdersView];

    private OrderService Orders(Guid bid)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, null);
        return new OrderService(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new EntitlementService(db),
            new PricingService(db, t, new AuditLogger(db)), new AllowAllPermissions(), new UnrestrictedLocationScope());
    }

    /// <summary>With no signed-in user there is deliberately no location scope (setup and verification only).</summary>
    private TaskService Tasks(Guid bid, Guid? userId)
    {
        var db = fx.Create(bid);
        var t = new FakeUserTenant(bid, userId);
        ILocationScope? scope = userId is null ? null : new LocationScope(db, t, new PermissionService(db));
        return new TaskService(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new PermissionService(db), scope);
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
            var business = new Business { Name = "TaskLoc-" + Guid.NewGuid().ToString("N")[..8], CurrencyCode = "USD", TimeZoneId = "UTC" };
            var a = new BusinessLocation { BusinessId = business.Id, Name = "Lekki" };
            var b = new BusinessLocation { BusinessId = business.Id, Name = "Ikeja" };
            var customer = new Customer { BusinessId = business.Id, FullName = "Ada Obi" };
            var (workflow, links) = DefaultWorkflows.Create(business.Id);
            db.Businesses.Add(business);
            db.Locations.AddRange(a, b);
            db.Customers.Add(customer);
            db.Workflows.Add(workflow);
            db.StageDependencies.AddRange(links);
            await db.SaveChangesAsync();
            (bid, la, lb, cid) = (business.Id, a.Id, b.Id, customer.Id);
        }

        // Created with no signed-in user, so the setup itself is not location-scoped. Confirming an order plans its tasks.
        var (orderA, _) = await Orders(bid).CreateAsync(NewOrder(cid, la), null);
        var (orderB, _) = await Orders(bid).CreateAsync(NewOrder(cid, lb), null);
        foreach (var id in new[] { orderA.Id, orderB.Id })
        {
            await Orders(bid).QuoteAsync(id);
            await Orders(bid).ConfirmAsync(id);
        }

        var tasksA = (await Tasks(bid, null).ListAsync(new TaskListQuery { OrderId = orderA.Id, PageSize = 100 })).Items.ToList();
        var tasksB = (await Tasks(bid, null).ListAsync(new TaskListQuery { OrderId = orderB.Id, PageSize = 100 })).Items.ToList();
        Assert.NotEmpty(tasksA); // the tests below mean nothing without tasks at both locations
        Assert.NotEmpty(tasksB);
        return new World(bid, la, lb, orderA.Id, orderB.Id, tasksA, tasksB);
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

    private static async Task<AppException> Fails(Func<Task> action) => await Assert.ThrowsAsync<AppException>(action);

    private static void AssertNotFound(AppException e, string code)
    {
        Assert.Equal(404, e.StatusCode);
        Assert.Equal(code, e.Code);
    }

    private async Task<TaskSummaryDto> ReloadAsync(World w, Guid taskId)
        => (await Tasks(w.BusinessId, null).GetAsync(taskId)).Task;

    [Fact]
    public async Task Staff_see_only_tasks_of_orders_at_their_location()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, TaskViewers, [w.LocA]);

        var list = await Tasks(w.BusinessId, user).ListAsync(new TaskListQuery { PageSize = 100 });
        Assert.Equal(w.TasksA.Select(t => t.Id).Order(), list.Items.Select(t => t.Id).Order());
        Assert.Equal(w.TasksA.Count, list.TotalCount);

        var own = w.TasksA[0];
        Assert.Equal(own.Id, (await Tasks(w.BusinessId, user).GetAsync(own.Id)).Task.Id);

        AssertNotFound(await Fails(() => Tasks(w.BusinessId, user).GetAsync(w.TasksB[0].Id)), "task_not_found");
    }

    [Fact]
    public async Task A_hidden_task_is_the_same_404_as_a_task_that_does_not_exist()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, TaskViewers, [w.LocA]);

        var hidden = await Fails(() => Tasks(w.BusinessId, user).GetAsync(w.TasksB[0].Id));
        var missing = await Fails(() => Tasks(w.BusinessId, user).GetAsync(Guid.NewGuid()));
        Assert.Equal(missing.StatusCode, hidden.StatusCode);
        Assert.Equal(missing.Code, hidden.Code);
    }

    [Fact]
    public async Task Filters_cannot_be_used_to_reach_tasks_at_a_hidden_location()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, TaskViewers, [w.LocA]);

        var byOrder = await Tasks(w.BusinessId, user).ListAsync(new TaskListQuery { OrderId = w.OrderB, PageSize = 100 });
        Assert.Empty(byOrder.Items);
        Assert.Equal(0, byOrder.TotalCount);

        var byGarment = await Tasks(w.BusinessId, user).ListAsync(new TaskListQuery { GarmentId = w.TasksB[0].OrderGarmentId, PageSize = 100 });
        Assert.Empty(byGarment.Items);
    }

    [Fact]
    public async Task Hidden_tasks_cannot_be_changed_and_stay_untouched()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, TaskViewers, [w.LocA]);
        var target = w.TasksB[0];

        AssertNotFound(await Fails(() => Tasks(w.BusinessId, user)
            .UpdateAsync(target.Id, new UpdateTaskRequest { Version = target.Version, Priority = "Urgent" })), "task_not_found");
        AssertNotFound(await Fails(() => Tasks(w.BusinessId, user)
            .AssignAsync(target.Id, new AssignTaskRequest { MembershipId = null })), "task_not_found");
        AssertNotFound(await Fails(() => Tasks(w.BusinessId, user)
            .UnblockAsync(target.Id, new NoteRequest { Note = "nope" })), "task_not_found");
        AssertNotFound(await Fails(() => Tasks(w.BusinessId, user)
            .CancelAsync(target.Id, new CancelTaskRequest { Reason = "not allowed" })), "task_not_found");

        var after = await ReloadAsync(w, target.Id);
        Assert.Equal(target.Status, after.Status);
        Assert.Equal(target.Priority, after.Priority);
        Assert.Equal(target.Version, after.Version); // nothing was written
    }

    [Fact]
    public async Task A_hidden_task_cannot_be_overridden_even_by_someone_with_the_override_permission()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, [.. TaskViewers, Permissions.TasksOverride], [w.LocA]);

        var e = await Fails(() => Tasks(w.BusinessId, user)
            .OverrideAsync(w.TasksB[0].Id, new OverrideTaskRequest { Action = "Start", Reason = "trying it on" }));
        AssertNotFound(e, "task_not_found"); // and not override_not_permitted / assignee_required, which would confirm it exists
    }

    [Fact]
    public async Task Hidden_orders_cannot_get_new_tasks_or_have_tasks_generated_or_alterations_requested()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, TaskViewers, [w.LocA]);
        var before = w.TasksB.Count;

        AssertNotFound(await Fails(() => Tasks(w.BusinessId, user)
            .CreateAsync(w.OrderB, new CreateTaskRequest { Title = "Extra button check", OrderGarmentId = w.TasksB[0].OrderGarmentId })), "order_not_found");
        AssertNotFound(await Fails(() => Tasks(w.BusinessId, user)
            .GenerateForOrderAsync(w.OrderB, new GenerateTasksRequest())), "order_not_found");
        AssertNotFound(await Fails(() => Tasks(w.BusinessId, user)
            .RequestAlterationsAsync(w.TasksB[0].OrderGarmentId, new RequestAlterationsRequest { Notes = "take in the waist" })), "garment_not_found");

        var after = await Tasks(w.BusinessId, null).ListAsync(new TaskListQuery { OrderId = w.OrderB, PageSize = 100 });
        Assert.Equal(before, after.TotalCount);
    }

    [Fact]
    public async Task Staff_can_still_work_with_tasks_at_their_own_location()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, TaskViewers, [w.LocA]);
        var target = w.TasksA[0];

        var updated = await Tasks(w.BusinessId, user)
            .UpdateAsync(target.Id, new UpdateTaskRequest { Version = target.Version, Priority = "Urgent" });
        Assert.Equal("Urgent", updated.Task.Priority);

        var created = await Tasks(w.BusinessId, user)
            .CreateAsync(w.OrderA, new CreateTaskRequest { Title = "Extra button check", OrderGarmentId = target.OrderGarmentId });
        Assert.Equal(w.OrderA, created.Task.OrderId);
    }

    [Fact]
    public async Task A_member_with_no_locations_sees_no_tasks()
    {
        var w = await SeedAsync();
        var user = await StaffAsync(w.BusinessId, TaskViewers, []);

        var list = await Tasks(w.BusinessId, user).ListAsync(new TaskListQuery { PageSize = 100 });
        Assert.Empty(list.Items);
        Assert.Equal(0, list.TotalCount);
        AssertNotFound(await Fails(() => Tasks(w.BusinessId, user).GetAsync(w.TasksA[0].Id)), "task_not_found");
    }

    [Fact]
    public async Task The_owner_and_holders_of_locations_all_see_every_location()
    {
        var w = await SeedAsync();
        var everything = w.TasksA.Count + w.TasksB.Count;

        var owner = await StaffAsync(w.BusinessId, TaskViewers, [], owner: true);
        var all = await StaffAsync(w.BusinessId, [.. TaskViewers, Permissions.LocationsAll], [w.LocA]);

        foreach (var user in new[] { owner, all })
        {
            var list = await Tasks(w.BusinessId, user).ListAsync(new TaskListQuery { PageSize = 100 });
            Assert.Equal(everything, list.TotalCount);
            Assert.Equal(w.TasksB[0].Id, (await Tasks(w.BusinessId, user).GetAsync(w.TasksB[0].Id)).Task.Id);
        }
    }

    [Fact]
    public async Task Tasks_of_another_business_are_never_visible()
    {
        var one = await SeedAsync();
        var two = await SeedAsync();
        var user = await StaffAsync(one.BusinessId, TaskViewers, [one.LocA, one.LocB]);

        var list = await Tasks(one.BusinessId, user).ListAsync(new TaskListQuery { PageSize = 100 });
        Assert.DoesNotContain(list.Items, t => two.TasksA.Concat(two.TasksB).Any(x => x.Id == t.Id));
        AssertNotFound(await Fails(() => Tasks(one.BusinessId, user).GetAsync(two.TasksA[0].Id)), "task_not_found");
    }
}
