using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Auth;

public sealed record DayHoursDto(int DayOfWeek, bool IsOpen, string? OpensAt, string? ClosesAt);

public sealed record SetBusinessHoursRequest
{
    [Required] public IReadOnlyList<DayHoursDto> Days { get; init; } = [];
}

public sealed record HolidayDto(Guid Id, DateOnly Date, string Name);

public sealed record AddHolidayRequest
{
    [Required] public DateOnly Date { get; init; }
    [Required, StringLength(80)] public string Name { get; init; } = "";
}

public sealed record BusinessScheduleDto(string TimeZoneId, IReadOnlyList<DayHoursDto> Days, IReadOnlyList<HolidayDto> Holidays);

public interface IBusinessSettingsService
{
    /// <summary>Seven days. A business that has never set hours gets Monday to Saturday 09:00 to 17:00 shown as defaults (nothing is stored until it saves).</summary>
    Task<BusinessScheduleDto> GetScheduleAsync(CancellationToken ct = default);
    Task<BusinessScheduleDto> SetHoursAsync(SetBusinessHoursRequest request, CancellationToken ct = default);
    Task<HolidayDto> AddHolidayAsync(AddHolidayRequest request, CancellationToken ct = default);
    Task RemoveHolidayAsync(Guid holidayId, CancellationToken ct = default);
}
