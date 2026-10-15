using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Production;

public sealed record WorkflowStageDto(
    Guid Id, string Key, string Name, int SortOrder, string? Instructions, string? RequiredSkill,
    int? DefaultMinutes, bool IsConditional, IReadOnlyList<string> CompletionChecks, IReadOnlyList<string> DependsOn);

public sealed record WorkflowDto(
    Guid Id, string Name, string? Description, bool IsDefault, bool IsActive,
    IReadOnlyList<string> GarmentCategories, IReadOnlyList<WorkflowStageDto> Stages);

public sealed record StageInput
{
    [Required, RegularExpression("^[a-z][a-z0-9_]{0,39}$")] public string Key { get; init; } = "";
    [Required, StringLength(80, MinimumLength = 2)] public string Name { get; init; } = "";
    [StringLength(2000)] public string? Instructions { get; init; }
    [StringLength(60)] public string? RequiredSkill { get; init; }
    [Range(1, 1440)] public int? DefaultMinutes { get; init; }
    /// <summary>Generated as "not required" and activated only when a prior step asks for it (e.g. Alterations after a fitting).</summary>
    public bool IsConditional { get; init; }
    [MaxLength(10)] public List<string> CompletionChecks { get; init; } = new();
    /// <summary>Keys of prerequisite stages. Omit (null) to depend on the previous stage; send [] for no prerequisites.</summary>
    public List<string>? DependsOn { get; init; }
}

public sealed record UpsertWorkflowRequest
{
    [Required, StringLength(80, MinimumLength = 2)] public string Name { get; init; } = "";
    [StringLength(500)] public string? Description { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
    public List<string> GarmentCategories { get; init; } = new();
    [Required, MinLength(1), MaxLength(30)] public List<StageInput> Stages { get; init; } = new();
}

public interface IWorkflowService
{
    Task<IReadOnlyList<WorkflowDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<WorkflowDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<WorkflowDto> CreateAsync(UpsertWorkflowRequest request, CancellationToken ct = default);
    Task<WorkflowDto> UpdateAsync(Guid id, UpsertWorkflowRequest request, CancellationToken ct = default);
}
