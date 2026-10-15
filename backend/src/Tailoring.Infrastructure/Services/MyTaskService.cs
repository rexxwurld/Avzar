using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Production;
using Tailoring.Domain.Access;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>
/// What a worker can do. Every method loads the task THROUGH the caller's own membership, so another worker's task
/// is indistinguishable from a missing one. Workers cannot assign themselves, change deadlines, or skip prerequisites.
/// </summary>
public sealed class MyTaskService(AppDbContext db, ITenantContext tenant) : IMyTaskService
{
    private Guid? _me;

    private async Task<Guid> MeAsync(CancellationToken ct)
    {
        if (_me is { } cached) return cached;
        var userId = tenant.UserId ?? throw AppException.Unauthorized("no_user", "Not signed in.");
        var id = await db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct)
            ?? throw AppException.Forbidden("no_active_membership", "Your account is not an active member of this business.");
        _me = id;
        return id;
    }

    public async Task<PagedResult<TaskSummaryDto>> ListAsync(MyTaskListQuery query, CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var q = db.ProductionTasks.AsNoTracking().Where(t => t.AssignedMembershipId == me);
        q = (query.Show?.ToLowerInvariant()) switch
        {
            "all" => q,
            "completed" => q.Where(t => t.Status == ProductionTaskStatus.Completed),
            _ => q.Where(t => t.Status != ProductionTaskStatus.Completed && t.Status != ProductionTaskStatus.Cancelled),
        };

        var total = await q.CountAsync(ct);
        var paged = q
            .OrderBy(t => t.Priority == OrderPriority.Urgent ? 0 : t.Priority == OrderPriority.High ? 1 : t.Priority == OrderPriority.Normal ? 2 : 3)
            .ThenBy(t => t.DeadlineUtc == null).ThenBy(t => t.DeadlineUtc).ThenBy(t => t.StageOrder)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);

        var now = DateTime.UtcNow;
        var rows = await TaskSupport.Rows(db, paged).ToListAsync(ct);
        return new PagedResult<TaskSummaryDto>(rows.Select(r => TaskSupport.ToSummary(r, now)).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<TaskDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        await LoadOwnAsync(id, ct, track: false);
        return await TaskSupport.LoadDetailAsync(db, id, includeCustomer: false, ct);
    }

    public async Task<TaskDetailDto> StartAsync(Guid id, CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var t = await LoadOwnAsync(id, ct);
        if (t.Status is not (ProductionTaskStatus.Pending or ProductionTaskStatus.Scheduled))
            throw AppException.Conflict("invalid_transition", $"A {t.Status} task cannot be started.");
        await EnsureOrderActiveAsync(t, ct);

        var waitingFor = await UnmetPrerequisiteTitlesAsync(t.Id, excluding: null, ct);
        if (waitingFor.Count > 0)
            throw AppException.Conflict("dependencies_incomplete", $"Waiting for: {string.Join(", ", waitingFor)}.",
                new Dictionary<string, object?> { ["blockedBy"] = waitingFor });
        await EnsureNothingElseInProgressAsync(me, t.Id, ct);

        var fromStatus = t.Status;
        t.Status = ProductionTaskStatus.InProgress;
        t.ActualStartUtc ??= DateTime.UtcNow;
        t.UpdatedByUserId = tenant.UserId;
        TaskSupport.Record(db, t, TaskEventTypes.Started, fromStatus, t.Status, tenant.UserId);
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetailDto> PauseAsync(Guid id, NoteRequest req, CancellationToken ct = default)
    {
        var t = await LoadOwnAsync(id, ct);
        if (t.Status != ProductionTaskStatus.InProgress)
            throw AppException.Conflict("invalid_transition", "Only a task in progress can be paused.");

        t.Status = ProductionTaskStatus.Paused;
        t.UpdatedByUserId = tenant.UserId;
        TaskSupport.Record(db, t, TaskEventTypes.Paused, ProductionTaskStatus.InProgress, t.Status, tenant.UserId, Parse.Clean(req.Note));
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetailDto> ResumeAsync(Guid id, CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var t = await LoadOwnAsync(id, ct);
        if (t.Status != ProductionTaskStatus.Paused)
            throw AppException.Conflict("invalid_transition", "Only a paused task can be resumed.");
        await EnsureOrderActiveAsync(t, ct);
        await EnsureNothingElseInProgressAsync(me, t.Id, ct);

        t.Status = ProductionTaskStatus.InProgress;
        t.UpdatedByUserId = tenant.UserId;
        TaskSupport.Record(db, t, TaskEventTypes.Resumed, ProductionTaskStatus.Paused, t.Status, tenant.UserId);
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetailDto> CompleteAsync(Guid id, CompleteTaskRequest req, CancellationToken ct = default)
    {
        var t = await LoadOwnAsync(id, ct);
        if (t.Status != ProductionTaskStatus.InProgress)
            throw AppException.Conflict("invalid_transition", "Start the task before completing it.");

        var ticked = req.CompletedChecks.Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = t.RequiredChecks.Where(c => !ticked.Contains(c)).ToList();
        if (missing.Count > 0)
            throw AppException.BadRequest("checks_incomplete", $"Tick every checklist item first. Missing: {string.Join("; ", missing)}.");

        var activated = new List<ProductionTask>();
        if (req.AlterationsRequired)
        {
            var precedesConditional = await db.TaskDependencies.AnyAsync(d => d.DependsOnTaskId == t.Id
                && db.ProductionTasks.Any(x => x.Id == d.TaskId && x.IsConditional), ct);
            if (!precedesConditional)
                throw AppException.BadRequest("alterations_not_applicable", "Alterations can only be requested from the step before them (e.g. the fitting).");
            activated = await TaskSupport.ActivateConditionalAsync(db, t.OrderGarmentId, req.AlterationNotes ?? req.Notes, tenant.UserId, ct);
        }

        var activatedIds = activated.Select(a => a.Id).ToList();
        var newlyEligible = await NewlyEligibleAsync(t.Id, activatedIds, ct);
        newlyEligible.AddRange(activatedIds);

        t.Status = ProductionTaskStatus.Completed;
        t.CompletedAtUtc = DateTime.UtcNow;
        t.CompletedChecks = t.RequiredChecks.ToList();
        t.CompletionNotes = Parse.Clean(req.Notes);
        t.UpdatedByUserId = tenant.UserId;
        TaskSupport.Record(db, t, TaskEventTypes.Completed, ProductionTaskStatus.InProgress, t.Status, tenant.UserId,
            t.CompletionNotes, new { newlyEligible, alterationsRequested = req.AlterationsRequired });

        // newlyEligible is what Phase 5 will turn into "your next step is ready" notifications.
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetailDto> ReportIssueAsync(Guid id, ReportIssueRequest req, CancellationToken ct = default)
    {
        var type = Parse.Value<IssueType>(req.Type, "type");
        var t = await LoadOwnAsync(id, ct);
        if (t.Status is ProductionTaskStatus.Completed or ProductionTaskStatus.Cancelled or ProductionTaskStatus.Blocked)
            throw AppException.Conflict("invalid_transition", $"A {t.Status} task cannot be reported on.");

        var fromStatus = t.Status;
        t.StatusBeforeBlock = fromStatus;
        t.Status = ProductionTaskStatus.Blocked;
        t.BlockedType = type;
        t.BlockedReason = req.Description.Trim();
        t.UpdatedByUserId = tenant.UserId;
        TaskSupport.Record(db, t, TaskEventTypes.IssueReported, fromStatus, t.Status, tenant.UserId, t.BlockedReason, new { type = type.ToString() });
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<ProductionTask> LoadOwnAsync(Guid id, CancellationToken ct, bool track = true)
    {
        var me = await MeAsync(ct);
        var q = track ? db.ProductionTasks.AsQueryable() : db.ProductionTasks.AsNoTracking();
        return await q.FirstOrDefaultAsync(t => t.Id == id && t.AssignedMembershipId == me, ct)
            ?? throw AppException.NotFound("task_not_found", "Task not found.");
    }

    private async Task EnsureOrderActiveAsync(ProductionTask t, CancellationToken ct)
    {
        var status = await db.Orders.AsNoTracking().Where(o => o.Id == t.OrderId).Select(o => o.Status).FirstAsync(ct);
        if (status is not (OrderStatus.Confirmed or OrderStatus.InProduction))
            throw AppException.Conflict("order_not_active", "This order is not in production.");
    }

    private async Task EnsureNothingElseInProgressAsync(Guid me, Guid taskId, CancellationToken ct)
    {
        if (await db.ProductionTasks.AnyAsync(x => x.AssignedMembershipId == me
                && x.Status == ProductionTaskStatus.InProgress && x.Id != taskId, ct))
            throw AppException.Conflict("task_in_progress", "Pause or finish the task you are working on first.");
    }

    private async Task<List<string>> UnmetPrerequisiteTitlesAsync(Guid taskId, Guid? excluding, CancellationToken ct)
    {
        var deps = db.TaskDependencies.Where(d => d.TaskId == taskId);
        if (excluding is { } skip) deps = deps.Where(d => d.DependsOnTaskId != skip);   // no nullable comparison in SQL
        return await deps
            .Join(db.ProductionTasks, d => d.DependsOnTaskId, x => x.Id, (d, x) => x)
            .Where(x => x.Status != ProductionTaskStatus.Completed && x.Status != ProductionTaskStatus.Cancelled)
            .OrderBy(x => x.StageOrder).Select(x => x.Title).ToListAsync(ct);
    }

    /// <summary>Dependents of this task that become startable once it is completed.</summary>
    private async Task<List<Guid>> NewlyEligibleAsync(Guid completingTaskId, IReadOnlyCollection<Guid> justActivated, CancellationToken ct)
    {
        var result = new List<Guid>();
        var dependents = await db.TaskDependencies.Where(d => d.DependsOnTaskId == completingTaskId)
            .Join(db.ProductionTasks, d => d.TaskId, x => x.Id, (d, x) => new { x.Id, x.Status }).ToListAsync(ct);
        foreach (var dep in dependents.Where(d => d.Status is ProductionTaskStatus.Pending or ProductionTaskStatus.Scheduled))
        {
            if ((await UnmetPrerequisiteTitlesAsync(dep.Id, excluding: completingTaskId, ct)).Count > 0) continue;
            // Steps re-opened in this same operation are not saved yet, so the DB still sees them as cancelled.
            // Anything that depends on one of them must keep waiting.
            if (justActivated.Count > 0 && await db.TaskDependencies.AnyAsync(d => d.TaskId == dep.Id && justActivated.Contains(d.DependsOnTaskId), ct)) continue;
            result.Add(dep.Id);
        }
        return result;
    }
}
