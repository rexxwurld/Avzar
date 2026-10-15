using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Tailoring.Application.Common;
using Tailoring.Domain.Production;

namespace Tailoring.Application.Production;

public sealed record MaterialRequirementDto(
    [property: Required, StringLength(120)] string Name,
    [property: Range(0.001, 1000000)] decimal Quantity,
    [property: Required, StringLength(20)] string Unit);

public sealed record TaskSummaryDto(
    Guid Id, Guid OrderId, string OrderNumber, Guid OrderGarmentId, string GarmentName,
    string StageName, int StageOrder, string Title, int Quantity,
    string Status, string Priority, DateTime? DeadlineUtc, bool IsOverdue,
    Guid? AssigneeMembershipId, string? AssigneeName, string? RequiredSkill,
    int? EstimatedMinutes, bool NeedsEstimate, int UnmetDependencies, bool IsEligible, bool IsConditional,
    string? BlockedType, string? BlockedReason, uint Version);

public sealed record TaskDependencyDto(Guid TaskId, string Title, string Status);
public sealed record TaskEventDto(string Type, string? FromStatus, string? ToStatus, Guid? ActorUserId, string? Note, DateTime AtUtc);

public sealed record GarmentSpecDto(
    string Name, string? Category, int Quantity, string? Fabric, string? Colour,
    string? StyleInstructions, string? AlterationNotes, JsonElement? MeasurementSnapshot);

public sealed record TaskDetailDto(
    TaskSummaryDto Task, string? Instructions,
    IReadOnlyList<string> RequiredChecks, IReadOnlyList<string> CompletedChecks,
    IReadOnlyList<MaterialRequirementDto> RequiredMaterials,
    string? CompletionNotes, DateTime? PlannedStartUtc, DateTime? PlannedEndUtc,
    DateTime? ActualStartUtc, DateTime? CompletedAtUtc,
    IReadOnlyList<TaskDependencyDto> DependsOn, GarmentSpecDto Garment,
    // CustomerName is only populated for managers (tasks.view). Workers never receive customer personal data.
    string? CustomerName, IReadOnlyList<TaskEventDto> Events);

public sealed record TaskListQuery : PagedQuery
{
    public string? Status { get; init; }
    public Guid? OrderId { get; init; }
    public Guid? GarmentId { get; init; }
    public Guid? AssigneeId { get; init; }
    public bool? Unassigned { get; init; }
    public bool? Overdue { get; init; }
    public bool? Eligible { get; init; }
    public string? Stage { get; init; }
    public string? Priority { get; init; }
    public DateTimeOffset? DueBefore { get; init; }
}

public sealed record MyTaskListQuery : PagedQuery
{
    /// <summary>open (default) | all | completed</summary>
    public string? Show { get; init; }
}

public sealed record CreateTaskRequest
{
    [Required, StringLength(160, MinimumLength = 2)] public string Title { get; init; } = "";
    public Guid? OrderGarmentId { get; init; }
    [StringLength(80)] public string? StageName { get; init; }
    [StringLength(4000)] public string? Instructions { get; init; }
    [StringLength(60)] public string? RequiredSkill { get; init; }
    [Range(1, 100000)] public int? EstimatedMinutes { get; init; }
    public string Priority { get; init; } = "Normal";
    public DateTimeOffset? Deadline { get; init; }
    [MaxLength(10)] public List<string> CompletionChecks { get; init; } = new();
    public List<MaterialRequirementDto> RequiredMaterials { get; init; } = new();
    /// <summary>Tasks in the same order that must be done first.</summary>
    public List<Guid> DependsOnTaskIds { get; init; } = new();
}

public sealed record UpdateTaskRequest
{
    [Required] public uint Version { get; init; }
    [StringLength(4000)] public string? Instructions { get; init; }
    public string? Priority { get; init; }
    public DateTimeOffset? Deadline { get; init; }
    [Range(1, 100000)] public int? EstimatedMinutes { get; init; }
    [StringLength(60)] public string? RequiredSkill { get; init; }
    public List<MaterialRequirementDto>? RequiredMaterials { get; init; }
}

public sealed record AssignTaskRequest
{
    /// <summary>Null un-assigns the task.</summary>
    public Guid? MembershipId { get; init; }
    /// <summary>Required (min 5 chars) together with Override=true, and needs tasks.override.</summary>
    [StringLength(500)] public string? Reason { get; init; }
    /// <summary>Allow assigning someone who lacks the required skill or is not at the order's location.</summary>
    public bool Override { get; init; }
}

