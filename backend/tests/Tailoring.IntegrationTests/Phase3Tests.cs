using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Orders;
using Tailoring.Application.Production;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Production;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

public sealed class Phase3Tests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record World(Guid BusinessId, Guid LocationId, Guid CustomerId,
        Guid OwnerUser, Guid OwnerMember, Guid CutterUser, Guid CutterMember,
        Guid TailorUser, Guid TailorMember, Guid OutsiderMember);

    // ------------------------------------------------------------------ setup helpers

    private async Task<World> SeedWorldAsync()
    {
        await using var db = fx.Create(null);
        var biz = new Business { Name = "Shop-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "USD", TimeZoneId = "UTC" };
        var loc = new BusinessLocation { BusinessId = biz.Id, Name = "Main" };
        var customer = new Customer { BusinessId = biz.Id, FullName = "Ada Obi" };
        var (workflow, links) = DefaultWorkflows.Create(biz.Id);

        AppUser U(string n) => new() { UserName = $"{n}-{Guid.NewGuid():N}@t.local", Email = $"{n}@t.local", FullName = n };
        var owner = U("owner"); var cutter = U("cutter"); var tailor = U("tailor"); var outsider = U("outsider");
        db.Users.AddRange(owner, cutter, tailor, outsider);

        BusinessMembership M(AppUser u, bool isOwner = false) => new() { BusinessId = biz.Id, UserId = u.Id, DisplayName = u.FullName, IsOwner = isOwner };
        var mo = M(owner, true); var mc = M(cutter); var mt = M(tailor); var mx = M(outsider);

        db.Businesses.Add(biz);
        db.Locations.Add(loc);
        db.Customers.Add(customer);
        db.Workflows.Add(workflow);
        db.StageDependencies.AddRange(links);
        db.Memberships.AddRange(mo, mc, mt, mx);
        await db.SaveChangesAsync();

        foreach (var m in new[] { mo, mc, mt })
            db.MembershipLocations.Add(new MembershipLocation { BusinessId = biz.Id, MembershipId = m.Id, LocationId = loc.Id });
        db.WorkerSkills.Add(new WorkerSkill { BusinessId = biz.Id, MembershipId = mc.Id, Skill = "cutting" });
        db.WorkerSkills.Add(new WorkerSkill { BusinessId = biz.Id, MembershipId = mt.Id, Skill = "sewing" });
        await db.SaveChangesAsync();

        return new World(biz.Id, loc.Id, customer.Id, owner.Id, mo.Id, cutter.Id, mc.Id, tailor.Id, mt.Id, mx.Id);
    }

    private (AppDbContext Db, FakeUserTenant Tenant) Ctx(World w, Guid user)
    {
        var t = new FakeUserTenant(w.BusinessId, user);
        return (fx.CreateFor(t), t);
    }

    private OrderService Orders(World w, Guid? user = null)
    { var (db, t) = Ctx(w, user ?? w.OwnerUser); return new(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new EntitlementService(db), new PricingService(db, t, new AuditLogger(db)), new AllowAllPermissions()); }

    private TaskService Tasks(World w, Guid? user = null)
    { var (db, t) = Ctx(w, user ?? w.OwnerUser); return new(db, t, new AuditLogger(db), new ProductionPlanner(db, t), new PermissionService(db)); }

    private MyTaskService Mine(World w, Guid user)
    { var (db, t) = Ctx(w, user); return new(db, t); }

    private WorkflowService Workflows(World w)
    { var (db, t) = Ctx(w, w.OwnerUser); return new(db, t, new AuditLogger(db)); }

    private static CreateOrderRequest OrderReq(World w, string? category = null, int qty = 1) => new()
    {
        CustomerId = w.CustomerId, LocationId = w.LocationId,
        PromisedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
        Garments = [new OrderGarmentInput { Name = "Shirt", Category = category, Quantity = qty, UnitPrice = 100m }]
    };

    private async Task<OrderDto> ConfirmedOrderAsync(World w, string? category = null, int qty = 1)
    {
        var (order, _) = await Orders(w).CreateAsync(OrderReq(w, category, qty), null);
        await Orders(w).QuoteAsync(order.Id);
        return await Orders(w).ConfirmAsync(order.Id);
    }

    private async Task<List<TaskSummaryDto>> AllTasksAsync(World w, Guid orderId)
        => (await Tasks(w).ListAsync(new TaskListQuery { OrderId = orderId, PageSize = 100 })).Items.ToList();

    private async Task<TaskSummaryDto> TaskAsync(World w, Guid orderId, string stage)
        => (await AllTasksAsync(w, orderId)).Single(t => t.StageName == stage);

    /// <summary>Assign (with a manager override so skills never get in the way), start, tick the checklist, complete.</summary>
    private async Task<TaskDetailDto> RunAsync(World w, Guid taskId, bool alterations = false)
    {
        await Tasks(w).AssignAsync(taskId, new AssignTaskRequest { MembershipId = w.CutterMember, Override = true, Reason = "test run" });
        await Mine(w, w.CutterUser).StartAsync(taskId);
        var detail = await Mine(w, w.CutterUser).GetAsync(taskId);
        return await Mine(w, w.CutterUser).CompleteAsync(taskId,
            new CompleteTaskRequest { CompletedChecks = detail.RequiredChecks.ToList(), AlterationsRequired = alterations });
    }

    private async Task<int> RunAllEligibleAsync(World w, Guid orderId, Func<TaskSummaryDto, bool>? alterationsAt = null)
    {
        var done = 0;
        for (var i = 0; i < 30; i++)
        {
            var next = (await Tasks(w).ListAsync(new TaskListQuery { OrderId = orderId, Eligible = true, PageSize = 100 })).Items.FirstOrDefault();
            if (next is null) break;
            await RunAsync(w, next.Id, alterationsAt?.Invoke(next) ?? false);
            done++;
        }
        return done;
    }

    [Fact]
    public async Task Schedule_preview_apply_uses_postgres_versions_and_queues_deduplicated_assignment_and_reminder_mail()
    {
        var w = await SeedWorldAsync();
        await Workflows(w).CreateAsync(new UpsertWorkflowRequest
        {
            Name = "Single-stage schedule test", GarmentCategories = ["shirt"],
            Stages = [new StageInput { Key = "cut", Name = "Cut", DefaultMinutes = 60 }]
        });
        var order = await ConfirmedOrderAsync(w, category: "shirt");
        var task = Assert.Single(await AllTasksAsync(w, order.Id));
        await Tasks(w).AssignAsync(task.Id, new AssignTaskRequest { MembershipId = w.CutterMember, Override = true, Reason = "schedule integration test" });

        var (db, tenant) = Ctx(w, w.OwnerUser);
        db.BusinessHours.AddRange(Enumerable.Range(0, 7).Select(day => new BusinessHoursDay
        {
            BusinessId = w.BusinessId, DayOfWeek = day, IsOpen = true, OpensAt = new TimeOnly(9, 0), ClosesAt = new TimeOnly(17, 0)
        }));
        db.EntitlementOverrides.Add(new TenantEntitlementOverride
        {
            BusinessId = w.BusinessId, Key = EntitlementKeys.SchedulingShop, Value = "true", Reason = "test fixture"
        });
        await db.SaveChangesAsync();
        var schedule = new ShopScheduleService(db, tenant, new EntitlementService(db),
            new LocationScope(db, tenant, new PermissionService(db)));
        var start = new DateTimeOffset(DateTime.UtcNow.AddDays(3).Date.AddHours(9), TimeSpan.Zero);
        db.WorkerAvailability.AddRange(Enumerable.Range(0, 7).Select(day => new WorkerAvailabilityDay
        {
            BusinessId = w.BusinessId, MembershipId = w.CutterMember, DayOfWeek = day, IsAvailable = true,
            OpensAt = new TimeOnly(9, 0), ClosesAt = new TimeOnly(17, 0), CapacityMinutes = 480
        }));
        db.WorkerLeaves.Add(new WorkerLeave
        {
            BusinessId = w.BusinessId, MembershipId = w.CutterMember, StartsAtUtc = start.UtcDateTime,
            EndsAtUtc = start.UtcDateTime.AddHours(2), Reason = "approved leave during the morning"
        });
        await db.SaveChangesAsync();
        var preview = await schedule.PreviewAsync(new ShopSchedulePreviewRequest { StartUtc = start, HorizonDays = 10 });
        Assert.True(preview.IsPreviewOnly);
        Assert.Empty(preview.Unscheduled);
        var proposedSlot = Assert.Single(preview.Scheduled);
        Assert.Equal(start.UtcDateTime.AddHours(2), proposedSlot.StartUtc); // approved leave blocks 09:00-11:00
        Assert.Null((await db.ProductionTasks.AsNoTracking().SingleAsync(x => x.Id == task.Id)).PlannedStartUtc);

        var applied = await schedule.ApplyAsync(new ShopScheduleApplyRequest
        {
            StartUtc = start, HorizonDays = 10, ExpectedTaskVersions = preview.TaskVersions, ExpectedSlots = preview.Scheduled
        });
        Assert.Equal(1, applied.UpdatedTasks);
        Assert.True(applied.NotificationsQueued);
        var persisted = await db.ProductionTasks.AsNoTracking().SingleAsync(x => x.Id == task.Id);
        Assert.Equal(ProductionTaskStatus.Scheduled, persisted.Status);
        Assert.NotNull(persisted.PlannedStartUtc);
        Assert.Equal(2, await db.EmailOutbox.CountAsync(x => x.BusinessId == w.BusinessId && x.DeduplicationKey != null));

        var stale = await Assert.ThrowsAsync<AppException>(() => schedule.ApplyAsync(new ShopScheduleApplyRequest
        {
            StartUtc = start, HorizonDays = 10, ExpectedTaskVersions = preview.TaskVersions, ExpectedSlots = preview.Scheduled
        }));
        Assert.Equal("schedule_preview_stale", stale.Code);
        Assert.Equal(2, await db.EmailOutbox.CountAsync(x => x.BusinessId == w.BusinessId && x.DeduplicationKey != null));
    }

    [Fact]
    public async Task Worker_schedule_api_service_validates_weekly_availability_and_prevents_overlapping_leave()
    {
        var w = await SeedWorldAsync();
        var (db, tenant) = Ctx(w, w.OwnerUser);
        var service = new WorkerScheduleService(db, tenant, new LocationScope(db, tenant, new PermissionService(db)), new AuditLogger(db));
        var days = Enumerable.Range(0, 7).Select(day => day == 0
            ? new WorkerAvailabilityDayInput(day, false, null, null, 0)
            : new WorkerAvailabilityDayInput(day, true, new TimeOnly(9, 0), new TimeOnly(17, 0), 420)).ToArray();
        var saved = await service.SetAvailabilityAsync(w.CutterMember, days);
        Assert.True(saved.IsConfigured);
        Assert.Equal(7, saved.Days.Count);
        Assert.False(saved.Days.Single(x => x.DayOfWeek == 0).IsAvailable);

        var start = DateTimeOffset.UtcNow.AddDays(5);
        start = new DateTimeOffset(start.Year, start.Month, start.Day, 9, 0, 0, TimeSpan.Zero);
        var request = new WorkerLeaveCreateRequest { StartsAtUtc = start, EndsAtUtc = start.AddHours(3), Reason = "Annual leave" };
        var created = await service.AddLeaveAsync(w.CutterMember, request);
        Assert.Equal("Approved", created.Status);
        var overlap = await Assert.ThrowsAsync<AppException>(() => service.AddLeaveAsync(w.CutterMember,
            new WorkerLeaveCreateRequest { StartsAtUtc = start.AddHours(1), EndsAtUtc = start.AddHours(4), Reason = "Overlapping leave" }));
        Assert.Equal("worker_leave_overlap", overlap.Code);
        await service.CancelLeaveAsync(w.CutterMember, created.Id);
        var listed = await service.ListLeaveAsync(w.CutterMember, start.AddDays(-1), start.AddDays(1));
        Assert.Equal("Cancelled", Assert.Single(listed).Status);
    }

    // ------------------------------------------------------------------ generation

    [Fact]
    public async Task Confirming_an_order_generates_tasks_from_the_default_workflow()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);

        var tasks = await AllTasksAsync(w, order.Id);
        Assert.Equal(10, tasks.Count);
        Assert.Equal("Cancelled", tasks.Single(t => t.StageName == "Alterations").Status);
        Assert.Equal(1, tasks.Single(t => t.StageName == "Sewing").UnmetDependencies);

        var eligible = tasks.Where(t => t.IsEligible).Select(t => t.StageName).ToList();
        Assert.Single(eligible);
        Assert.Equal("Measurements Verified", eligible[0]);

        var progress = (await Orders(w).GetAsync(order.Id)).Progress!;
        Assert.Equal(9, progress.TotalTasks);          // the not-required alterations step is excluded
        Assert.Equal(0, progress.PercentComplete);
    }

    [Fact]
    public async Task Category_specific_workflow_is_used_and_missing_estimates_are_flagged_not_invented()
    {
        var w = await SeedWorldAsync();
        await Workflows(w).CreateAsync(new UpsertWorkflowRequest
        {
            Name = "Shirts", GarmentCategories = ["shirt"],
            Stages = [new StageInput { Key = "cut", Name = "Cut" }, new StageInput { Key = "sew", Name = "Sew" }]
        });

        var order = await ConfirmedOrderAsync(w, category: "Shirt");
        var tasks = await AllTasksAsync(w, order.Id);
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.True(t.NeedsEstimate));
        Assert.Equal(1, tasks.Single(t => t.StageName == "Sew").UnmetDependencies);   // default: depends on previous stage
    }

    [Fact]
    public async Task Workflow_with_a_dependency_cycle_is_rejected()
    {
        var w = await SeedWorldAsync();
        var ex = await Assert.ThrowsAsync<AppException>(() => Workflows(w).CreateAsync(new UpsertWorkflowRequest
        {
            Name = "Loop",
            Stages = [new StageInput { Key = "a", Name = "A", DependsOn = ["b"] }, new StageInput { Key = "b", Name = "B", DependsOn = ["a"] }]
        }));
        Assert.Equal("invalid_workflow", ex.Code);
        Assert.Contains("cycle", ex.Message);
    }

    // ------------------------------------------------------------------ dependencies and worker actions

    [Fact]
    public async Task A_task_cannot_start_before_its_prerequisites_are_done()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var cutting = await TaskAsync(w, order.Id, "Cutting");
        await Tasks(w).AssignAsync(cutting.Id, new AssignTaskRequest { MembershipId = w.CutterMember });

        var ex = await Assert.ThrowsAsync<AppException>(() => Mine(w, w.CutterUser).StartAsync(cutting.Id));
        Assert.Equal("dependencies_incomplete", ex.Code);
        Assert.Contains("Materials Prepared", ex.Message);
    }

    [Fact]
    public async Task Completing_a_task_unlocks_the_next_and_requires_the_checklist()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var first = await TaskAsync(w, order.Id, "Measurements Verified");
        await Tasks(w).AssignAsync(first.Id, new AssignTaskRequest { MembershipId = w.CutterMember });
        await Mine(w, w.CutterUser).StartAsync(first.Id);

        var skipChecks = await Assert.ThrowsAsync<AppException>(() =>
            Mine(w, w.CutterUser).CompleteAsync(first.Id, new CompleteTaskRequest()));
        Assert.Equal("checks_incomplete", skipChecks.Code);

        await Mine(w, w.CutterUser).CompleteAsync(first.Id,
            new CompleteTaskRequest { CompletedChecks = ["Measurements match the order snapshot"] });

        Assert.True((await TaskAsync(w, order.Id, "Materials Prepared")).IsEligible);
        Assert.Equal("InProduction", (await Orders(w).GetAsync(order.Id)).Status);
    }

    [Fact]
    public async Task A_worker_cannot_see_or_act_on_someone_elses_task()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var first = await TaskAsync(w, order.Id, "Measurements Verified");
        await Tasks(w).AssignAsync(first.Id, new AssignTaskRequest { MembershipId = w.CutterMember });

        var ex = await Assert.ThrowsAsync<AppException>(() => Mine(w, w.TailorUser).StartAsync(first.Id));
        Assert.Equal("task_not_found", ex.Code);
        Assert.Empty((await Mine(w, w.TailorUser).ListAsync(new MyTaskListQuery())).Items);
        Assert.Single((await Mine(w, w.CutterUser).ListAsync(new MyTaskListQuery())).Items);
    }

    [Fact]
    public async Task Worker_view_never_includes_customer_personal_data()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var first = await TaskAsync(w, order.Id, "Measurements Verified");
        await Tasks(w).AssignAsync(first.Id, new AssignTaskRequest { MembershipId = w.CutterMember });

        Assert.Null((await Mine(w, w.CutterUser).GetAsync(first.Id)).CustomerName);
        Assert.Equal("Ada Obi", (await Tasks(w).GetAsync(first.Id)).CustomerName);
    }

    [Fact]
    public async Task A_worker_can_only_have_one_task_in_progress()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var a = await Tasks(w).CreateAsync(order.Id, new CreateTaskRequest { Title = "Re-cut collar" });
        var b = await Tasks(w).CreateAsync(order.Id, new CreateTaskRequest { Title = "Replace button" });
        foreach (var t in new[] { a, b })
            await Tasks(w).AssignAsync(t.Task.Id, new AssignTaskRequest { MembershipId = w.CutterMember });

        await Mine(w, w.CutterUser).StartAsync(a.Task.Id);
        var ex = await Assert.ThrowsAsync<AppException>(() => Mine(w, w.CutterUser).StartAsync(b.Task.Id));
        Assert.Equal("task_in_progress", ex.Code);

        await Mine(w, w.CutterUser).PauseAsync(a.Task.Id, new NoteRequest { Note = "lunch" });
        Assert.Equal("InProgress", (await Mine(w, w.CutterUser).StartAsync(b.Task.Id)).Task.Status);
    }

    [Fact]
    public async Task Reporting_an_issue_blocks_the_task_and_a_manager_can_unblock_it()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var t = await Tasks(w).CreateAsync(order.Id, new CreateTaskRequest { Title = "Re-cut collar" });
        await Tasks(w).AssignAsync(t.Task.Id, new AssignTaskRequest { MembershipId = w.CutterMember });
        await Mine(w, w.CutterUser).StartAsync(t.Task.Id);

        var blocked = await Mine(w, w.CutterUser).ReportIssueAsync(t.Task.Id,
            new ReportIssueRequest { Type = "MissingMaterials", Description = "No black thread" });
        Assert.Equal("Blocked", blocked.Task.Status);
        Assert.Equal("MissingMaterials", blocked.Task.BlockedType);

        var cannotStart = await Assert.ThrowsAsync<AppException>(() => Mine(w, w.CutterUser).ResumeAsync(t.Task.Id));
        Assert.Equal("invalid_transition", cannotStart.Code);

        var unblocked = await Tasks(w).UnblockAsync(t.Task.Id, new NoteRequest { Note = "Thread bought" });
        Assert.Equal("Paused", unblocked.Task.Status);     // had already started, so the worker must resume it explicitly
        Assert.Equal("InProgress", (await Mine(w, w.CutterUser).ResumeAsync(t.Task.Id)).Task.Status);
    }

    // ------------------------------------------------------------------ assignment rules and overrides

    [Fact]
    public async Task Assignment_requires_skill_and_workshop_unless_an_authorised_manager_overrides_with_a_reason()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var cutting = await TaskAsync(w, order.Id, "Cutting");

        var wrongSkill = await Assert.ThrowsAsync<AppException>(() =>
            Tasks(w).AssignAsync(cutting.Id, new AssignTaskRequest { MembershipId = w.TailorMember }));
        Assert.Equal("assignment_not_allowed", wrongSkill.Code);

        var outsider = await Assert.ThrowsAsync<AppException>(() =>
            Tasks(w).AssignAsync(cutting.Id, new AssignTaskRequest { MembershipId = w.OutsiderMember }));
        Assert.Contains("workshop", outsider.Message);

        // A worker without tasks.override cannot force it.
        var notPermitted = await Assert.ThrowsAsync<AppException>(() =>
            Tasks(w, w.TailorUser).AssignAsync(cutting.Id, new AssignTaskRequest { MembershipId = w.TailorMember, Override = true, Reason = "I want it" }));
        Assert.Equal("override_not_permitted", notPermitted.Code);

        // The owner needs a real reason.
        var noReason = await Assert.ThrowsAsync<AppException>(() =>
            Tasks(w).AssignAsync(cutting.Id, new AssignTaskRequest { MembershipId = w.TailorMember, Override = true, Reason = "x" }));
        Assert.Equal("reason_required", noReason.Code);

        var ok = await Tasks(w).AssignAsync(cutting.Id,
            new AssignTaskRequest { MembershipId = w.TailorMember, Override = true, Reason = "Cutter is on leave" });
        Assert.Equal(w.TailorMember, ok.Task.AssigneeMembershipId);
        Assert.Contains(ok.Events, e => e.Type == TaskEventTypes.OverrideAssign);

        await using var db = fx.Create(w.BusinessId);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "task.override_assign" && a.EntityId == cutting.Id));
    }

    [Fact]
    public async Task Manager_override_completes_a_task_past_its_prerequisites_but_only_with_permission_and_reason()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var sewing = await TaskAsync(w, order.Id, "Sewing");   // prerequisites are NOT done
        await Tasks(w).AssignAsync(sewing.Id, new AssignTaskRequest { MembershipId = w.TailorMember });

        var denied = await Assert.ThrowsAsync<AppException>(() =>
            Tasks(w, w.TailorUser).OverrideAsync(sewing.Id, new OverrideTaskRequest { Action = "Complete", Reason = "just do it" }));
        Assert.Equal("override_not_permitted", denied.Code);

        var shortReason = await Assert.ThrowsAsync<AppException>(() =>
            Tasks(w).OverrideAsync(sewing.Id, new OverrideTaskRequest { Action = "Complete", Reason = "ok" }));
        Assert.Equal("reason_required", shortReason.Code);

        var done = await Tasks(w).OverrideAsync(sewing.Id, new OverrideTaskRequest { Action = "Complete", Reason = "Customer supplied pre-sewn panel" });
        Assert.Equal("Completed", done.Task.Status);
        Assert.Contains(done.Events, e => e.Type == TaskEventTypes.OverrideComplete && e.Note!.Contains("pre-sewn"));

        await using var db = fx.Create(w.BusinessId);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "task.override.complete" && a.EntityId == sewing.Id));
    }

    // ------------------------------------------------------------------ full lifecycle

    [Fact]
    public async Task Finishing_every_task_makes_the_order_ready_and_only_then_can_it_be_delivered()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);

        var early = await Assert.ThrowsAsync<AppException>(() => Orders(w).DeliverAsync(order.Id));
        Assert.Equal("invalid_transition", early.Code);

        Assert.Equal(9, await RunAllEligibleAsync(w, order.Id));

        var ready = await Orders(w).GetAsync(order.Id);
        Assert.Equal("Ready", ready.Status);
        Assert.Equal(100, ready.Progress!.PercentComplete);
        Assert.Equal("Cancelled", (await TaskAsync(w, order.Id, "Alterations")).Status);   // never needed

        var delivered = await Orders(w).DeliverAsync(order.Id);
        Assert.Equal("Delivered", delivered.Status);
        Assert.NotNull(delivered.DeliveredAtUtc);
    }

    [Fact]
    public async Task A_fitting_can_request_alterations_which_must_finish_before_final_quality_check()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);

        // Work up to and including the fitting, requesting alterations there.
        for (var i = 0; i < 30; i++)
        {
            var next = (await Tasks(w).ListAsync(new TaskListQuery { OrderId = order.Id, Eligible = true, PageSize = 100 })).Items.First();
            var isFitting = next.StageName == "Fitting";
            await RunAsync(w, next.Id, alterations: isFitting);
            if (isFitting) break;
        }

        var alterations = await TaskAsync(w, order.Id, "Alterations");
        Assert.True(alterations.IsEligible);
        Assert.Equal(1, (await TaskAsync(w, order.Id, "Final Quality Check")).UnmetDependencies);

        var finalQc = await TaskAsync(w, order.Id, "Final Quality Check");
        await Tasks(w).AssignAsync(finalQc.Id, new AssignTaskRequest { MembershipId = w.CutterMember, Override = true, Reason = "test override" });
        var tooEarly = await Assert.ThrowsAsync<AppException>(() => Mine(w, w.CutterUser).StartAsync(finalQc.Id));
        Assert.Equal("dependencies_incomplete", tooEarly.Code);

        await RunAllEligibleAsync(w, order.Id);
        Assert.Equal("Ready", (await Orders(w).GetAsync(order.Id)).Status);
        Assert.Equal("Completed", (await TaskAsync(w, order.Id, "Alterations")).Status);
    }

    [Fact]
    public async Task Only_the_step_before_alterations_may_request_them()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var first = await TaskAsync(w, order.Id, "Measurements Verified");
        await Tasks(w).AssignAsync(first.Id, new AssignTaskRequest { MembershipId = w.CutterMember });
        await Mine(w, w.CutterUser).StartAsync(first.Id);

        var ex = await Assert.ThrowsAsync<AppException>(() => Mine(w, w.CutterUser).CompleteAsync(first.Id,
            new CompleteTaskRequest { CompletedChecks = ["Measurements match the order snapshot"], AlterationsRequired = true }));
        Assert.Equal("alterations_not_applicable", ex.Code);
    }

    [Fact]
    public async Task Cancelling_the_order_cancels_its_open_tasks()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        await Orders(w).CancelAsync(order.Id, new CancelOrderRequest { Reason = "Customer changed their mind" });

        Assert.All(await AllTasksAsync(w, order.Id), t => Assert.Equal("Cancelled", t.Status));
        Assert.Equal("Cancelled", (await Orders(w).GetAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Overdue_is_computed_from_deadline_and_status_never_stored()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var task = await TaskAsync(w, order.Id, "Measurements Verified");
        Assert.False(task.IsOverdue);

        var late = await Tasks(w).UpdateAsync(task.Id, new UpdateTaskRequest { Version = task.Version, Deadline = DateTimeOffset.UtcNow.AddHours(-2) });
        Assert.True(late.Task.IsOverdue);
        var overdueList = await Tasks(w).ListAsync(new TaskListQuery { OrderId = order.Id, Overdue = true });
        Assert.Single(overdueList.Items);
        Assert.Equal(1, (await Orders(w).GetAsync(order.Id)).Progress!.Overdue);

        var cancelled = await Tasks(w).CancelAsync(task.Id, new CancelTaskRequest { Reason = "Not needed" });
        Assert.False(cancelled.Task.IsOverdue);
    }

    [Fact]
    public async Task Editing_with_a_stale_task_version_is_rejected()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var task = await TaskAsync(w, order.Id, "Cutting");
        await Tasks(w).UpdateAsync(task.Id, new UpdateTaskRequest { Version = task.Version, EstimatedMinutes = 90 });

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Tasks(w).UpdateAsync(task.Id, new UpdateTaskRequest { Version = task.Version, EstimatedMinutes = 30 }));
        Assert.Equal("stale_version", ex.Code);
    }

    // ------------------------------------------------------------------ isolation and integrity

    [Fact]
    public async Task Another_business_cannot_see_or_change_the_tasks()
    {
        var a = await SeedWorldAsync();
        var b = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(a);
        var task = await TaskAsync(a, order.Id, "Cutting");

        var ex = await Assert.ThrowsAsync<AppException>(() => Tasks(b).GetAsync(task.Id));
        Assert.Equal("task_not_found", ex.Code);
        Assert.Empty((await Tasks(b).ListAsync(new TaskListQuery())).Items);
        await Assert.ThrowsAsync<AppException>(() =>
            Tasks(b).CancelAsync(task.Id, new CancelTaskRequest { Reason = "sabotage" }));
    }

    [Fact]
    public async Task Database_rejects_a_task_that_depends_on_itself()
    {
        var w = await SeedWorldAsync();
        var order = await ConfirmedOrderAsync(w);
        var task = await TaskAsync(w, order.Id, "Cutting");

        await using var raw = fx.Create(null);
        raw.TaskDependencies.Add(new TaskDependency { BusinessId = w.BusinessId, TaskId = task.Id, DependsOnTaskId = task.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
    }
}
