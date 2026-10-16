using Tailoring.Domain.Tenancy;

namespace Tailoring.Domain.Production;

/// <summary>Inputs for the deterministic first-pass shop scheduler. All instants are UTC; opening hours are business-local.</summary>
public sealed record ScheduleCandidate(
    Guid Id,
    Guid? AssignedMembershipId,
    int? EstimatedMinutes,
    int PriorityRank,
    DateTime? DeadlineUtc,
    int StageOrder,
    IReadOnlyList<Guid> DependsOnTaskIds);

public sealed record ScheduleReservation(Guid? MembershipId, DateTime StartUtc, DateTime EndUtc);
/// <summary>Recurring local-time availability for one worker and weekday. Capacity is total scheduled minutes per local day.</summary>
public sealed record WorkerAvailabilityWindow(Guid MembershipId, int DayOfWeek, TimeOnly? OpensAt, TimeOnly? ClosesAt, int CapacityMinutes, bool IsAvailable = true);
/// <summary>Approved worker leave, represented as a half-open UTC interval [StartUtc, EndUtc).</summary>
public sealed record WorkerLeaveReservation(Guid MembershipId, DateTime StartUtc, DateTime EndUtc);
public sealed record ScheduledSlot(Guid TaskId, DateTime StartUtc, DateTime EndUtc);
public sealed record UnscheduledTask(Guid TaskId, string Reason);
public sealed record SchedulePlan(IReadOnlyList<ScheduledSlot> Scheduled, IReadOnlyList<UnscheduledTask> Unscheduled);

