using System.ComponentModel.DataAnnotations;
using Tailoring.Application.Common;

namespace Tailoring.Application.Customers;

public sealed record CustomerDto(
    Guid Id, string FullName, string? Email, string? Phone, string? AddressLine, string? City,
    string? Notes, string? GarmentPreferences, string Status, Guid? MergedIntoCustomerId,
    uint Version, DateTime CreatedAtUtc);

public sealed record CustomerSummaryDto(
    Guid Id, string FullName, string? Email, string? Phone, string Status, int OrderCount, DateTime CreatedAtUtc);

public sealed record DuplicateCandidateDto(Guid Id, string FullName, string? Email, string? Phone, string MatchedOn);

public sealed record CustomerListQuery : PagedQuery
{
    public string? Search { get; init; }
    public string? Status { get; init; }          // Active | Inactive | Merged ; default Active+Inactive
    public string? Sort { get; init; }            // name | created
    public bool Descending { get; init; }
}

public sealed record UpsertCustomerRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string FullName { get; init; } = "";
    [EmailAddress, StringLength(256)] public string? Email { get; init; }
    [StringLength(40)] public string? Phone { get; init; }
    [StringLength(300)] public string? AddressLine { get; init; }
    [StringLength(100)] public string? City { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
    [StringLength(2000)] public string? GarmentPreferences { get; init; }
    /// <summary>Required on update (optimistic concurrency).</summary>
    public uint? Version { get; init; }
    /// <summary>Set true only after the user has seen the duplicate warning and chosen to continue.</summary>
    public bool AllowDuplicate { get; init; }
}

public sealed record SetCustomerStatusRequest
{
    [Required] public string Status { get; init; } = "";   // Active | Inactive
}

public sealed record CommunicationDto(
    Guid Id, string Channel, string Direction, string? Subject, string Body, Guid? RecordedByUserId, DateTime CreatedAtUtc);

public sealed record AddCommunicationRequest
{
    [Required] public string Channel { get; init; } = "";
    [Required] public string Direction { get; init; } = "";
    [StringLength(200)] public string? Subject { get; init; }
    [Required, StringLength(4000)] public string Body { get; init; } = "";
}

public sealed record FittingDto(
    Guid Id, Guid CustomerId, Guid? OrderId, Guid LocationId, DateTime ScheduledAtUtc,
    int DurationMinutes, string Status, string? Notes);

public sealed record CreateFittingRequest
{
    [Required] public Guid CustomerId { get; init; }
    public Guid? OrderId { get; init; }
    [Required] public Guid LocationId { get; init; }
    /// <summary>Must carry an offset or Z; stored as UTC.</summary>
    [Required] public DateTimeOffset ScheduledAt { get; init; }
    [Range(5, 480)] public int DurationMinutes { get; init; } = 30;
    [StringLength(2000)] public string? Notes { get; init; }
}

public sealed record SetFittingStatusRequest
{
    [Required] public string Status { get; init; } = "";   // Scheduled | Completed | Cancelled | NoShow
    [StringLength(2000)] public string? Notes { get; init; }
}

public sealed record FittingListQuery : PagedQuery
{
    public Guid? CustomerId { get; init; }
    public Guid? OrderId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public interface ICustomerService
{
    Task<PagedResult<CustomerSummaryDto>> ListAsync(CustomerListQuery query, CancellationToken ct = default);
    Task<CustomerDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(UpsertCustomerRequest request, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(Guid id, UpsertCustomerRequest request, CancellationToken ct = default);
    Task<CustomerDto> SetStatusAsync(Guid id, SetCustomerStatusRequest request, CancellationToken ct = default);
    Task<CustomerDto> MergeAsync(Guid sourceId, Guid targetId, CancellationToken ct = default);
    Task<IReadOnlyList<CommunicationDto>> ListCommunicationsAsync(Guid customerId, CancellationToken ct = default);
    Task<CommunicationDto> AddCommunicationAsync(Guid customerId, AddCommunicationRequest request, CancellationToken ct = default);
}

public interface IFittingService
{
    Task<PagedResult<FittingDto>> ListAsync(FittingListQuery query, CancellationToken ct = default);
    Task<FittingDto> CreateAsync(CreateFittingRequest request, CancellationToken ct = default);
    Task<FittingDto> SetStatusAsync(Guid id, SetFittingStatusRequest request, CancellationToken ct = default);
}
