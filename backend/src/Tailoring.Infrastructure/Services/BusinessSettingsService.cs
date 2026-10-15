using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Auth;
using Tailoring.Application.Common;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class BusinessSettingsService(AppDbContext db, ITenantContext tenant, IAuditLogger audit) : IBusinessSettingsService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");
    private Guid ActorId => tenant.UserId ?? throw AppException.Unauthorized("no_user", "Not signed in.");

    private const int MaxHolidays = 400;

    public async Task<BusinessScheduleDto> GetScheduleAsync(CancellationToken ct = default)
    {
        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == BusinessId, ct);
        var rows = await db.BusinessHours.AsNoTracking().OrderBy(d => d.DayOfWeek).ToListAsync(ct);

        IReadOnlyList<DayHoursDto> days = rows.Count == 7
            ? rows.Select(ToDto).ToList()
            : Enumerable.Range(0, 7).Select(d => d == 0
                ? new DayHoursDto(0, false, null, null)
                : new DayHoursDto(d, true, "09:00", "17:00")).ToList();

        var holidays = await db.BusinessHolidays.AsNoTracking().OrderBy(h => h.Date)
            .Select(h => new HolidayDto(h.Id, h.Date, h.Name)).ToListAsync(ct);
        return new BusinessScheduleDto(business.TimeZoneId, days, holidays);
    }

    public async Task<BusinessScheduleDto> SetHoursAsync(SetBusinessHoursRequest req, CancellationToken ct = default)
    {
        var parsed = new List<DayHoursInput>();
        foreach (var d in req.Days)
        {
            if (!TryTime(d.OpensAt, out var opens) || !TryTime(d.ClosesAt, out var closes))
                throw AppException.BadRequest("invalid_hours", "Times must look like 09:00 (24-hour).");
            parsed.Add(new DayHoursInput(d.DayOfWeek, d.IsOpen, opens, closes));
        }
        if (BusinessHoursRules.Validate(parsed) is { } error) throw AppException.BadRequest("invalid_hours", error);

        var existing = await db.BusinessHours.ToListAsync(ct);
        foreach (var p in parsed)
        {
            var row = existing.FirstOrDefault(x => x.DayOfWeek == p.DayOfWeek);
            if (row is null)
            {
                row = new BusinessHoursDay { BusinessId = BusinessId, DayOfWeek = p.DayOfWeek };
                db.BusinessHours.Add(row);
            }
            row.IsOpen = p.IsOpen; row.OpensAt = p.OpensAt; row.ClosesAt = p.ClosesAt;
        }
        audit.Record(BusinessId, ActorId, "business.hours_set", nameof(BusinessHoursDay), null);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two people saving the first set of hours at the same moment: the unique (business, day) index rejects the loser.
            throw AppException.Conflict("hours_conflict", "Opening hours were changed by someone else. Reload and try again.");
        }
        return await GetScheduleAsync(ct);
    }

    public async Task<HolidayDto> AddHolidayAsync(AddHolidayRequest req, CancellationToken ct = default)
    {
        if (BusinessHoursRules.ValidateHolidayName(req.Name) is { } nameError) throw AppException.BadRequest("invalid_holiday", nameError);
        if (await db.BusinessHolidays.AnyAsync(h => h.Date == req.Date, ct))
            throw AppException.Conflict("holiday_exists", "That date is already marked as closed.");
        if (await db.BusinessHolidays.CountAsync(ct) >= MaxHolidays)
            throw AppException.BadRequest("too_many_holidays", $"A business can have at most {MaxHolidays} closed dates.");

        var holiday = new BusinessHoliday { BusinessId = BusinessId, Date = req.Date, Name = req.Name.Trim() };
        db.BusinessHolidays.Add(holiday);
        audit.Record(BusinessId, ActorId, "business.holiday_added", nameof(BusinessHoliday), holiday.Id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw AppException.Conflict("holiday_exists", "That date is already marked as closed."); }
        return new HolidayDto(holiday.Id, holiday.Date, holiday.Name);
    }

    public async Task RemoveHolidayAsync(Guid holidayId, CancellationToken ct = default)
    {
        var holiday = await db.BusinessHolidays.FirstOrDefaultAsync(h => h.Id == holidayId, ct)
            ?? throw AppException.NotFound("holiday_not_found", "Closed date not found.");
        db.BusinessHolidays.Remove(holiday);
        audit.Record(BusinessId, ActorId, "business.holiday_removed", nameof(BusinessHoliday), holiday.Id);
        await db.SaveChangesAsync(ct);
    }

    private static DayHoursDto ToDto(BusinessHoursDay d)
        => new(d.DayOfWeek, d.IsOpen, d.OpensAt?.ToString("HH:mm", CultureInfo.InvariantCulture), d.ClosesAt?.ToString("HH:mm", CultureInfo.InvariantCulture));

    private static bool TryTime(string? s, out TimeOnly? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(s)) return true;
        if (!TimeOnly.TryParseExact(s.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return false;
        value = t;
        return true;
    }
}