/// <summary>
/// Deterministic scheduler. It respects shop opening hours, holidays, dependencies, existing reservations,
/// recurring worker availability, approved leave, per-worker overlap and daily workload capacity.
/// Workers are never silently assigned and estimates are never invented.
/// </summary>
public static class ProductionSchedulePlanner
{
    public static SchedulePlan Plan(
        IEnumerable<ScheduleCandidate> candidates,
        IEnumerable<ScheduleReservation> reservations,
        IReadOnlyList<DayHoursInput> weeklyHours,
        IReadOnlySet<DateOnly> holidays,
        TimeZoneInfo businessTimeZone,
        DateTime startUtc,
        int horizonDays = 60,
        IEnumerable<WorkerAvailabilityWindow>? workerAvailability = null,
        IEnumerable<WorkerLeaveReservation>? workerLeave = null)
    {
        if (BusinessHoursRules.Validate(weeklyHours) is { } error)
            throw new ArgumentException(error, nameof(weeklyHours));
        if (startUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Start time must be UTC.", nameof(startUtc));
        if (horizonDays is < 1 or > 366) throw new ArgumentOutOfRangeException(nameof(horizonDays));

        var tasks = candidates.ToDictionary(x => x.Id);
        var availability = (workerAvailability ?? []).ToList();
        if (availability.Any(x => x.DayOfWeek is < 0 or > 6 ||
            (x.IsAvailable && (x.OpensAt is null || x.ClosesAt is null || x.OpensAt >= x.ClosesAt || x.CapacityMinutes is < 1 or > 1440)) ||
            (!x.IsAvailable && (x.OpensAt is not null || x.ClosesAt is not null || x.CapacityMinutes != 0))))
            throw new ArgumentException("Worker availability requires a valid weekday and a valid available/closed-day shape.", nameof(workerAvailability));
        if (availability.GroupBy(x => (x.MembershipId, x.DayOfWeek)).Any(g => g.Count() > 1))
            throw new ArgumentException("Only one availability window per worker and weekday is supported.", nameof(workerAvailability));
        var leave = (workerLeave ?? []).Where(x => x.EndUtc > x.StartUtc)
            .Select(x => new MutableReservation(x.MembershipId, AsUtc(x.StartUtc), AsUtc(x.EndUtc), IsLeave: true)).ToList();
        var busy = reservations.Where(x => x.EndUtc > x.StartUtc)
            .Select(x => new MutableReservation(x.MembershipId, AsUtc(x.StartUtc), AsUtc(x.EndUtc))).ToList();
        busy.AddRange(leave);
        var scheduled = new List<ScheduledSlot>();
        var unscheduled = new Dictionary<Guid, string>();
        var remaining = tasks.Keys.ToHashSet();

        // Repeated passes allow dependencies to be scheduled before their dependents even if their
        // priority/deadline ordering differs. A no-progress pass marks unresolved/cyclic work clearly.
        while (remaining.Count > 0)
        {
            var madeProgress = false;
            var ordered = remaining.Select(id => tasks[id])
                .OrderBy(x => x.PriorityRank)
                .ThenBy(x => x.DeadlineUtc ?? DateTime.MaxValue)
                .ThenBy(x => x.StageOrder)
                .ThenBy(x => x.Id)
                .ToList();

            foreach (var task in ordered)
            {
                if (task.AssignedMembershipId is null)
                {
                    unscheduled[task.Id] = "Worker assignment is required.";
                    remaining.Remove(task.Id); madeProgress = true; continue;
                }
                if (task.EstimatedMinutes is null or <= 0)
                {
                    unscheduled[task.Id] = "A positive duration estimate is required.";
                    remaining.Remove(task.Id); madeProgress = true; continue;
                }

                var deps = task.DependsOnTaskIds.Distinct().ToArray();
                if (deps.Any(d => !tasks.ContainsKey(d)))
                {
                    unscheduled[task.Id] = "A prerequisite is missing from the schedule input.";
                    remaining.Remove(task.Id); madeProgress = true; continue;
                }
                if (deps.Any(d => unscheduled.ContainsKey(d)))
                {
                    unscheduled[task.Id] = "A prerequisite could not be scheduled.";
                    remaining.Remove(task.Id); madeProgress = true; continue;
                }
                if (deps.Any(remaining.Contains)) continue;

                var earliest = startUtc;
                var missingDependencyTime = false;
                foreach (var dependencyId in deps)
                {
                    var dependencySlot = scheduled.FirstOrDefault(x => x.TaskId == dependencyId);
                    if (dependencySlot is not null && dependencySlot.EndUtc > earliest) earliest = dependencySlot.EndUtc;
                    else if (dependencySlot is null)
                    {
                        // The caller should omit already-satisfied prerequisites from DependsOnTaskIds.
                        unscheduled[task.Id] = "A prerequisite has no planned completion time.";
                        remaining.Remove(task.Id); madeProgress = true; missingDependencyTime = true; break;
                    }
                }
                if (missingDependencyTime) continue;

                var slot = FindSlot(task.AssignedMembershipId.Value, earliest, task.EstimatedMinutes.Value,
                    businessTimeZone, weeklyHours, holidays, busy, horizonDays, availability);
                if (slot is null)
                {
                    unscheduled[task.Id] = $"No available shop-hours slot found within {horizonDays} days.";
                }
                else
                {
                    scheduled.Add(new ScheduledSlot(task.Id, slot.Value.Start, slot.Value.End));
                    busy.Add(new MutableReservation(task.AssignedMembershipId, slot.Value.Start, slot.Value.End));
                }
                remaining.Remove(task.Id); madeProgress = true;
            }

            if (!madeProgress)
            {
                foreach (var id in remaining)
                    unscheduled[id] = "Circular or unresolved task dependency prevents scheduling.";
                remaining.Clear();
            }
        }

        return new SchedulePlan(scheduled.OrderBy(x => x.StartUtc).ThenBy(x => x.TaskId).ToList(),
            unscheduled.Select(x => new UnscheduledTask(x.Key, x.Value)).ToList());
    }

    private static (DateTime Start, DateTime End)? FindSlot(Guid membershipId, DateTime earliestUtc, int minutes,
        TimeZoneInfo zone, IReadOnlyList<DayHoursInput> week, IReadOnlySet<DateOnly> holidays,
        IReadOnlyList<MutableReservation> busy, int horizonDays,
        IReadOnlyList<WorkerAvailabilityWindow> workerAvailability)
    {
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(earliestUtc, zone));
        for (var offset = 0; offset < horizonDays; offset++)
        {
            var date = firstDate.AddDays(offset);
            if (!BusinessHoursRules.IsOpenOn(date, week, holidays)) continue;
            var hours = week.Single(x => x.DayOfWeek == (int)date.DayOfWeek);
            var localOpenTime = hours.OpensAt!.Value;
            var localCloseTime = hours.ClosesAt!.Value;
            var configuredDays = workerAvailability.Where(x => x.MembershipId == membershipId).ToList();
            WorkerAvailabilityWindow? workerDay = null;
            if (configuredDays.Count > 0)
            {
                workerDay = configuredDays.SingleOrDefault(x => x.DayOfWeek == (int)date.DayOfWeek);
                if (workerDay is null || !workerDay.IsAvailable) continue; // once configured, missing/closed weekdays mean unavailable
                if (workerDay.OpensAt!.Value > localOpenTime) localOpenTime = workerDay.OpensAt.Value;
                if (workerDay.ClosesAt!.Value < localCloseTime) localCloseTime = workerDay.ClosesAt.Value;
                if (localOpenTime >= localCloseTime) continue;
            }
            var localOpen = DateTime.SpecifyKind(date.ToDateTime(localOpenTime), DateTimeKind.Unspecified);
            var localClose = DateTime.SpecifyKind(date.ToDateTime(localCloseTime), DateTimeKind.Unspecified);
            var openUtc = TimeZoneInfo.ConvertTimeToUtc(localOpen, zone);
            var closeUtc = TimeZoneInfo.ConvertTimeToUtc(localClose, zone);
            var start = offset == 0 && earliestUtc > openUtc ? earliestUtc : openUtc;
            if (start >= closeUtc) continue;

            // Move past any reservation overlapping the proposed slot. Repeat because moving past one
            // reservation may expose another adjacent reservation.
            var changed = true;
            while (changed)
            {
                changed = false;
                var end = start.AddMinutes(minutes);
                var conflict = busy.Where(b => b.MembershipId == membershipId && b.StartUtc < end && b.EndUtc > start)
                    .OrderBy(b => b.EndUtc).FirstOrDefault();
                if (conflict is not null) { start = conflict.EndUtc; changed = true; }
            }
            var plannedEnd = start.AddMinutes(minutes);
            if (plannedEnd > closeUtc) continue;
            if (workerDay is not null)
            {
                var dayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), zone);
                var dayEnd = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), zone);
                var usedMinutes = busy.Where(b => b.MembershipId == membershipId && b.StartUtc < dayEnd && b.EndUtc > dayStart)
                    .Where(b => !b.IsLeave)
                    .Sum(b => (int)Math.Ceiling(((b.EndUtc < dayEnd ? b.EndUtc : dayEnd) - (b.StartUtc > dayStart ? b.StartUtc : dayStart)).TotalMinutes));
                if (usedMinutes + minutes > workerDay.CapacityMinutes) continue;
            }
            return (start, plannedEnd);
        }
        return null;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private sealed record MutableReservation(Guid? MembershipId, DateTime StartUtc, DateTime EndUtc, bool IsLeave = false);
}
