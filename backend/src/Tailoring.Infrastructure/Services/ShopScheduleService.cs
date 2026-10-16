using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Email;
using Tailoring.Application.Locations;
using Tailoring.Application.Production;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Access;
using Tailoring.Domain.Email;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>Builds tenant/location-scoped previews and applies complete plans with optimistic concurrency and append-only task events.</summary>
public sealed class ShopScheduleService(
    AppDbContext db,
    Tailoring.Application.Abstractions.ITenantContext tenant,
    IEntitlementService entitlements,
    ILocationScope locations) : IShopScheduleService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<ShopSchedulePreviewDto> PreviewAsync(ShopSchedulePreviewRequest request, CancellationToken ct = default)
    {
        var businessId = BusinessId;
        await entitlements.RequireFlagAsync(businessId, EntitlementKeys.SchedulingShop, ct);
        if (request.StartUtc.Offset != TimeSpan.Zero)
            throw AppException.BadRequest("schedule_start_must_be_utc", "StartUtc must use the UTC offset (+00:00).");
        if (request.HorizonDays is < 1 or > 366)
            throw AppException.BadRequest("invalid_schedule_horizon", "HorizonDays must be between 1 and 366.");

        var business = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == businessId, ct);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId); }
        catch (TimeZoneNotFoundException) { throw AppException.Conflict("invalid_business_time_zone", "The business time zone is not available on this server."); }
        catch (InvalidTimeZoneException) { throw AppException.Conflict("invalid_business_time_zone", "The business time zone configuration is invalid."); }

        var weeklyHours = await db.BusinessHours.AsNoTracking()
            .OrderBy(x => x.DayOfWeek)
            .Select(x => new DayHoursInput(x.DayOfWeek, x.IsOpen, x.OpensAt, x.ClosesAt))
            .ToListAsync(ct);
        if (BusinessHoursRules.Validate(weeklyHours) is { } hoursError)
            throw AppException.Conflict("business_hours_incomplete", hoursError);

        var startUtc = request.StartUtc.UtcDateTime;
        var endUtc = startUtc.AddDays(request.HorizonDays);
        var holidays = (await db.BusinessHolidays.AsNoTracking()
            .Where(x => x.Date >= DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone))
                     && x.Date <= DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(endUtc, zone)))
            .Select(x => x.Date).ToListAsync(ct)).ToHashSet();

        var query = db.ProductionTasks.AsNoTracking().AsQueryable();
        // Location scope is a required dependency: never silently fall back to tenant-wide access
        // if DI registration is broken or a caller constructs this service incorrectly.
        var allowedLocations = await locations.AllowedLocationIdsAsync(ct);
        if (allowedLocations is not null)
            query = query.Where(t => db.Orders.Any(o => o.Id == t.OrderId && allowedLocations.Contains(o.LocationId)));

        var rows = await query.Where(t => t.Status == ProductionTaskStatus.Pending || t.Status == ProductionTaskStatus.Scheduled)
            .Select(t => new
            {
                t.Id, t.AssignedMembershipId, t.EstimatedMinutes, t.Priority, t.DeadlineUtc,
                t.StageOrder, t.PlannedStartUtc, t.PlannedEndUtc, t.Version
            }).ToListAsync(ct);
        var taskIds = rows.Select(x => x.Id).ToHashSet();
        var dependencies = await db.TaskDependencies.AsNoTracking()
            .Where(d => taskIds.Contains(d.TaskId))
            .Select(d => new { d.TaskId, d.DependsOnTaskId })
            .ToListAsync(ct);
        var dependencyIds = dependencies.Select(d => d.DependsOnTaskId).Distinct().ToArray();
        var satisfiedDependencies = dependencyIds.Length == 0
            ? new List<Guid>()
            : await db.ProductionTasks.AsNoTracking()
                .Where(t => dependencyIds.Contains(t.Id)
                    && (t.Status == ProductionTaskStatus.Completed || t.Status == ProductionTaskStatus.Cancelled))
                .Select(t => t.Id).ToListAsync(ct);
        var satisfiedIds = satisfiedDependencies.ToHashSet();
        var dependencyMap = dependencies.GroupBy(x => x.TaskId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<Guid>)g.Select(x => x.DependsOnTaskId).Where(id => !satisfiedIds.Contains(id)).Distinct().ToArray());

        var candidateIds = taskIds;
        var reservations = await db.ProductionTasks.AsNoTracking()
            .Where(t => !candidateIds.Contains(t.Id)
                && t.AssignedMembershipId != null
                && t.PlannedStartUtc != null && t.PlannedEndUtc != null
                // A worker may be assigned across locations. Include already-planned work outside
                // the caller's visible location scope as anonymous busy time so a restricted manager
                // cannot accidentally create cross-location overlaps. Do not return these task details.
                && (t.Status == ProductionTaskStatus.Scheduled
                    || t.Status == ProductionTaskStatus.InProgress
                    || t.Status == ProductionTaskStatus.Paused))
            .Select(t => new ScheduleReservation(t.AssignedMembershipId, t.PlannedStartUtc!.Value, t.PlannedEndUtc!.Value))
            .ToListAsync(ct);

        var candidates = rows.Select(t => new ScheduleCandidate(
            t.Id, t.AssignedMembershipId, t.EstimatedMinutes,
            3 - (int)t.Priority, t.DeadlineUtc, t.StageOrder,
            dependencyMap.GetValueOrDefault(t.Id, Array.Empty<Guid>()))).ToList();

        var workerIds = candidates.Where(x => x.AssignedMembershipId != null).Select(x => x.AssignedMembershipId!.Value).Distinct().ToArray();
        var workerAvailability = workerIds.Length == 0
            ? new List<WorkerAvailabilityWindow>()
            : await db.WorkerAvailability.AsNoTracking().Where(x => workerIds.Contains(x.MembershipId))
                .Select(x => new WorkerAvailabilityWindow(x.MembershipId, x.DayOfWeek, x.OpensAt, x.ClosesAt, x.CapacityMinutes, x.IsAvailable))
                .ToListAsync(ct);
        var workerLeave = workerIds.Length == 0
            ? new List<WorkerLeaveReservation>()
            : await db.WorkerLeaves.AsNoTracking().Where(x => workerIds.Contains(x.MembershipId)
                    && x.Status == WorkerLeaveStatus.Approved && x.StartsAtUtc < endUtc && x.EndsAtUtc > startUtc)
                .Select(x => new WorkerLeaveReservation(x.MembershipId, x.StartsAtUtc, x.EndsAtUtc))
                .ToListAsync(ct);

        SchedulePlan plan;
        try
        {
            plan = ProductionSchedulePlanner.Plan(candidates, reservations, weeklyHours, holidays, zone, startUtc,
                request.HorizonDays, workerAvailability, workerLeave);
        }
        catch (ArgumentException ex)
        {
            throw AppException.Conflict("schedule_cannot_be_planned", ex.Message);
        }

        var versions = rows.ToDictionary(x => x.Id, x => x.Version);
        return new ShopSchedulePreviewDto(startUtc, request.HorizonDays, business.TimeZoneId,
            plan.Scheduled.Select(x => new ScheduledSlotDto(x.TaskId, x.StartUtc, x.EndUtc, versions[x.TaskId])).ToArray(),
            plan.Unscheduled.Select(x => new UnscheduledTaskDto(x.TaskId, x.Reason)).ToArray(),
            versions,
            IsPreviewOnly: true);
    }

    public async Task<ShopScheduleApplyDto> ApplyAsync(ShopScheduleApplyRequest request, CancellationToken ct = default)
    {
        var businessId = BusinessId;
        await entitlements.RequireFlagAsync(businessId, EntitlementKeys.SchedulingShop, ct);
        if (request.StartUtc.Offset != TimeSpan.Zero)
            throw AppException.BadRequest("schedule_start_must_be_utc", "StartUtc must use the UTC offset (+00:00).");
        if (request.HorizonDays is < 1 or > 366)
            throw AppException.BadRequest("invalid_schedule_horizon", "HorizonDays must be between 1 and 366.");
        if (request.ExpectedTaskVersions is null || request.ExpectedTaskVersions.Count == 0 || request.ExpectedSlots is null || request.ExpectedSlots.Count == 0)
            throw AppException.BadRequest("schedule_preview_required", "Submit the task versions and proposed slots returned by the schedule preview.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Recalculate rather than trusting client-supplied slot times. The row versions bind this apply
            // operation to the exact task set seen by the preview screen.
            var preview = await PreviewAsync(new ShopSchedulePreviewRequest
            {
                StartUtc = request.StartUtc,
                HorizonDays = request.HorizonDays
            }, ct);

            if (preview.TaskVersions.Count != request.ExpectedTaskVersions.Count ||
                preview.TaskVersions.Any(x => !request.ExpectedTaskVersions.TryGetValue(x.Key, out var expected) || expected != x.Value))
                throw AppException.Conflict("schedule_preview_stale", "Tasks changed after this preview. Generate a new preview before applying it.");

            if (preview.Unscheduled.Count > 0 || preview.Scheduled.Count != preview.TaskVersions.Count)
                throw AppException.Conflict("schedule_has_unscheduled_tasks", "The plan contains tasks that cannot be scheduled. Resolve them and generate a new preview before applying the schedule.");

            var expectedSlots = request.ExpectedSlots.OrderBy(x => x.TaskId).ToArray();
            var currentSlots = preview.Scheduled.OrderBy(x => x.TaskId).ToArray();
            if (expectedSlots.Length != currentSlots.Length || expectedSlots.Where((expected, index) =>
                    expected.TaskId != currentSlots[index].TaskId || expected.StartUtc != currentSlots[index].StartUtc ||
                    expected.EndUtc != currentSlots[index].EndUtc || expected.Version != currentSlots[index].Version).Any())
                throw AppException.Conflict("schedule_preview_stale", "The proposed times or scheduling inputs changed after preview. Generate a new preview before applying the schedule.");

            var ids = preview.Scheduled.Select(x => x.TaskId).ToArray();
            var tasks = await db.ProductionTasks.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
            if (tasks.Count != ids.Length)
                throw AppException.Conflict("schedule_preview_stale", "The task set changed after preview. Generate a new preview.");

            foreach (var task in tasks)
            {
                if (!request.ExpectedTaskVersions.TryGetValue(task.Id, out var expected) || task.Version != expected)
                    throw AppException.Conflict("schedule_preview_stale", "A task changed after preview. Generate a new preview before applying the schedule.");
                if (task.Status is not (ProductionTaskStatus.Pending or ProductionTaskStatus.Scheduled))
                    throw AppException.Conflict("schedule_task_state_changed", "A task is no longer eligible for scheduling. Generate a new preview.");
            }

            var byId = tasks.ToDictionary(x => x.Id);
            var actor = tenant.UserId;
            var appliedAt = DateTime.UtcNow;
            foreach (var slot in preview.Scheduled)
            {
                var task = byId[slot.TaskId];
                var previousStart = task.PlannedStartUtc;
                var previousEnd = task.PlannedEndUtc;
                var previousStatus = task.Status;
                task.PlannedStartUtc = slot.StartUtc;
                task.PlannedEndUtc = slot.EndUtc;
                task.Status = ProductionTaskStatus.Scheduled;
                task.UpdatedByUserId = actor;
                TaskSupport.Record(db, task, TaskEventTypes.ScheduleApplied, previousStatus, task.Status, actor,
                    note: "Shop schedule applied.", data: new
                    {
                        previousStartUtc = previousStart,
                        previousEndUtc = previousEnd,
                        plannedStartUtc = slot.StartUtc,
                        plannedEndUtc = slot.EndUtc,
                        appliedAtUtc = appliedAt
                    });
            }

            var notificationsQueued = await QueueScheduleNotificationsAsync(businessId, tasks, preview.Scheduled, appliedAt, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new ShopScheduleApplyDto(tasks.Count, appliedAt, notificationsQueued);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            throw AppException.Conflict("schedule_preview_stale", "A task changed while the schedule was being applied. Generate a new preview and try again.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
    private async Task<bool> QueueScheduleNotificationsAsync(Guid businessId, IReadOnlyCollection<ProductionTask> tasks,
        IReadOnlyList<ScheduledSlotDto> slots, DateTime nowUtc, CancellationToken ct)
    {
        var membershipIds = tasks.Where(t => t.AssignedMembershipId != null).Select(t => t.AssignedMembershipId!.Value).Distinct().ToArray();
        if (membershipIds.Length == 0) return false;
        var recipients = await (from membership in db.Memberships.AsNoTracking()
                                join user in db.Users.AsNoTracking() on membership.UserId equals user.Id
                                where membership.BusinessId == businessId && membershipIds.Contains(membership.Id)
                                select new { membership.Id, membership.DisplayName, user.Email })
            .ToDictionaryAsync(x => x.Id, ct);
        var byId = tasks.ToDictionary(t => t.Id);
        var expectedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            var task = byId[slot.TaskId];
            if (task.AssignedMembershipId is not { } memberId) continue;
            expectedKeys.Add($"schedule-assignment:{task.Id:N}:{memberId:N}:{slot.StartUtc.Ticks}");
            if (slot.StartUtc.AddHours(-24) > nowUtc)
                expectedKeys.Add($"schedule-reminder:{task.Id:N}:{memberId:N}:{slot.StartUtc.Ticks}");
        }
        // A newer plan supersedes queued mail for an old time. Keep the exact keys for unchanged slots;
        // terminal messages are immutable and cannot be unsent.
        var pendingScheduleMail = await db.EmailOutbox.Where(x => x.BusinessId == businessId && x.Status == EmailStatus.Pending &&
            (x.TemplateKey == "schedule.assignment" || x.TemplateKey == "schedule.reminder") && x.DeduplicationKey != null).ToListAsync(ct);
        var taskPrefixes = tasks.Select(t => t.Id.ToString("N")).ToHashSet(StringComparer.Ordinal);
        foreach (var oldMail in pendingScheduleMail)
        {
            var key = oldMail.DeduplicationKey!;
            var belongsToTask = taskPrefixes.Any(id => key.StartsWith($"schedule-assignment:{id}:", StringComparison.Ordinal) || key.StartsWith($"schedule-reminder:{id}:", StringComparison.Ordinal));
            if (!belongsToTask || expectedKeys.Contains(key)) continue;
            oldMail.Status = EmailStatus.Failed;
            oldMail.LastError = "Superseded by a newer schedule.";
            oldMail.TextBody = "";
            oldMail.HtmlBody = null;
            oldMail.BodyPurgedAtUtc = nowUtc;
            oldMail.LeaseUntilUtc = null;
        }
        var queued = false;
        foreach (var slot in slots)
        {
            var task = byId[slot.TaskId];
            if (task.AssignedMembershipId is not { } memberId || !recipients.TryGetValue(memberId, out var recipient) ||
                !EmailAddressRules.TryNormalize(recipient.Email, out var email)) continue;

            var localStart = slot.StartUtc.ToString("yyyy-MM-dd 'at' HH:mm 'UTC'");
            var assignmentKey = $"schedule-assignment:{task.Id:N}:{memberId:N}:{slot.StartUtc.Ticks}";
            if (!await db.EmailOutbox.AnyAsync(x => x.DeduplicationKey == assignmentKey, ct))
            {
                db.EmailOutbox.Add(new EmailOutboxMessage
                {
                    BusinessId = businessId,
                    ToAddress = email,
                    Subject = "A production task has been scheduled",
                    TextBody = $"Hello {recipient.DisplayName},\n\nYour task '{task.Title}' is scheduled for {localStart}.\n\nPlease open Avzar to view the task details.",
                    TemplateKey = "schedule.assignment",
                    DeduplicationKey = assignmentKey,
                    NextAttemptUtc = nowUtc
                });
                queued = true;
            }

            var reminderAt = slot.StartUtc.AddHours(-24);
            var reminderKey = $"schedule-reminder:{task.Id:N}:{memberId:N}:{slot.StartUtc.Ticks}";
            if (reminderAt > nowUtc && !await db.EmailOutbox.AnyAsync(x => x.DeduplicationKey == reminderKey, ct))
            {
                db.EmailOutbox.Add(new EmailOutboxMessage
                {
                    BusinessId = businessId,
                    ToAddress = email,
                    Subject = "Reminder: production task scheduled for tomorrow",
                    TextBody = $"Hello {recipient.DisplayName},\n\nReminder: your task '{task.Title}' is scheduled for {localStart}.\n\nPlease open Avzar to view the task details.",
                    TemplateKey = "schedule.reminder",
                    DeduplicationKey = reminderKey,
                    NextAttemptUtc = reminderAt
                });
                queued = true;
            }
        }
        return queued;
    }

}
