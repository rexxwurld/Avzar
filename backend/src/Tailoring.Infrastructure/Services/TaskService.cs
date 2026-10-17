using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Locations;
using Tailoring.Application.Production;
using Tailoring.Domain.Access;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class TaskService(
    AppDbContext db, ITenantContext tenant, IAuditLogger audit,
    IProductionPlanner planner, IPermissionService permissions, ILocationScope locations) : ITaskService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");
    private Guid? ActorId => tenant.UserId;

    // ------------------------------------------------------------------ location scope
    // A task follows the location of its order. Callers restricted to some locations (not the owner, no locations.all) see only those;
    // a hidden task or order looks exactly like a missing one. System/test scopes explicitly return null for unrestricted access.

    private async Task<Guid[]?> AllowedLocationsAsync(CancellationToken ct)
        => await locations.AllowedLocationIdsAsync(ct);

    private async Task<bool> OrderVisibleAsync(Guid orderId, CancellationToken ct)
    {
        var allowed = await AllowedLocationsAsync(ct);
        return allowed is null || await db.Orders.AnyAsync(o => o.Id == orderId && allowed.Contains(o.LocationId), ct);
    }

    private async Task EnsureOrderVisibleAsync(Guid orderId, CancellationToken ct)
    {
        if (!await OrderVisibleAsync(orderId, ct)) throw AppException.NotFound("order_not_found", "Order not found.");
    }

    // ------------------------------------------------------------------ queries

    public async Task<PagedResult<TaskSummaryDto>> ListAsync(TaskListQuery query, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var q = db.ProductionTasks.AsNoTracking().AsQueryable();
        var allowedLocations = await AllowedLocationsAsync(ct);
        if (allowedLocations is not null)
            q = q.Where(t => db.Orders.Any(o => o.Id == t.OrderId && allowedLocations.Contains(o.LocationId)));

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = Parse.Value<ProductionTaskStatus>(query.Status, "status");
            q = q.Where(t => t.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            var priority = Parse.Value<OrderPriority>(query.Priority, "priority");
            q = q.Where(t => t.Priority == priority);
        }
        if (query.OrderId is { } oid) q = q.Where(t => t.OrderId == oid);
        if (query.GarmentId is { } gid) q = q.Where(t => t.OrderGarmentId == gid);
        if (query.AssigneeId is { } aid) q = q.Where(t => t.AssignedMembershipId == aid);
        if (query.Unassigned == true) q = q.Where(t => t.AssignedMembershipId == null && t.Status != ProductionTaskStatus.Cancelled && t.Status != ProductionTaskStatus.Completed);
        if (!string.IsNullOrWhiteSpace(query.Stage))
        {
            var pattern = $"%{Parse.Like(query.Stage.Trim())}%";
            q = q.Where(t => EF.Functions.ILike(t.StageName, pattern));
        }
        if (query.DueBefore is { } due) { var d = due.UtcDateTime; q = q.Where(t => t.DeadlineUtc != null && t.DeadlineUtc < d); }
        if (query.Overdue == true)
            q = q.Where(t => t.Status != ProductionTaskStatus.Completed && t.Status != ProductionTaskStatus.Cancelled
                          && t.DeadlineUtc != null && t.DeadlineUtc < now);
        if (query.Eligible == true)
            q = q.Where(t => (t.Status == ProductionTaskStatus.Pending || t.Status == ProductionTaskStatus.Scheduled)
                && !db.TaskDependencies.Any(dep => dep.TaskId == t.Id && db.ProductionTasks.Any(x =>
                    x.Id == dep.DependsOnTaskId && x.Status != ProductionTaskStatus.Completed && x.Status != ProductionTaskStatus.Cancelled))
                && db.Orders.Any(o => o.Id == t.OrderId && (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.InProduction)));

        var total = await q.CountAsync(ct);
        var paged = q
            .OrderBy(t => t.Priority == OrderPriority.Urgent ? 0 : t.Priority == OrderPriority.High ? 1 : t.Priority == OrderPriority.Normal ? 2 : 3)
            .ThenBy(t => t.DeadlineUtc == null).ThenBy(t => t.DeadlineUtc).ThenBy(t => t.StageOrder)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);

        var rows = await TaskSupport.Rows(db, paged).ToListAsync(ct);
        return new PagedResult<TaskSummaryDto>(rows.Select(r => TaskSupport.ToSummary(r, now)).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<TaskDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureTaskVisibleAsync(id, ct);
        return await TaskSupport.LoadDetailAsync(db, id, includeCustomer: true, ct);
    }

    // ------------------------------------------------------------------ create / edit

    public async Task<TaskDetailDto> CreateAsync(Guid orderId, CreateTaskRequest req, CancellationToken ct = default)
    {
        await EnsureOrderVisibleAsync(orderId, ct);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw AppException.NotFound("order_not_found", "Order not found.");
        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.InProduction or OrderStatus.Ready))
            throw AppException.Conflict("order_not_active", "Tasks can only be added to confirmed orders that are not yet delivered.");

        var priority = Parse.Value<OrderPriority>(req.Priority, "priority");

        var garmentIds = await db.OrderGarments.Where(g => g.OrderId == orderId).Select(g => g.Id).ToListAsync(ct);
        var garmentId = req.OrderGarmentId ?? (garmentIds.Count == 1 ? garmentIds[0] : (Guid?)null)
            ?? throw AppException.BadRequest("garment_required", "Choose which garment this task is for.");
        if (!garmentIds.Contains(garmentId))
            throw AppException.BadRequest("unknown_garment", "That garment does not belong to this order.");

        var depIds = req.DependsOnTaskIds.Distinct().ToList();
        if (depIds.Count > 0 &&
            await db.ProductionTasks.CountAsync(t => depIds.Contains(t.Id) && t.OrderId == orderId, ct) != depIds.Count)
            throw AppException.BadRequest("unknown_dependency", "Every prerequisite must be a task of the same order.");

        var zoneId = await db.Businesses.AsNoTracking().Select(b => b.TimeZoneId).FirstAsync(ct);
        var task = new ProductionTask
        {
            BusinessId = BusinessId, OrderId = orderId, OrderGarmentId = garmentId,
            StageName = Parse.Clean(req.StageName) ?? "Additional work", StageOrder = 1000,
            Title = req.Title.Trim(), Instructions = Parse.Clean(req.Instructions),
            Quantity = await db.OrderGarments.Where(g => g.Id == garmentId).Select(g => g.Quantity).FirstAsync(ct),
            RequiredSkill = SkillName.Normalize(req.RequiredSkill), EstimatedMinutes = req.EstimatedMinutes,
            Priority = priority,
            DeadlineUtc = req.Deadline?.UtcDateTime
                ?? TaskPlanner.EndOfDayUtc(order.PromisedDate, TimeZoneInfo.FindSystemTimeZoneById(zoneId)),
            RequiredChecks = req.CompletionChecks.Select(c => c.Trim()).Where(c => c.Length > 0).ToList(),
            RequiredMaterialsJson = TaskSupport.SerializeMaterials(req.RequiredMaterials),
            CreatedByUserId = ActorId
        };
        db.ProductionTasks.Add(task);
        db.TaskDependencies.AddRange(depIds.Select(d => new TaskDependency { BusinessId = BusinessId, TaskId = task.Id, DependsOnTaskId = d }));
        TaskSupport.Record(db, task, TaskEventTypes.Created, null, task.Status, ActorId);
        audit.Record(BusinessId, ActorId, "task.created", nameof(ProductionTask), task.Id, new { orderId, task.Title });

        await TaskSupport.SaveWithOrderSyncAsync(db, orderId, ct);   // reopens a Ready order if needed
        return await GetAsync(task.Id, ct);
    }

    public async Task<TaskDetailDto> UpdateAsync(Guid id, UpdateTaskRequest req, CancellationToken ct = default)
    {
        var t = await LoadAsync(id, ct);
        EnsureOpen(t);
        db.Entry(t).Property(x => x.Version).OriginalValue = req.Version;

        var changed = new List<string>();
        if (req.Instructions is not null && req.Instructions != t.Instructions) { t.Instructions = Parse.Clean(req.Instructions); changed.Add("instructions"); }
        if (req.Priority is not null)
        {
            var p = Parse.Value<OrderPriority>(req.Priority, "priority");
            if (p != t.Priority) { t.Priority = p; changed.Add("priority"); }
        }
        if (req.Deadline is { } dl && dl.UtcDateTime != t.DeadlineUtc) { t.DeadlineUtc = dl.UtcDateTime; changed.Add("deadline"); }
        if (req.EstimatedMinutes is { } est && est != t.EstimatedMinutes) { t.EstimatedMinutes = est; changed.Add("estimate"); }
        if (req.RequiredSkill is not null)
        {
            var skill = SkillName.Normalize(req.RequiredSkill);
            if (skill != t.RequiredSkill) { t.RequiredSkill = skill; changed.Add("skill"); }
        }
        if (req.RequiredMaterials is not null) { t.RequiredMaterialsJson = TaskSupport.SerializeMaterials(req.RequiredMaterials); changed.Add("materials"); }

        t.UpdatedByUserId = ActorId;
        t.UpdatedAtUtc = DateTime.UtcNow;   // forces an UPDATE so a stale Version is always detected
        TaskSupport.Record(db, t, TaskEventTypes.Updated, t.Status, t.Status, ActorId, data: new { changed });
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ assignment

    public async Task<TaskDetailDto> AssignAsync(Guid id, AssignTaskRequest req, CancellationToken ct = default)
    {
        var t = await LoadAsync(id, ct);
        if (t.Status is ProductionTaskStatus.Completed or ProductionTaskStatus.Cancelled or ProductionTaskStatus.InProgress)
            throw AppException.Conflict("task_not_assignable", $"A {t.Status} task cannot be reassigned. Pause it first if it is in progress.");

        if (req.MembershipId is null)
        {
            if (t.AssignedMembershipId is null) return await GetAsync(id, ct);
            var previous = t.AssignedMembershipId;
            t.AssignedMembershipId = null;
            t.UpdatedByUserId = ActorId;
            TaskSupport.Record(db, t, TaskEventTypes.Unassigned, t.Status, t.Status, ActorId, req.Reason, new { previous });
            await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
            return await GetAsync(id, ct);
        }

        var memberId = req.MembershipId.Value;
        var allowedLocations = await locations.AllowedLocationIdsAsync(ct);
        if (allowedLocations is not null && !await db.MembershipLocations.AnyAsync(l =>
                l.MembershipId == memberId && allowedLocations.Contains(l.LocationId), ct))
            throw AppException.BadRequest("unknown_worker", "That person is not an active member of your permitted workshop locations.");
        if (t.AssignedMembershipId == memberId) return await GetAsync(id, ct);

        var member = await db.Memberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == memberId && m.Status == MembershipStatus.Active, ct)
            ?? throw AppException.BadRequest("unknown_worker", "That person is not an active member of your business.");

        var problems = new List<string>();
        if (t.RequiredSkill is { } skill &&
            !await db.WorkerSkills.AnyAsync(s => s.MembershipId == memberId && s.Skill == skill, ct))
            problems.Add($"{member.DisplayName} does not have the '{skill}' skill.");

        var locationId = await db.Orders.Where(o => o.Id == t.OrderId).Select(o => o.LocationId).FirstAsync(ct);
        if (!await db.MembershipLocations.AnyAsync(l => l.MembershipId == memberId && l.LocationId == locationId, ct))
            problems.Add($"{member.DisplayName} is not assigned to this order's workshop.");

        if (problems.Count > 0)
        {
            if (!req.Override)
                throw AppException.Conflict("assignment_not_allowed",
                    string.Join(" ", problems) + " An authorised manager can override this with a reason.",
                    new Dictionary<string, object?> { ["problems"] = problems });
            await RequireOverrideAsync(req.Reason, ct);
        }

        var before = t.AssignedMembershipId;
        t.AssignedMembershipId = memberId;
        t.UpdatedByUserId = ActorId;
        t.UpdatedAtUtc = DateTime.UtcNow;
        var overridden = problems.Count > 0;
        TaskSupport.Record(db, t, overridden ? TaskEventTypes.OverrideAssign : TaskEventTypes.Assigned, t.Status, t.Status, ActorId,
            req.Reason, new { before, after = memberId, problems });
        if (overridden)
            audit.Record(BusinessId, ActorId, "task.override_assign", nameof(ProductionTask), t.Id, new { reason = req.Reason, problems });
        if (before != memberId)
            await ProductionNotificationQueue.QueueMembershipAsync(db, BusinessId, t, memberId,
                "task.assignment", "A production task has been assigned to you",
                $"Task '{t.Title}' has been assigned to you.", ct);
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ state changes by managers

    public async Task<TaskDetailDto> UnblockAsync(Guid id, NoteRequest req, CancellationToken ct = default)
    {
        var t = await LoadAsync(id, ct);
        if (t.Status != ProductionTaskStatus.Blocked)
            throw AppException.Conflict("invalid_transition", "Only blocked tasks can be unblocked.");

        var issue = t.BlockedType;
        // Work that had already begun resumes as Paused, so the worker must explicitly resume it.
        var back = t.StatusBeforeBlock == ProductionTaskStatus.InProgress || t.ActualStartUtc is not null
            ? ProductionTaskStatus.Paused : ProductionTaskStatus.Pending;
        t.Status = back;
        t.StatusBeforeBlock = null; t.BlockedType = null; t.BlockedReason = null;
        t.UpdatedByUserId = ActorId;
        t.UpdatedAtUtc = DateTime.UtcNow;
        TaskSupport.Record(db, t, TaskEventTypes.Unblocked, ProductionTaskStatus.Blocked, back, ActorId, req.Note, new { issue = issue?.ToString() });
        if (t.AssignedMembershipId is { } memberId)
            await ProductionNotificationQueue.QueueMembershipAsync(db, BusinessId, t, memberId,
                "task.unblocked", "A blocked production task is ready for review",
                $"Task '{t.Title}' has been unblocked and is now {back}.", ct);
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetailDto> CancelAsync(Guid id, CancelTaskRequest req, CancellationToken ct = default)
    {
        var t = await LoadAsync(id, ct);
        EnsureOpen(t);
        var fromStatus = t.Status;
        t.Status = ProductionTaskStatus.Cancelled;
        t.CancelReason = req.Reason.Trim();
        t.UpdatedByUserId = ActorId;
        TaskSupport.Record(db, t, TaskEventTypes.Cancelled, fromStatus, t.Status, ActorId, t.CancelReason);
        audit.Record(BusinessId, ActorId, "task.cancelled", nameof(ProductionTask), t.Id, new { reason = t.CancelReason });
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetailDto> OverrideAsync(Guid id, OverrideTaskRequest req, CancellationToken ct = default)
    {
        var action = Parse.Value<OverrideAction>(req.Action, "action");
        await EnsureTaskVisibleAsync(id, ct);
        await RequireOverrideAsync(req.Reason, ct);

        var t = await LoadAsync(id, ct);
        EnsureOpen(t);
        if (t.AssignedMembershipId is null)
            throw AppException.Conflict("assignee_required", "Assign the task to someone first so the work is attributed correctly.");
        var orderStatus = await db.Orders.Where(o => o.Id == t.OrderId).Select(o => o.Status).FirstAsync(ct);
        if (orderStatus is not (OrderStatus.Confirmed or OrderStatus.InProduction or OrderStatus.Ready))
            throw AppException.Conflict("order_not_active", "This order is no longer in production.");

        var fromStatus = t.Status;
        if (action == OverrideAction.Start)
        {
            t.Status = ProductionTaskStatus.InProgress;
            t.ActualStartUtc ??= DateTime.UtcNow;
            t.StatusBeforeBlock = null; t.BlockedType = null; t.BlockedReason = null;
        }
        else
        {
            t.Status = ProductionTaskStatus.Completed;
            t.CompletedAtUtc = DateTime.UtcNow;
            t.CompletionNotes = Parse.Truncate($"Completed by manager override: {req.Reason.Trim()}", 2000);
        }
        t.UpdatedByUserId = ActorId;
        var type = action == OverrideAction.Start ? TaskEventTypes.OverrideStart : TaskEventTypes.OverrideComplete;
        TaskSupport.Record(db, t, type, fromStatus, t.Status, ActorId, req.Reason.Trim());
        audit.Record(BusinessId, ActorId, $"task.{type}", nameof(ProductionTask), t.Id, new { reason = req.Reason.Trim(), previousStatus = fromStatus.ToString() });
        await TaskSupport.SaveWithOrderSyncAsync(db, t.OrderId, ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ order-level actions

    public async Task<GenerateTasksResult> GenerateForOrderAsync(Guid orderId, GenerateTasksRequest req, CancellationToken ct = default)
    {
        await EnsureOrderVisibleAsync(orderId, ct);
        var result = await planner.GenerateAsync(orderId, req.WorkflowOverrides, ct);
        audit.Record(BusinessId, ActorId, "order.tasks_generated", nameof(Order), orderId, result);
        await TaskSupport.SaveWithOrderSyncAsync(db, orderId, ct);
        return result;
    }

    public async Task<IReadOnlyList<TaskDetailDto>> RequestAlterationsAsync(Guid garmentId, RequestAlterationsRequest req, CancellationToken ct = default)
    {
        var orderId = await db.OrderGarments.Where(g => g.Id == garmentId).Select(g => (Guid?)g.OrderId).FirstOrDefaultAsync(ct)
            ?? throw AppException.NotFound("garment_not_found", "Garment not found.");
        if (!await OrderVisibleAsync(orderId, ct)) // a garment at a hidden location looks like a missing one
            throw AppException.NotFound("garment_not_found", "Garment not found.");
        var orderStatus = await db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).FirstAsync(ct);
        if (orderStatus is not (OrderStatus.Confirmed or OrderStatus.InProduction or OrderStatus.Ready))
            throw AppException.Conflict("order_not_active", "Alterations can only be requested while the order is in production.");

        var activated = await TaskSupport.ActivateConditionalAsync(db, garmentId, req.Notes, ActorId, ct);
        audit.Record(BusinessId, ActorId, "garment.alterations_requested", nameof(OrderGarment), garmentId, new { req.Notes });
        await TaskSupport.SaveWithOrderSyncAsync(db, orderId, ct);

        var details = new List<TaskDetailDto>();
        foreach (var t in activated) details.Add(await GetAsync(t.Id, ct));
        return details;
    }

    // ------------------------------------------------------------------ helpers

    private enum OverrideAction { Start, Complete }

    private async Task EnsureTaskVisibleAsync(Guid id, CancellationToken ct)
    {
        var allowed = await AllowedLocationsAsync(ct);
        if (allowed is null) return;
        if (!await db.ProductionTasks.AnyAsync(t => t.Id == id && db.Orders.Any(o => o.Id == t.OrderId && allowed.Contains(o.LocationId)), ct))
            throw AppException.NotFound("task_not_found", "Task not found.");
    }

    private async Task<ProductionTask> LoadAsync(Guid id, CancellationToken ct)
    {
        await EnsureTaskVisibleAsync(id, ct);
        return await db.ProductionTasks.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw AppException.NotFound("task_not_found", "Task not found.");
    }

    private static void EnsureOpen(ProductionTask t)
    {
        if (t.Status is ProductionTaskStatus.Completed or ProductionTaskStatus.Cancelled)
            throw AppException.Conflict("task_closed", $"This task is {t.Status} and can no longer be changed.");
    }

    /// <summary>Overrides need the tasks.override permission AND a written reason. Both are checked server-side.</summary>
    private async Task RequireOverrideAsync(string? reason, CancellationToken ct)
    {
        var access = tenant.UserId is { } u ? await permissions.GetAccessAsync(u, BusinessId, ct) : null;
        if (access is null || !(access.IsOwner || access.Permissions.Contains(Permissions.TasksOverride)))
            throw AppException.Forbidden("override_not_permitted", "You do not have permission to override workflow rules.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw AppException.BadRequest("reason_required", "Give a reason (at least 5 characters) for the override.");
    }
}

public sealed class WorkerService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, ILocationScope locations) : IWorkerService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<IReadOnlyList<WorkerDto>> ListAsync(CancellationToken ct = default)
    {
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        var memberQuery = db.Memberships.AsNoTracking().Where(m => m.Status == MembershipStatus.Active);
        if (allowed is not null)
            memberQuery = memberQuery.Where(m => db.MembershipLocations.Any(l => l.MembershipId == m.Id && allowed.Contains(l.LocationId)));
        var members = await memberQuery.OrderBy(m => m.DisplayName).Select(m => new { m.Id, m.DisplayName }).ToListAsync(ct);
        var memberIds = members.Select(m => m.Id).ToArray();
        var skills = await db.WorkerSkills.AsNoTracking().Where(s => memberIds.Contains(s.MembershipId)).ToListAsync(ct);
        var taskQuery = db.ProductionTasks.AsNoTracking()
            .Where(t => t.AssignedMembershipId != null && memberIds.Contains(t.AssignedMembershipId.Value)
                && t.Status != ProductionTaskStatus.Completed && t.Status != ProductionTaskStatus.Cancelled);
        if (allowed is not null)
            taskQuery = taskQuery.Where(t => db.Orders.Any(o => o.Id == t.OrderId && allowed.Contains(o.LocationId)));
        var open = await taskQuery.GroupBy(t => t.AssignedMembershipId).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);

        return members.Select(m => new WorkerDto(m.Id, m.DisplayName, true,
            skills.Where(s => s.MembershipId == m.Id).Select(s => s.Skill).OrderBy(s => s).ToList(),
            open.FirstOrDefault(o => o.Id == m.Id)?.Count ?? 0)).ToList();
    }

    public async Task<WorkerDto> SetSkillsAsync(Guid membershipId, SetSkillsRequest req, CancellationToken ct = default)
    {
        await EnsureMembershipVisibleAsync(membershipId, ct);

        var wanted = req.Skills.Select(s => SkillName.Normalize(s)).Where(s => s is not null).Select(s => s!).Distinct().ToList();
        if (wanted.Any(s => s.Length > 60)) throw AppException.BadRequest("invalid_skill", "Skill names are limited to 60 characters.");

        var current = await db.WorkerSkills.Where(s => s.MembershipId == membershipId).ToListAsync(ct);
        db.WorkerSkills.RemoveRange(current.Where(s => !wanted.Contains(s.Skill)));
        foreach (var skill in wanted.Where(s => current.All(c => c.Skill != s)))
            db.WorkerSkills.Add(new WorkerSkill { BusinessId = BusinessId, MembershipId = membershipId, Skill = skill });

        audit.Record(BusinessId, tenant.UserId, "worker.skills_changed", nameof(WorkerSkill), membershipId, new { skills = wanted });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(w => w.MembershipId == membershipId);
    }

    private async Task EnsureMembershipVisibleAsync(Guid membershipId, CancellationToken ct)
    {
        var query = db.Memberships.Where(m => m.Id == membershipId && m.Status == MembershipStatus.Active);
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        if (allowed is not null)
            query = query.Where(m => db.MembershipLocations.Any(l => l.MembershipId == m.Id && allowed.Contains(l.LocationId)));
        if (!await query.AnyAsync(ct)) throw AppException.NotFound("staff_not_found", "Active staff member was not found in your permitted locations.");
    }
}
