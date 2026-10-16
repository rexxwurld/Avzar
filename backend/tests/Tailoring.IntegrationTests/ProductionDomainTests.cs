using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Pure logic. No database, no Docker.</summary>
public class ProductionDomainTests
{
    private static (Order, OrderGarment) Sample(int qty = 2, OrderPriority priority = OrderPriority.High)
    {
        var order = new Order { BusinessId = Guid.NewGuid(), Priority = priority, PromisedDate = new DateOnly(2026, 10, 20) };
        var garment = new OrderGarment { BusinessId = order.BusinessId, OrderId = order.Id, Name = "Suit", Quantity = qty };
        return (order, garment);
    }

    private static TaskPlan PlanDefault(int qty = 2)
    {
        var (order, garment) = Sample(qty);
        var (wf, links) = DefaultWorkflows.Create(order.BusinessId);
        return TaskPlanner.Plan(order, garment, wf, links.Select(l => new StageLink(l.StageId, l.DependsOnStageId)), null, null);
    }

    [Fact]
    public void Default_workflow_produces_the_ten_spec_stages_with_alterations_not_required()
    {
        var plan = PlanDefault();
        Assert.Equal(10, plan.Tasks.Count);
        var alterations = plan.Tasks.Single(t => t.StageName == "Alterations");
        Assert.Equal(ProductionTaskStatus.Cancelled, alterations.Status);
        Assert.Equal(TaskStatusRules.NotRequiredReason, alterations.CancelReason);
        Assert.All(plan.Tasks.Where(t => !t.IsConditional), t => Assert.Equal(ProductionTaskStatus.Pending, t.Status));
    }

    [Fact]
    public void Dependencies_follow_the_workflow_and_final_qc_waits_for_fitting_and_alterations()
    {
        var plan = PlanDefault();
        Guid Id(string stage) => plan.Tasks.Single(t => t.StageName == stage).Id;
        bool Dep(string stage, string on) => plan.Dependencies.Any(d => d.TaskId == Id(stage) && d.DependsOnTaskId == Id(on));

        Assert.True(Dep("Sewing", "Cutting"));
        Assert.True(Dep("Finishing", "Sewing"));
        Assert.True(Dep("Final Quality Check", "Fitting"));
        Assert.True(Dep("Final Quality Check", "Alterations"));
        Assert.False(Dep("Sewing", "Finishing"));
        Assert.Equal(10, plan.Dependencies.Count);
    }

    [Fact]
    public void Estimates_scale_with_quantity_and_missing_defaults_stay_unknown()
    {
        var plan = PlanDefault(qty: 3);
        Assert.Equal(60 * 3, plan.Tasks.Single(t => t.StageName == "Cutting").EstimatedMinutes);
        Assert.All(plan.Tasks, t => Assert.Equal(3, t.Quantity));

        var (order, garment) = Sample();
        var wf = new ProductionWorkflow { BusinessId = order.BusinessId, Name = "NoEstimates" };
        wf.Stages.Add(new ProductionStage { BusinessId = order.BusinessId, WorkflowId = wf.Id, Key = "a", Name = "A" });
        var bare = TaskPlanner.Plan(order, garment, wf, [], null, null);
        Assert.Null(bare.Tasks.Single().EstimatedMinutes);   // not invented
    }

    [Fact]
    public void Tasks_inherit_order_priority_and_the_supplied_deadline()
    {
        var (order, garment) = Sample(priority: OrderPriority.Urgent);
        var (wf, links) = DefaultWorkflows.Create(order.BusinessId);
        var deadline = new DateTime(2026, 10, 20, 22, 59, 0, DateTimeKind.Utc);
        var plan = TaskPlanner.Plan(order, garment, wf, links.Select(l => new StageLink(l.StageId, l.DependsOnStageId)), deadline, null);
        Assert.All(plan.Tasks, t => { Assert.Equal(OrderPriority.Urgent, t.Priority); Assert.Equal(deadline, t.DeadlineUtc); });
    }

    [Fact]
    public void End_of_day_is_computed_in_the_business_time_zone()
    {
        var lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos"); // UTC+1, no DST
        var utc = TaskPlanner.EndOfDayUtc(new DateOnly(2026, 10, 20), lagos);
        Assert.Equal(new DateTime(2026, 10, 20, 22, 59, 0, DateTimeKind.Utc), utc);
    }

