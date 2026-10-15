using Tailoring.Domain.Common;

namespace Tailoring.Domain.Tenancy;

/// <summary>Opening hours for one weekday (0 = Sunday .. 6 = Saturday), in the business's local time.</summary>
public class BusinessHoursDay : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public int DayOfWeek { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly? OpensAt { get; set; }
    public TimeOnly? ClosesAt { get; set; }
}

/// <summary>A closed date (public holiday, annual leave). One row per business per date.</summary>
public class BusinessHoliday : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}

public sealed record DayHoursInput(int DayOfWeek, bool IsOpen, TimeOnly? OpensAt, TimeOnly? ClosesAt);

/// <summary>Pure validation and lookups for opening hours. The scheduling engine (Step C) builds on these.</summary>
public static class BusinessHoursRules
{
    /// <summary>Returns an error message, or null when the week is valid: exactly seven distinct days; open days need opens &lt; closes; closed days carry no times.</summary>
    public static string? Validate(IReadOnlyList<DayHoursInput> days)
    {
        if (days.Count != 7) return "Provide exactly seven days (0 = Sunday to 6 = Saturday).";
        if (days.Select(d => d.DayOfWeek).Distinct().Count() != 7 || days.Any(d => d.DayOfWeek is < 0 or > 6))
            return "Each weekday from 0 to 6 must appear once.";
        foreach (var d in days)
        {
            if (!d.IsOpen)
            {
                if (d.OpensAt is not null || d.ClosesAt is not null) return $"Day {d.DayOfWeek} is closed but has opening times.";
                continue;
            }
            if (d.OpensAt is null || d.ClosesAt is null) return $"Day {d.DayOfWeek} is open and needs opening and closing times.";
            if (d.OpensAt >= d.ClosesAt) return $"Day {d.DayOfWeek}: opening time must be before closing time.";
        }
        return null;
    }

    /// <summary>Open on that date? A holiday always wins over the weekly pattern.</summary>
    public static bool IsOpenOn(DateOnly date, IReadOnlyList<DayHoursInput> week, IReadOnlySet<DateOnly> holidays)
    {
        if (holidays.Contains(date)) return false;
        var day = week.FirstOrDefault(d => d.DayOfWeek == (int)date.DayOfWeek);
        return day is { IsOpen: true };
    }

    public static string? ValidateHolidayName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "Holiday name is required."
         : name.Trim().Length > 80 ? "Holiday name must be 80 characters or fewer." : null;
}
