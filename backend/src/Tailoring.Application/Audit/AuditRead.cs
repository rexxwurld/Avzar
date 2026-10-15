using Tailoring.Application.Common;

namespace Tailoring.Application.Audit;

public sealed record AuditEntryDto(
    Guid Id, DateTime CreatedAtUtc, Guid? ActorUserId, string? ActorName,
    string Action, string EntityType, Guid? EntityId, string? DetailsJson);

/// <summary>All filters are exact matches. <c>ToUtc</c> is exclusive.</summary>
public sealed record AuditListQuery : PagedQuery
{
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public Guid? ActorUserId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

/// <summary>Read-only. The audit log is append-only; there is deliberately no update or delete.</summary>
public interface IAuditReadService
{
    Task<PagedResult<AuditEntryDto>> ListAsync(AuditListQuery query, CancellationToken ct = default);
}
