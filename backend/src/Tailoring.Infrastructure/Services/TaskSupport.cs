using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Production;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

internal sealed record TaskRow(
    Guid Id, Guid OrderId, string OrderNumber, OrderStatus OrderStatus, Guid GarmentId, string GarmentName,
    string StageName, int StageOrder, string Title, int Quantity, ProductionTaskStatus Status, OrderPriority Priority,
    DateTime? DeadlineUtc, Guid? AssigneeId, string? AssigneeName, string? RequiredSkill, int? EstimatedMinutes,
    int UnmetDependencies, bool IsConditional, IssueType? BlockedType, string? BlockedReason, uint Version);

internal static class TaskSupport
{
    public static IQueryable<TaskRow> Rows(AppDbContext db, IQueryable<ProductionTask> q) => q.Select(t => new TaskRow(
        t.Id, t.OrderId,
        db.Orders.Where(o => o.Id == t.OrderId).Select(o => o.OrderNumber).First(),
        db.Orders.Where(o => o.Id == t.OrderId).Select(o => o.Status).First(),
        t.OrderGarmentId,
        db.OrderGarments.Where(g => g.Id == t.OrderGarmentId).Select(g => g.Name).First(),
        t.StageName, t.StageOrder, t.Title, t.Quantity, t.Status, t.Priority, t.DeadlineUtc,
        t.AssignedMembershipId,
        db.Memberships.Where(m => m.Id == t.AssignedMembershipId).Select(m => m.DisplayName).FirstOrDefault(),
        t.RequiredSkill, t.EstimatedMinutes,
        db.TaskDependencies.Count(d => d.TaskId == t.Id && db.ProductionTasks.Any(x =>
            x.Id == d.DependsOnTaskId && x.Status != ProductionTaskStatus.Completed && x.Status != ProductionTaskStatus.Cancelled)),
        t.IsConditional, t.BlockedType, t.BlockedReason, t.Version));

    public static TaskSummaryDto ToSummary(TaskRow r, DateTime nowUtc)
    {
        var eligible = r.Status is ProductionTaskStatus.Pending or ProductionTaskStatus.Scheduled
            && r.UnmetDependencies == 0
            && r.OrderStatus is OrderStatus.Confirmed or OrderStatus.InProduction;
        return new TaskSummaryDto(r.Id, r.OrderId, r.OrderNumber, r.GarmentId, r.GarmentName, r.StageName, r.StageOrder,
            r.Title, r.Quantity, r.Status.ToString(), r.Priority.ToString(), r.DeadlineUtc,
            TaskStatusRules.IsOverdue(r.Status, r.DeadlineUtc, nowUtc), r.AssigneeId, r.AssigneeName, r.RequiredSkill,
            r.EstimatedMinutes, r.EstimatedMinutes is null && TaskStatusRules.IsOpen(r.Status), r.UnmetDependencies,
            eligible, r.IsConditional, r.BlockedType?.ToString(), r.BlockedReason, r.Version);
    }

    public static async Task<TaskDetailDto> LoadDetailAsync(AppDbContext db, Guid id, bool includeCustomer, CancellationToken ct)
    {
        var row = await Rows(db, db.ProductionTasks.AsNoTracking().Where(t => t.Id == id)).FirstOrDefaultAsync(ct)
            ?? throw AppException.NotFound("task_not_found", "Task not found.");
        var t = await db.ProductionTasks.AsNoTracking().FirstAsync(x => x.Id == id, ct);

        var deps = await (from d in db.TaskDependencies.AsNoTracking()
                          where d.TaskId == id
                          join x in db.ProductionTasks.AsNoTracking() on d.DependsOnTaskId equals x.Id
                          orderby x.StageOrder
                          select new { x.Id, x.Title, x.Status }).ToListAsync(ct);

        var g = await db.OrderGarments.AsNoTracking().FirstAsync(x => x.Id == t.OrderGarmentId, ct);
        string? customer = null;
        if (includeCustomer)
            customer = await (from o in db.Orders.AsNoTracking() where o.Id == t.OrderId
                              join c in db.Customers.AsNoTracking() on o.CustomerId equals c.Id
                              select c.FullName).FirstOrDefaultAsync(ct);

        var events = await db.TaskEvents.AsNoTracking().Where(e => e.TaskId == id)
            .OrderByDescending(e => e.CreatedAtUtc).Take(100).ToListAsync(ct);

        return new TaskDetailDto(
            ToSummary(row, DateTime.UtcNow), t.Instructions, t.RequiredChecks.ToList(), t.CompletedChecks.ToList(),
            ParseMaterials(t.RequiredMaterialsJson), t.CompletionNotes, t.PlannedStartUtc, t.PlannedEndUtc,
            t.ActualStartUtc, t.CompletedAtUtc,
            deps.Select(d => new TaskDependencyDto(d.Id, d.Title, d.Status.ToString())).ToList(),
            new GarmentSpecDto(g.Name, g.Category, g.Quantity, g.Fabric, g.Colour, g.StyleInstructions, g.AlterationNotes,
                ParseJson(g.MeasurementSnapshotJson)),
            customer,
            events.Select(e => new TaskEventDto(e.Type, e.FromStatus, e.ToStatus, e.ActorUserId, e.Note, e.CreatedAtUtc)).ToList());
    }

