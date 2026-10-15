using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Measurements;

public sealed record TemplateFieldDto(string Key, string Label, string Unit, decimal? MinValue, decimal? MaxValue, bool IsRequired, int SortOrder);
public sealed record TemplateDto(Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<TemplateFieldDto> Fields);

public sealed record TemplateFieldInput
{
    [Required, RegularExpression("^[a-z][a-z0-9_]{0,39}$")] public string Key { get; init; } = "";
    [Required, StringLength(60)] public string Label { get; init; } = "";
    [Required] public string Unit { get; init; } = "cm";           // cm | mm | in | kg | lb
    public decimal? MinValue { get; init; }
    public decimal? MaxValue { get; init; }
    public bool IsRequired { get; init; }
}

public sealed record UpsertTemplateRequest
{
    [Required, StringLength(80, MinimumLength = 2)] public string Name { get; init; } = "";
    [StringLength(500)] public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
    [Required, MinLength(1), MaxLength(60)] public List<TemplateFieldInput> Fields { get; init; } = new();
}

public sealed record MeasurementValueDto(string Key, string Label, string Unit, decimal Value);

public sealed record MeasurementSetDto(
    Guid Id, Guid CustomerId, Guid TemplateId, string TemplateName, string Label,
    int CurrentRevision, IReadOnlyList<MeasurementValueDto> Values, DateTime UpdatedAtUtc);

public sealed record RevisionDto(
    int RevisionNumber, IReadOnlyList<MeasurementValueDto> Values, string? Notes, Guid? RecordedByUserId, DateTime RecordedAtUtc);

public sealed record CreateMeasurementSetRequest
{
    [Required] public Guid TemplateId { get; init; }
    [Required, StringLength(80, MinimumLength = 1)] public string Label { get; init; } = "";
    [Required] public Dictionary<string, decimal> Values { get; init; } = new();
    [StringLength(2000)] public string? Notes { get; init; }
}

/// <summary>A revision is a COMPLETE set of values (not a patch), so every revision stands on its own.</summary>
public sealed record AddRevisionRequest
{
    [Required] public Dictionary<string, decimal> Values { get; init; } = new();
    [StringLength(2000)] public string? Notes { get; init; }
}

public sealed record MeasurementDiffDto(string Key, string Label, string Unit, decimal? From, decimal? To, decimal? Delta);

public interface IMeasurementService
{
    Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken ct = default);
    Task<TemplateDto> GetTemplateAsync(Guid id, CancellationToken ct = default);
    Task<TemplateDto> CreateTemplateAsync(UpsertTemplateRequest request, CancellationToken ct = default);
    Task<TemplateDto> UpdateTemplateAsync(Guid id, UpsertTemplateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<MeasurementSetDto>> ListSetsAsync(Guid customerId, CancellationToken ct = default);
    Task<MeasurementSetDto> CreateSetAsync(Guid customerId, CreateMeasurementSetRequest request, CancellationToken ct = default);
    Task<MeasurementSetDto> AddRevisionAsync(Guid setId, AddRevisionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RevisionDto>> HistoryAsync(Guid setId, CancellationToken ct = default);
    Task<IReadOnlyList<MeasurementDiffDto>> CompareAsync(Guid setId, int fromRevision, int toRevision, CancellationToken ct = default);
}
