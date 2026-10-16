using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Production;

public sealed record ShopSchedulePreviewRequest
{
    [Required] public DateTimeOffset StartUtc { get; init; }
    [Range(1, 366)] public int HorizonDays { get; init; } = 60;
}

public sealed record ScheduledSlotDto(Guid TaskId, DateTime StartUtc, DateTime EndUtc, uint Version);
public sealed record UnscheduledTaskDto(Guid TaskId, string Reason);
public sealed record ShopSchedulePreviewDto(
    DateTime StartUtc,
    int HorizonDays,
    string TimeZoneId,
    IReadOnlyList<ScheduledSlotDto> Scheduled,
    IReadOnlyList<UnscheduledTaskDto> Unscheduled,
    IReadOnlyDictionary<Guid, uint> TaskVersions,
    bool IsPreviewOnly);

/// <summary>Applies a previously previewed schedule only when every candidate is schedulable and its row version is unchanged.</summary>
public sealed record ShopScheduleApplyRequest
{
    [Required] public DateTimeOffset StartUtc { get; init; }
    [Range(1, 366)] public int HorizonDays { get; init; } = 60;
    [Required] public IReadOnlyDictionary<Guid, uint> ExpectedTaskVersions { get; init; } = new Dictionary<Guid, uint>();
    [Required] public IReadOnlyList<ScheduledSlotDto> ExpectedSlots { get; init; } = Array.Empty<ScheduledSlotDto>();
}

public sealed record ShopScheduleApplyDto(int UpdatedTasks, DateTime AppliedAtUtc, bool NotificationsQueued);

public interface IShopScheduleService
{
    Task<ShopSchedulePreviewDto> PreviewAsync(ShopSchedulePreviewRequest request, CancellationToken ct = default);
    Task<ShopScheduleApplyDto> ApplyAsync(ShopScheduleApplyRequest request, CancellationToken ct = default);
}

public sealed record WorkerAvailabilityDayInput(int DayOfWeek, bool IsAvailable, TimeOnly? OpensAt, TimeOnly? ClosesAt, int CapacityMinutes);
public sealed record WorkerAvailabilityDto(Guid MembershipId, bool IsConfigured, IReadOnlyList<WorkerAvailabilityDayInput> Days);
public sealed record SetWorkerAvailabilityRequest { [Required] public IReadOnlyList<WorkerAvailabilityDayInput> Days { get; init; } = Array.Empty<WorkerAvailabilityDayInput>(); }
public sealed record WorkerLeaveCreateRequest
{
    [Required] public DateTimeOffset StartsAtUtc { get; init; }
    [Required] public DateTimeOffset EndsAtUtc { get; init; }
    [Required, StringLength(300, MinimumLength = 2)] public string Reason { get; init; } = "";
}
public sealed record WorkerLeaveDto(Guid Id, Guid MembershipId, DateTime StartsAtUtc, DateTime EndsAtUtc, string Reason, string Status);
public sealed record WorkerCalendarItemDto(Guid TaskId, Guid OrderId, string Title, string Status, DateTime StartUtc, DateTime EndUtc, DateTime? DeadlineUtc);

public interface IWorkerScheduleService
{
    Task<WorkerAvailabilityDto> GetAvailabilityAsync(Guid membershipId, CancellationToken ct = default);
    Task<WorkerAvailabilityDto> SetAvailabilityAsync(Guid membershipId, IReadOnlyList<WorkerAvailabilityDayInput> days, CancellationToken ct = default);
    Task<IReadOnlyList<WorkerLeaveDto>> ListLeaveAsync(Guid membershipId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
    Task<WorkerLeaveDto> AddLeaveAsync(Guid membershipId, WorkerLeaveCreateRequest request, CancellationToken ct = default);
    Task CancelLeaveAsync(Guid membershipId, Guid leaveId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkerCalendarItemDto>> GetCalendarAsync(Guid membershipId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