    public static void Record(AppDbContext db, ProductionTask t, string type, ProductionTaskStatus? fromStatus,
        ProductionTaskStatus? toStatus, Guid? actor, string? note = null, object? data = null)
        => db.TaskEvents.Add(new TaskEvent
        {
            BusinessId = t.BusinessId, TaskId = t.Id, Type = type,
            FromStatus = fromStatus?.ToString(), ToStatus = toStatus?.ToString(), ActorUserId = actor, Note = note,
            DataJson = data is null ? null : JsonSerializer.Serialize(data, MeasurementJson.Options)
        });

    // ---------------------------------------------------------------- materials json

    public static string SerializeMaterials(IEnumerable<MaterialRequirementDto> items)
        => JsonSerializer.Serialize(items.Select(m => new MaterialRequirement(m.Name.Trim(), m.Quantity, m.Unit.Trim())), MeasurementJson.Options);

    public static List<MaterialRequirementDto> ParseMaterials(string json)
        => (JsonSerializer.Deserialize<List<MaterialRequirement>>(json, MeasurementJson.Options) ?? new())
            .Select(m => new MaterialRequirementDto(m.Name, m.Quantity, m.Unit)).ToList();

    private static JsonElement? ParseJson(string? json)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ---------------------------------------------------------------- order status derived from tasks

