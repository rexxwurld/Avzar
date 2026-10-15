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

    [Fact]
    public void The_default_workflow_is_acyclic()
    {
        var (wf, links) = DefaultWorkflows.Create(Guid.NewGuid());
        var keyById = wf.Stages.ToDictionary(s => s.Id, s => s.Key);
        Assert.False(DependencyGraph.HasCycle(keyById.Values, links.Select(l => (keyById[l.DependsOnStageId], keyById[l.StageId]))));
    }
}
