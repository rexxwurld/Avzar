using Tailoring.Domain.Orders;

namespace Tailoring.Domain.Production;

public sealed record StageLink(Guid StageId, Guid DependsOnStageId);
public sealed record TaskPlan(List<ProductionTask> Tasks, List<TaskDependency> Dependencies);

/// <summary>Pure function: turns one garment + one workflow into tasks and dependency edges. No database access.</summary>
public static class TaskPlanner
{
    public static TaskPlan Plan(Order order, OrderGarment garment, ProductionWorkflow workflow,
        IEnumerable<StageLink> links, DateTime? deadlineUtc, Guid? actorUserId)
    {
        var stages = workflow.Stages.OrderBy(s => s.SortOrder).ToList();
        var byStage = new Dictionary<Guid, ProductionTask>();
        var tasks = new List<ProductionTask>();

        foreach (var stage in stages)
        {
            var task = new ProductionTask
            {
                BusinessId = order.BusinessId,
                OrderId = order.Id,
                OrderGarmentId = garment.Id,
                StageId = stage.Id,
                StageName = stage.Name,
                StageOrder = stage.SortOrder,
                Title = $"{stage.Name} - {garment.Name}",
                Instructions = stage.Instructions,
                Quantity = garment.Quantity,
                RequiredSkill = stage.RequiredSkill,
                EstimatedMinutes = stage.DefaultMinutes is { } m ? m * garment.Quantity : null,
                DeadlineUtc = deadlineUtc,
                Priority = order.Priority,
                RequiredChecks = stage.CompletionChecks.ToList(),
                IsConditional = stage.IsConditional,
                CreatedByUserId = actorUserId
            };
            if (stage.IsConditional)
            {
                task.Status = ProductionTaskStatus.Cancelled;
                task.CancelReason = TaskStatusRules.NotRequiredReason;
            }
            byStage[stage.Id] = task;
            tasks.Add(task);
        }

        var deps = links
            .Where(l => byStage.ContainsKey(l.StageId) && byStage.ContainsKey(l.DependsOnStageId))
            .Select(l => new TaskDependency
            {
                BusinessId = order.BusinessId,
                TaskId = byStage[l.StageId].Id,
                DependsOnTaskId = byStage[l.DependsOnStageId].Id
            }).ToList();

        return new TaskPlan(tasks, deps);
    }

    /// <summary>End of the promised day in the business time zone, as UTC.</summary>
    public static DateTime EndOfDayUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(23, 59)), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
