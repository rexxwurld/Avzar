using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Locations;
using Tailoring.Application.Production;
using Tailoring.Domain.Production;
using Tailoring.Domain.Access;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class WorkerScheduleService(AppDbContext db, ITenantContext tenant, ILocationScope locations, IAuditLogger audit) : IWorkerScheduleService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<WorkerAvailabilityDto> GetAvailabilityAsync(Guid membershipId, CancellationToken ct = default)
    {
        await EnsureMembershipAsync(membershipId, ct);
        var rows = await db.WorkerAvailability.AsNoTracking().Where(x => x.MembershipId == membershipId)
            .OrderBy(x => x.DayOfWeek)
            .Select(x => new WorkerAvailabilityDayInput(x.DayOfWeek, x.IsAvailable, x.OpensAt, x.ClosesAt, x.CapacityMinutes))
            .ToListAsync(ct);
        return new WorkerAvailabilityDto(membershipId, rows.Count > 0, rows);
    }

    public async Task<WorkerAvailabilityDto> SetAvailabilityAsync(Guid membershipId, IReadOnlyList<WorkerAvailabilityDayInput> days, CancellationToken ct = default)
    {
        await EnsureMembershipAsync(membershipId, ct);
        if (days.Count != 7 || days.Select(x => x.DayOfWeek).Distinct().Count() != 7 || days.Any(x => x.DayOfWeek is < 0 or > 6))
            throw AppException.BadRequest("invalid_worker_availability", "Provide exactly one availability entry for every weekday (0 Sunday through 6 Saturday).");
        foreach (var day in days)
        {
            var valid = day.IsAvailable
                ? day.OpensAt is not null && day.ClosesAt is not null && day.OpensAt < day.ClosesAt && day.CapacityMinutes is >= 1 and <= 1440
                : day.OpensAt is null && day.ClosesAt is null && day.CapacityMinutes == 0;
            if (!valid)
                throw AppException.BadRequest("invalid_worker_availability", $"Weekday {day.DayOfWeek} has an invalid time window or daily capacity.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.WorkerAvailability.Where(x => x.MembershipId == membershipId).ToListAsync(ct);
        db.WorkerAvailability.RemoveRange(existing);
        db.WorkerAvailability.AddRange(days.Select(x => new WorkerAvailabilityDay
        {
            BusinessId = BusinessId, MembershipId = membershipId, DayOfWeek = x.DayOfWeek,
            IsAvailable = x.IsAvailable, OpensAt = x.OpensAt, ClosesAt = x.ClosesAt, CapacityMinutes = x.CapacityMinutes
        }));
        audit.Record(BusinessId, tenant.UserId, "worker.availability_updated", nameof(BusinessMembership), membershipId,
            new { availableDays = days.Count(x => x.IsAvailable), dailyCapacityMinutes = days.Where(x => x.IsAvailable).Sum(x => x.CapacityMinutes) });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAvailabilityAsync(membershipId, ct);
    }

    public async Task<IReadOnlyList<WorkerLeaveDto>> ListLeaveAsync(Guid membershipId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        await EnsureMembershipAsync(membershipId, ct);
        ValidateRange(fromUtc, toUtc);
        var rows = await db.WorkerLeaves.AsNoTracking().Where(x => x.MembershipId == membershipId && x.StartsAtUtc < toUtc.UtcDateTime && x.EndsAtUtc > fromUtc.UtcDateTime)
            .OrderBy(x => x.StartsAtUtc)
            .Select(x => new { x.Id, x.MembershipId, x.StartsAtUtc, x.EndsAtUtc, x.Reason, x.Status, x.ReviewNote })
            .ToListAsync(ct);
        return rows.Select(x => new WorkerLeaveDto(x.Id, x.MembershipId, x.StartsAtUtc, x.EndsAtUtc, x.Reason, x.Status.ToString(), x.ReviewNote)).ToList();
    }

    public async Task<WorkerLeaveDto> AddLeaveAsync(Guid membershipId, WorkerLeaveCreateRequest request, CancellationToken ct = default)
    {
        await EnsureMembershipAsync(membershipId, ct);
        if (request.StartsAtUtc.Offset != TimeSpan.Zero || request.EndsAtUtc.Offset != TimeSpan.Zero || request.EndsAtUtc <= request.StartsAtUtc)
            throw AppException.BadRequest("invalid_worker_leave", "Leave times must be UTC and the end must be after the start.");
        var reason = request.Reason.Trim();
        if (reason.Length is < 2 or > 300) throw AppException.BadRequest("invalid_worker_leave", "Leave reason must be between 2 and 300 characters.");
        var start = request.StartsAtUtc.UtcDateTime;
        var end = request.EndsAtUtc.UtcDateTime;
        if (await db.WorkerLeaves.AnyAsync(x => x.MembershipId == membershipId && x.Status == WorkerLeaveStatus.Approved && x.StartsAtUtc < end && x.EndsAtUtc > start, ct))
            throw AppException.Conflict("worker_leave_overlap", "This leave overlaps an existing approved leave interval.");
        var row = new WorkerLeave { BusinessId = BusinessId, MembershipId = membershipId, StartsAtUtc = start, EndsAtUtc = end, Reason = reason, Status = WorkerLeaveStatus.Approved, CreatedByUserId = tenant.UserId };
        db.WorkerLeaves.Add(row);
        audit.Record(BusinessId, tenant.UserId, "worker.leave_added", nameof(WorkerLeave), row.Id,
            new { membershipId, startsAtUtc = start, endsAtUtc = end, reason });
        await db.SaveChangesAsync(ct);
        return new WorkerLeaveDto(row.Id, row.MembershipId, row.StartsAtUtc, row.EndsAtUtc, row.Reason, row.Status.ToString(), row.ReviewNote);
    }


    public async Task<WorkerLeaveDto> RequestMyLeaveAsync(WorkerLeaveCreateRequest request, CancellationToken ct = default)
    {
        var userId = tenant.UserId ?? throw AppException.Unauthorized("not_authenticated", "Sign in to request leave.");
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == userId && m.BusinessId == BusinessId && m.Status == MembershipStatus.Active, ct)
            ?? throw AppException.NotFound("worker_not_found", "No active membership was found in this business.");
        ValidateLeaveRequest(request);
        var row = new WorkerLeave { BusinessId = BusinessId, MembershipId = membership.Id, StartsAtUtc = request.StartsAtUtc.UtcDateTime, EndsAtUtc = request.EndsAtUtc.UtcDateTime, Reason = request.Reason.Trim(), Status = WorkerLeaveStatus.Pending, CreatedByUserId = userId };
        db.WorkerLeaves.Add(row);
        audit.Record(BusinessId, userId, "worker.leave_requested", nameof(WorkerLeave), row.Id, new { row.MembershipId, row.StartsAtUtc, row.EndsAtUtc });
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task<IReadOnlyList<WorkerLeaveDto>> ListPendingLeaveRequestsAsync(CancellationToken ct = default)
    {
        var rows = await db.WorkerLeaves.AsNoTracking().Where(x => x.BusinessId == BusinessId && x.Status == WorkerLeaveStatus.Pending)
            .OrderBy(x => x.StartsAtUtc).Select(x => new { x.Id, x.MembershipId, x.StartsAtUtc, x.EndsAtUtc, x.Reason, x.Status, x.ReviewNote }).ToListAsync(ct);
        return rows.Select(x => new WorkerLeaveDto(x.Id, x.MembershipId, x.StartsAtUtc, x.EndsAtUtc, x.Reason, x.Status.ToString(), x.ReviewNote)).ToList();
    }

    public async Task<WorkerLeaveDto> ReviewLeaveRequestAsync(Guid leaveId, WorkerLeaveReviewRequest request, CancellationToken ct = default)
    {
        var userId = tenant.UserId ?? throw AppException.Unauthorized("not_authenticated", "Sign in to review leave.");
        var row = await db.WorkerLeaves.FirstOrDefaultAsync(x => x.Id == leaveId && x.BusinessId == BusinessId, ct)
            ?? throw AppException.NotFound("worker_leave_not_found", "That leave request was not found.");
        if (row.Status != WorkerLeaveStatus.Pending) throw AppException.Conflict("leave_request_already_reviewed", "Only pending leave requests can be reviewed.");
        var note = request.Note?.Trim();
        if (note?.Length > 500) throw AppException.BadRequest("invalid_review_note", "Review note cannot exceed 500 characters.");
        if (request.Approve && await db.WorkerLeaves.AnyAsync(x => x.Id != row.Id && x.MembershipId == row.MembershipId && x.Status == WorkerLeaveStatus.Approved && x.StartsAtUtc < row.EndsAtUtc && x.EndsAtUtc > row.StartsAtUtc, ct))
            throw AppException.Conflict("worker_leave_overlap", "This request overlaps existing approved leave.");
        row.Status = request.Approve ? WorkerLeaveStatus.Approved : WorkerLeaveStatus.Rejected;
        row.ReviewedByUserId = userId; row.ReviewedAtUtc = DateTime.UtcNow; row.ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note;
        audit.Record(BusinessId, userId, request.Approve ? "worker.leave_approved" : "worker.leave_rejected", nameof(WorkerLeave), row.Id, new { row.MembershipId, row.ReviewNote });
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private static void ValidateLeaveRequest(WorkerLeaveCreateRequest request)
    {
        if (request.StartsAtUtc.Offset != TimeSpan.Zero || request.EndsAtUtc.Offset != TimeSpan.Zero || request.EndsAtUtc <= request.StartsAtUtc)
            throw AppException.BadRequest("invalid_worker_leave", "Leave times must be UTC and the end must be after the start.");
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length is < 2 or > 300) throw AppException.BadRequest("invalid_worker_leave", "Leave reason must be between 2 and 300 characters.");
    }

    private static WorkerLeaveDto ToDto(WorkerLeave row) => new(row.Id, row.MembershipId, row.StartsAtUtc, row.EndsAtUtc, row.Reason, row.Status.ToString(), row.ReviewNote);

    public async Task CancelLeaveAsync(Guid membershipId, Guid leaveId, CancellationToken ct = default)
    {
        await EnsureMembershipAsync(membershipId, ct);
        var row = await db.WorkerLeaves.FirstOrDefaultAsync(x =>
                x.Id == leaveId && x.MembershipId == membershipId && x.BusinessId == BusinessId, ct)
            ?? throw AppException.NotFound("worker_leave_not_found", "That worker leave record was not found.");
        if (row.Status == WorkerLeaveStatus.Cancelled) return;
        row.Status = WorkerLeaveStatus.Cancelled;
        audit.Record(BusinessId, tenant.UserId, "worker.leave_cancelled", nameof(WorkerLeave), row.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WorkerCalendarItemDto>> GetCalendarAsync(Guid membershipId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        await EnsureMembershipAsync(membershipId, ct);
        ValidateRange(fromUtc, toUtc);
        var query = db.ProductionTasks.AsNoTracking().Where(x => x.AssignedMembershipId == membershipId &&
            x.PlannedStartUtc != null && x.PlannedEndUtc != null && x.PlannedStartUtc < toUtc.UtcDateTime && x.PlannedEndUtc > fromUtc.UtcDateTime &&
            (x.Status == ProductionTaskStatus.Scheduled || x.Status == ProductionTaskStatus.InProgress || x.Status == ProductionTaskStatus.Paused));
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        if (allowed is not null)
            query = query.Where(t => db.Orders.Any(o => o.Id == t.OrderId && allowed.Contains(o.LocationId)));
        var rows = await query.OrderBy(x => x.PlannedStartUtc)
            .Select(x => new { x.Id, x.OrderId, x.Title, x.Status, StartUtc = x.PlannedStartUtc!.Value, EndUtc = x.PlannedEndUtc!.Value, x.DeadlineUtc })
            .ToListAsync(ct);
        return rows.Select(x => new WorkerCalendarItemDto(x.Id, x.OrderId, x.Title, x.Status.ToString(), x.StartUtc, x.EndUtc, x.DeadlineUtc)).ToList();
    }

    private async Task EnsureMembershipAsync(Guid membershipId, CancellationToken ct)
    {
        if (!await db.Memberships.AnyAsync(x => x.Id == membershipId && x.BusinessId == BusinessId && x.Status == MembershipStatus.Active, ct))
            throw AppException.NotFound("worker_not_found", "That active worker was not found in this business.");
    }

    private static void ValidateRange(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero || toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(366))
            throw AppException.BadRequest("invalid_calendar_range", "Provide a UTC range with an end after the start, no longer than 366 days.");
    }
}