    [Theory]
    [InlineData(ProductionTaskStatus.InProgress, -1, true)]
    [InlineData(ProductionTaskStatus.Blocked, -1, true)]
    [InlineData(ProductionTaskStatus.Pending, +1, false)]
    [InlineData(ProductionTaskStatus.Completed, -1, false)]
    [InlineData(ProductionTaskStatus.Cancelled, -1, false)]
    public void Overdue_is_derived_from_deadline_and_status(ProductionTaskStatus status, int hoursFromNow, bool expected)
    {
        var now = DateTime.UtcNow;
        Assert.Equal(expected, TaskStatusRules.IsOverdue(status, now.AddHours(hoursFromNow), now));
    }

    [Fact]
    public void A_task_without_a_deadline_is_never_overdue()
        => Assert.False(TaskStatusRules.IsOverdue(ProductionTaskStatus.Pending, null, DateTime.UtcNow));

    [Fact]
    public void Cycle_detection_finds_loops_and_accepts_dags()
    {
        string[] nodes = ["a", "b", "c"];
        Assert.True(DependencyGraph.HasCycle(nodes, [("a", "b"), ("b", "c"), ("c", "a")]));
        Assert.False(DependencyGraph.HasCycle(nodes, [("a", "b"), ("b", "c"), ("a", "c")]));
        Assert.True(DependencyGraph.HasCycle(nodes, [("a", "a")]));
    }

    private static IReadOnlyList<Tailoring.Domain.Tenancy.DayHoursInput> ScheduleWeek()
        => Enumerable.Range(0, 7).Select(day => new Tailoring.Domain.Tenancy.DayHoursInput(day, true,
            new TimeOnly(9, 0), new TimeOnly(17, 0))).ToList();

    [Fact]
    public void Schedule_planner_respects_business_hours_and_worker_non_overlap()
    {
        var worker = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var start = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);
        var plan = ProductionSchedulePlanner.Plan([
            new ScheduleCandidate(first, worker, 120, 0, null, 1, []),
            new ScheduleCandidate(second, worker, 60, 1, null, 2, [])
        ], [], ScheduleWeek(), new HashSet<DateOnly>(), TimeZoneInfo.Utc, start);