public sealed record NoteRequest { [StringLength(1000)] public string? Note { get; init; } }

public sealed record CancelTaskRequest
{
    [Required, StringLength(500, MinimumLength = 3)] public string Reason { get; init; } = "";
}

public sealed record OverrideTaskRequest
{
    [Required] public string Action { get; init; } = "";          // Start | Complete
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; init; } = "";
}

public sealed record GenerateTasksRequest
{
    /// <summary>Optional per-garment workflow choice: garmentId -> workflowId.</summary>
    public Dictionary<Guid, Guid>? WorkflowOverrides { get; init; }
}

public sealed record GenerateTasksResult(int TasksCreated, int GarmentsWithoutWorkflow, int GarmentsAlreadyPlanned);

public sealed record RequestAlterationsRequest
{
    [Required, StringLength(2000, MinimumLength = 3)] public string Notes { get; init; } = "";
}

public sealed record CompleteTaskRequest
{
    [StringLength(2000)] public string? Notes { get; init; }
    /// <summary>Every item in the task's checklist must appear here.</summary>
    public List<string> CompletedChecks { get; init; } = new();
    /// <summary>Only valid on a task that precedes a conditional (alterations) step, e.g. the fitting.</summary>
    public bool AlterationsRequired { get; init; }
    [StringLength(2000)] public string? AlterationNotes { get; init; }
}

public sealed record ReportIssueRequest
{
    [Required] public string Type { get; init; } = "";           // MissingMaterials | Defect | Clarification | Other
    [Required, StringLength(2000, MinimumLength = 3)] public string Description { get; init; } = "";
}

public sealed record WorkerDto(Guid MembershipId, string DisplayName, bool IsActive, IReadOnlyList<string> Skills, int OpenTasks);
public sealed record SetSkillsRequest { [MaxLength(30)] public List<string> Skills { get; init; } = new(); }

public interface ITaskService
{
    Task<PagedResult<TaskSummaryDto>> ListAsync(TaskListQuery query, CancellationToken ct = default);
    Task<TaskDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<TaskDetailDto> CreateAsync(Guid orderId, CreateTaskRequest request, CancellationToken ct = default);
    Task<TaskDetailDto> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken ct = default);
    Task<TaskDetailDto> AssignAsync(Guid id, AssignTaskRequest request, CancellationToken ct = default);
    Task<TaskDetailDto> UnblockAsync(Guid id, NoteRequest request, CancellationToken ct = default);
    Task<TaskDetailDto> CancelAsync(Guid id, CancelTaskRequest request, CancellationToken ct = default);
    Task<TaskDetailDto> OverrideAsync(Guid id, OverrideTaskRequest request, CancellationToken ct = default);
    Task<GenerateTasksResult> GenerateForOrderAsync(Guid orderId, GenerateTasksRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TaskDetailDto>> RequestAlterationsAsync(Guid garmentId, RequestAlterationsRequest request, CancellationToken ct = default);
}

/// <summary>Actions a worker performs on tasks assigned to THEM. Every call re-checks assignment on the server.</summary>
public interface IMyTaskService
{
    Task<PagedResult<TaskSummaryDto>> ListAsync(MyTaskListQuery query, CancellationToken ct = default);
    Task<TaskDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<TaskDetailDto> StartAsync(Guid id, CancellationToken ct = default);
    Task<TaskDetailDto> PauseAsync(Guid id, NoteRequest request, CancellationToken ct = default);
    Task<TaskDetailDto> ResumeAsync(Guid id, CancellationToken ct = default);
    Task<TaskDetailDto> CompleteAsync(Guid id, CompleteTaskRequest request, CancellationToken ct = default);
    Task<TaskDetailDto> ReportIssueAsync(Guid id, ReportIssueRequest request, CancellationToken ct = default);
}

public interface IWorkerService
{
    Task<IReadOnlyList<WorkerDto>> ListAsync(CancellationToken ct = default);
    Task<WorkerDto> SetSkillsAsync(Guid membershipId, SetSkillsRequest request, CancellationToken ct = default);
}

/// <summary>Turns a confirmed order into production tasks. Adds entities to the current unit of work; the caller saves.</summary>
public interface IProductionPlanner
{
    Task<GenerateTasksResult> GenerateAsync(Guid orderId, Dictionary<Guid, Guid>? workflowOverrides, CancellationToken ct = default);
    Task CancelOpenTasksAsync(Guid orderId, string reason, CancellationToken ct = default);
}
