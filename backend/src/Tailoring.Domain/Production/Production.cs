using Tailoring.Domain.Common;
using Tailoring.Domain.Orders;

namespace Tailoring.Domain.Production;

public enum ProductionTaskStatus { Pending, Scheduled, InProgress, Paused, Blocked, Completed, Cancelled }
public enum IssueType { MissingMaterials, Defect, Clarification, Other }

public static class TaskStatusRules
{
    /// <summary>A prerequisite counts as done when it is completed, or cancelled / not applicable.</summary>
    public static bool IsSatisfied(ProductionTaskStatus s) => s is ProductionTaskStatus.Completed or ProductionTaskStatus.Cancelled;
    public static bool IsOpen(ProductionTaskStatus s) => !IsSatisfied(s);

    /// <summary>Overdue is DERIVED from deadline + status. It is never stored, so it cannot drift out of sync.</summary>
    public static bool IsOverdue(ProductionTaskStatus s, DateTime? deadlineUtc, DateTime nowUtc)
        => IsOpen(s) && deadlineUtc is { } d && d < nowUtc;

    /// <summary>Marker stored in CancelReason for conditional tasks that are not (yet) required.</summary>
    public const string NotRequiredReason = "Not required (conditional step)";
}

public class ProductionWorkflow : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Garment categories (measurement template names) that use this workflow. Empty = only used when default.</summary>
    public List<string> GarmentCategories { get; set; } = new();
    public List<ProductionStage> Stages { get; set; } = new();
}

public class ProductionStage : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid WorkflowId { get; set; }
    /// <summary>Stable machine key within the workflow, e.g. "cutting".</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public string? Instructions { get; set; }
    public string? RequiredSkill { get; set; }
    /// <summary>Minutes per garment piece; multiplied by quantity when tasks are generated. Null = needs a manual estimate.</summary>
    public int? DefaultMinutes { get; set; }
    /// <summary>Conditional stages (e.g. Alterations) are generated as "not required" and activated only when needed.</summary>
    public bool IsConditional { get; set; }
    /// <summary>Checklist the worker must tick before the task can be completed.</summary>
    public List<string> CompletionChecks { get; set; } = new();
}

/// <summary>Stage <see cref="StageId"/> cannot start until stage <see cref="DependsOnStageId"/> is done.</summary>
public class StageDependency : ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid StageId { get; set; }
    public Guid DependsOnStageId { get; set; }
}

public class ProductionTask : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderGarmentId { get; set; }
    /// <summary>Informational link to the stage it came from. Deliberately NOT a foreign key: workflows can be edited later.</summary>
    public Guid? StageId { get; set; }
    public string StageName { get; set; } = "";
    public int StageOrder { get; set; }
    public string Title { get; set; } = "";
    public string? Instructions { get; set; }
    public int Quantity { get; set; } = 1;

    public Guid? AssignedMembershipId { get; set; }
    public string? RequiredSkill { get; set; }
    /// <summary>Null means "needs an estimate"; the scheduler will flag these instead of inventing a number.</summary>
    public int? EstimatedMinutes { get; set; }
    public DateTime? PlannedStartUtc { get; set; }   // set by the scheduler (Phase 4)
    public DateTime? PlannedEndUtc { get; set; }     // set by the scheduler (Phase 4)
    public DateTime? ActualStartUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? DeadlineUtc { get; set; }

    public OrderPriority Priority { get; set; } = OrderPriority.Normal;
    public ProductionTaskStatus Status { get; set; } = ProductionTaskStatus.Pending;
    public ProductionTaskStatus? StatusBeforeBlock { get; set; }
    public IssueType? BlockedType { get; set; }
    public string? BlockedReason { get; set; }
    public string? CompletionNotes { get; set; }
    public string? CancelReason { get; set; }

    public List<string> RequiredChecks { get; set; } = new();
    public List<string> CompletedChecks { get; set; } = new();
    /// <summary>JSON array of <see cref="MaterialRequirement"/>. Linked to real inventory in Phase 6.</summary>
    public string RequiredMaterialsJson { get; set; } = "[]";
    public bool IsConditional { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public uint Version { get; set; }
}

public sealed record MaterialRequirement(string Name, decimal Quantity, string Unit);

public class TaskDependency : ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid TaskId { get; set; }
    public Guid DependsOnTaskId { get; set; }
}

/// <summary>Append-only history of everything that happens to a task, and who did it.</summary>
public class TaskEvent : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid TaskId { get; set; }
    public string Type { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Note { get; set; }
    public string? DataJson { get; set; }
}

public static class TaskEventTypes
{
    public const string Generated = "generated";
    public const string Created = "created";
    public const string Updated = "updated";
    public const string ScheduleApplied = "schedule_applied";
    public const string Assigned = "assigned";
    public const string Unassigned = "unassigned";
    public const string Started = "started";
    public const string Paused = "paused";
    public const string Resumed = "resumed";
    public const string Completed = "completed";
    public const string IssueReported = "issue_reported";
    public const string Unblocked = "unblocked";
    public const string Cancelled = "cancelled";
    public const string AlterationsRequested = "alterations_requested";
    public const string OverrideStart = "override.start";
    public const string OverrideComplete = "override.complete";
    public const string OverrideAssign = "override.assign";
}

public class WorkerSkill : ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid MembershipId { get; set; }
    /// <summary>Lowercase, trimmed, e.g. "cutting".</summary>
    public string Skill { get; set; } = "";
}