    /// <summary>
    /// Keeps the order's status honest: Confirmed -> InProduction when work starts, -> Ready when every active task is done,
    /// and back to InProduction if a task is added or reopened after Ready. Never moves an order past Ready.
    /// </summary>
    public static async Task SyncOrderAsync(AppDbContext db, Order order, CancellationToken ct)
    {
        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.InProduction or OrderStatus.Ready)) return;

        var statuses = await db.ProductionTasks.AsNoTracking()
            .Where(t => t.OrderId == order.Id && t.Status != ProductionTaskStatus.Cancelled)
            .Select(t => t.Status).ToListAsync(ct);
        if (statuses.Count == 0) return;

        var allDone = statuses.All(s => s == ProductionTaskStatus.Completed);
        var started = statuses.Any(s => s is ProductionTaskStatus.InProgress or ProductionTaskStatus.Paused or ProductionTaskStatus.Completed);

        if (allDone && order.Status != OrderStatus.Ready)
        {
            order.Status = OrderStatus.Ready;
            order.ReadyAtUtc = DateTime.UtcNow;
        }
        else if (!allDone && order.Status == OrderStatus.Ready)
        {
            order.Status = OrderStatus.InProduction;
            order.ReadyAtUtc = null;
        }
        else if (!allDone && started && order.Status == OrderStatus.Confirmed)
        {
            order.Status = OrderStatus.InProduction;
        }
    }

    /// <summary>
    /// Saves task changes, then re-derives the order status from the saved tasks, atomically.
    /// The caller must NOT already have an open transaction. A concurrent edit surfaces as 409.
    /// </summary>
    public static async Task SaveWithOrderSyncAsync(AppDbContext db, Guid orderId, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            var order = await db.Orders.FirstAsync(o => o.Id == orderId, ct);
            await SyncOrderAsync(db, order, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict("stale_version", "This task was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>
    /// Re-opens the garment's conditional steps (e.g. Alterations) that were generated as "not required".
    /// Caller saves via <see cref="SaveWithOrderSyncAsync"/>.
    /// </summary>
    public static async Task<List<ProductionTask>> ActivateConditionalAsync(
        AppDbContext db, Guid garmentId, string? notes, Guid? actor, CancellationToken ct)
    {
        var tasks = await db.ProductionTasks.Where(t => t.OrderGarmentId == garmentId && t.IsConditional
            && t.Status == ProductionTaskStatus.Cancelled && t.CancelReason == TaskStatusRules.NotRequiredReason).ToListAsync(ct);
        if (tasks.Count == 0)
            throw AppException.Conflict("no_conditional_steps",
                "This garment has no alteration step to activate (it was already requested, or its workflow has none).");

        foreach (var t in tasks)
        {
            t.Status = ProductionTaskStatus.Pending;
            t.CancelReason = null;
            t.UpdatedByUserId = actor;
            Record(db, t, TaskEventTypes.AlterationsRequested, ProductionTaskStatus.Cancelled, ProductionTaskStatus.Pending, actor, notes);
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            var garment = await db.OrderGarments.FirstAsync(g => g.Id == garmentId, ct);
            var line = $"[Alterations {DateTime.UtcNow:yyyy-MM-dd}] {notes.Trim()}";
            garment.AlterationNotes = Parse.Truncate(string.IsNullOrEmpty(garment.AlterationNotes) ? line : garment.AlterationNotes + "\n" + line, 2000);
        }
        return tasks;
    }

    public static async Task<ProductionProgressDto?> ProgressAsync(AppDbContext db, Guid orderId, CancellationToken ct)
    {
        var rows = await db.ProductionTasks.AsNoTracking()
            .Where(t => t.OrderId == orderId && t.Status != ProductionTaskStatus.Cancelled)
            .Select(t => new { t.Status, t.DeadlineUtc }).ToListAsync(ct);
        if (rows.Count == 0) return null;

        var now = DateTime.UtcNow;
        var done = rows.Count(r => r.Status == ProductionTaskStatus.Completed);
        return new ProductionProgressDto(rows.Count, done,
            rows.Count(r => r.Status == ProductionTaskStatus.InProgress),
            rows.Count(r => r.Status == ProductionTaskStatus.Blocked),
            rows.Count(r => TaskStatusRules.IsOverdue(r.Status, r.DeadlineUtc, now)),
            (int)Math.Round(100.0 * done / rows.Count));
    }
}

public sealed class ProductionPlanner(AppDbContext db, ITenantContext tenant) : IProductionPlanner
{
    public async Task<GenerateTasksResult> GenerateAsync(Guid orderId, Dictionary<Guid, Guid>? overrides, CancellationToken ct = default)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw AppException.NotFound("order_not_found", "Order not found.");
        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.InProduction))
            throw AppException.Conflict("order_not_active", "Tasks can only be generated for confirmed orders in production.");

        var zoneId = await db.Businesses.AsNoTracking().Select(b => b.TimeZoneId).FirstAsync(ct);
        var deadline = TaskPlanner.EndOfDayUtc(order.PromisedDate, TimeZoneInfo.FindSystemTimeZoneById(zoneId));

        var garments = await db.OrderGarments.Where(g => g.OrderId == orderId).OrderBy(g => g.LineNumber).ToListAsync(ct);
        var alreadyPlanned = (await db.ProductionTasks.Where(t => t.OrderId == orderId)
            .Select(t => t.OrderGarmentId).Distinct().ToListAsync(ct)).ToHashSet();

        var workflows = await db.Workflows.AsNoTracking().Include(w => w.Stages).Where(w => w.IsActive).ToListAsync(ct);
        var stageIds = workflows.SelectMany(w => w.Stages).Select(s => s.Id).ToList();
        var links = await db.StageDependencies.AsNoTracking().Where(d => stageIds.Contains(d.StageId))
            .Select(d => new StageLink(d.StageId, d.DependsOnStageId)).ToListAsync(ct);

        int created = 0, noWorkflow = 0, planned = 0;
        foreach (var garment in garments)
        {
            if (alreadyPlanned.Contains(garment.Id)) { planned++; continue; }

            ProductionWorkflow? wf;
            if (overrides is not null && overrides.TryGetValue(garment.Id, out var wanted))
                wf = workflows.FirstOrDefault(w => w.Id == wanted)
                     ?? throw AppException.BadRequest("unknown_workflow", "A chosen workflow does not exist or is inactive.");
            else
                wf = workflows.FirstOrDefault(w => garment.Category != null
                         && w.GarmentCategories.Contains(garment.Category, StringComparer.OrdinalIgnoreCase))
                     ?? workflows.FirstOrDefault(w => w.IsDefault);

            if (wf is null) { noWorkflow++; continue; }

            var plan = TaskPlanner.Plan(order, garment, wf, links, deadline, tenant.UserId);
            db.ProductionTasks.AddRange(plan.Tasks);
            db.TaskDependencies.AddRange(plan.Dependencies);
            foreach (var t in plan.Tasks)
                TaskSupport.Record(db, t, TaskEventTypes.Generated, null, t.Status, tenant.UserId, data: new { workflow = wf.Name });
            created += plan.Tasks.Count;
        }
        return new GenerateTasksResult(created, noWorkflow, planned);
    }

    public async Task CancelOpenTasksAsync(Guid orderId, string reason, CancellationToken ct = default)
    {
        var open = await db.ProductionTasks.Where(t => t.OrderId == orderId
            && t.Status != ProductionTaskStatus.Completed && t.Status != ProductionTaskStatus.Cancelled).ToListAsync(ct);
        foreach (var t in open)
        {
            var fromStatus = t.Status;
            t.Status = ProductionTaskStatus.Cancelled;
            t.CancelReason = reason;
            t.UpdatedByUserId = tenant.UserId;
            TaskSupport.Record(db, t, TaskEventTypes.Cancelled, fromStatus, t.Status, tenant.UserId, reason);
        }
    }
}