        Assert.Empty(plan.Unscheduled);
        Assert.Equal(2, plan.Scheduled.Count);
        Assert.Equal(new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc), plan.Scheduled[0].StartUtc);
        Assert.Equal(new DateTime(2026, 10, 19, 11, 0, 0, DateTimeKind.Utc), plan.Scheduled[1].StartUtc);
        Assert.All(plan.Scheduled, x => Assert.True(x.EndUtc > x.StartUtc));
    }

    [Fact]
    public void Schedule_planner_respects_dependencies_and_existing_worker_reservations()
    {
        var worker = Guid.NewGuid();
        var prerequisite = Guid.NewGuid();
        var dependent = Guid.NewGuid();
        var start = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
        var plan = ProductionSchedulePlanner.Plan([
            new ScheduleCandidate(dependent, worker, 30, 0, null, 2, [prerequisite]),
            new ScheduleCandidate(prerequisite, worker, 60, 2, null, 1, [])
        ], [new ScheduleReservation(worker, start, start.AddHours(1))], ScheduleWeek(), new HashSet<DateOnly>(), TimeZoneInfo.Utc, start);

        Assert.Empty(plan.Unscheduled);
        var first = plan.Scheduled.Single(x => x.TaskId == prerequisite);
        var next = plan.Scheduled.Single(x => x.TaskId == dependent);
        Assert.Equal(start.AddHours(1), first.StartUtc);
        Assert.True(next.StartUtc >= first.EndUtc);
    }

    [Fact]
    public void Schedule_planner_reports_missing_estimates_and_closed_dates_instead_of_fabricating_slots()
    {
        var worker = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var known = Guid.NewGuid();
        var monday = new DateOnly(2026, 10, 19);
        var start = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
        var plan = ProductionSchedulePlanner.Plan([
            new ScheduleCandidate(unknown, worker, null, 0, null, 1, []),
            new ScheduleCandidate(known, worker, 60, 1, null, 2, [])
        ], [], ScheduleWeek(), new HashSet<DateOnly> { monday }, TimeZoneInfo.Utc, start, horizonDays: 1);

        Assert.Contains(plan.Unscheduled, x => x.TaskId == unknown && x.Reason.Contains("estimate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Unscheduled, x => x.TaskId == known && x.Reason.Contains("shop-hours", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void Schedule_planner_flags_dependency_cycles_and_does_not_overlap_workers_with_themselves()
    {
        var workerA = Guid.NewGuid();
        var workerB = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var start = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
        var plan = ProductionSchedulePlanner.Plan([
            new ScheduleCandidate(a, workerA, 30, 0, null, 1, [b]),
            new ScheduleCandidate(b, workerA, 30, 1, null, 2, [a]),
            new ScheduleCandidate(Guid.NewGuid(), workerB, 45, 2, null, 3, [])
        ], [], ScheduleWeek(), new HashSet<DateOnly>(), TimeZoneInfo.Utc, start);

        Assert.Contains(plan.Unscheduled, x => x.TaskId == a && x.Reason.Contains("dependency", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Unscheduled, x => x.TaskId == b && x.Reason.Contains("dependency", StringComparison.OrdinalIgnoreCase));
        var workerBSlot = Assert.Single(plan.Scheduled);
        Assert.Equal(start, workerBSlot.StartUtc);
    }

    [Fact]
    public void Schedule_planner_respects_worker_availability_windows_and_approved_leave()
    {
        var worker = Guid.NewGuid();
        var task = Guid.NewGuid();
        var monday = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);
        var availability = Enumerable.Range(0, 7).Select(day =>
            new WorkerAvailabilityWindow(worker, day, new TimeOnly(10, 0), new TimeOnly(15, 0), 300)).ToArray();
        var leave = new[] { new WorkerLeaveReservation(worker,
            new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)) };

        var plan = ProductionSchedulePlanner.Plan([
            new ScheduleCandidate(task, worker, 60, 0, null, 1, [])
        ], [], ScheduleWeek(), new HashSet<DateOnly>(), TimeZoneInfo.Utc, monday,
            horizonDays: 2, workerAvailability: availability, workerLeave: leave);

        var slot = Assert.Single(plan.Scheduled);
        Assert.Equal(new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc), slot.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 19, 13, 0, 0, DateTimeKind.Utc), slot.EndUtc);
        Assert.Empty(plan.Unscheduled);
    }

    [Fact]
    public void Schedule_planner_enforces_daily_workload_capacity_and_missing_worker_days_are_unavailable()
    {
        var worker = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var start = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
        var availability = Enumerable.Range(0, 7).Select(day =>
            new WorkerAvailabilityWindow(worker, day, new TimeOnly(9, 0), new TimeOnly(17, 0), 90)).ToArray();

        var plan = ProductionSchedulePlanner.Plan([
            new ScheduleCandidate(first, worker, 90, 0, null, 1, []),
            new ScheduleCandidate(second, worker, 90, 1, null, 2, [])
        ], [], ScheduleWeek(), new HashSet<DateOnly>(), TimeZoneInfo.Utc, start,
            horizonDays: 2, workerAvailability: availability);

        Assert.Empty(plan.Unscheduled);
        Assert.Equal(new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc), plan.Scheduled.Single(x => x.TaskId == first).StartUtc);
        Assert.Equal(new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), plan.Scheduled.Single(x => x.TaskId == second).StartUtc);

        var onlyMonday = new[] { new WorkerAvailabilityWindow(worker, 0, new TimeOnly(9, 0), new TimeOnly(17, 0), 480) };
        var unavailable = ProductionSchedulePlanner.Plan([
            new ScheduleCandidate(Guid.NewGuid(), worker, 30, 0, null, 1, [])
        ], [], ScheduleWeek(), new HashSet<DateOnly>(), TimeZoneInfo.Utc, start,
            horizonDays: 1, workerAvailability: onlyMonday);
        Assert.Single(unavailable.Unscheduled);
    }

    [Fact]
    public void Schedule_planner_rejects_non_utc_start_instants_and_invalid_horizons()
    {
        Assert.Throws<ArgumentException>(() => ProductionSchedulePlanner.Plan([], [], ScheduleWeek(),
            new HashSet<DateOnly>(), TimeZoneInfo.Utc, new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Local)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionSchedulePlanner.Plan([], [], ScheduleWeek(),
            new HashSet<DateOnly>(), TimeZoneInfo.Utc, new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc), 0));
    }

    [Fact]
    public void The_default_workflow_is_acyclic()
    {
        var (wf, links) = DefaultWorkflows.Create(Guid.NewGuid());
        var keyById = wf.Stages.ToDictionary(s => s.Id, s => s.Key);
        Assert.False(DependencyGraph.HasCycle(keyById.Values, links.Select(l => (keyById[l.DependsOnStageId], keyById[l.StageId]))));
    }
}
