using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Production;
using Tailoring.Domain.Production;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class WorkflowService(AppDbContext db, ITenantContext tenant, IAuditLogger audit) : IWorkflowService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<IReadOnlyList<WorkflowDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var q = db.Workflows.AsNoTracking().Include(w => w.Stages).AsQueryable();
        if (!includeInactive) q = q.Where(w => w.IsActive);
        var workflows = await q.OrderBy(w => w.Name).ToListAsync(ct);
        return await ToDtosAsync(workflows, ct);
    }

    public async Task<WorkflowDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var wf = await db.Workflows.AsNoTracking().Include(w => w.Stages).FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw AppException.NotFound("workflow_not_found", "Workflow not found.");
        return (await ToDtosAsync([wf], ct)).Single();
    }

    public async Task<WorkflowDto> CreateAsync(UpsertWorkflowRequest req, CancellationToken ct = default)
    {
        var resolved = Validate(req);
        var name = req.Name.Trim();
        if (await db.Workflows.AnyAsync(w => w.Name == name, ct))
            throw AppException.Conflict("workflow_exists", "A workflow with this name already exists.");

        var makeDefault = req.IsDefault || !await db.Workflows.AnyAsync(ct);
        if (makeDefault && !req.IsActive)
            throw AppException.BadRequest("default_must_be_active", "The default workflow must be active.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (makeDefault)
            await db.Workflows.Where(w => w.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsDefault, false), ct);

        var wf = new ProductionWorkflow
        {
            BusinessId = BusinessId, Name = name, Description = Parse.Clean(req.Description),
            IsDefault = makeDefault, IsActive = req.IsActive, GarmentCategories = NormalizeCategories(req.GarmentCategories)
        };
        for (var i = 0; i < req.Stages.Count; i++) wf.Stages.Add(NewStage(wf, req.Stages[i], i));
        db.Workflows.Add(wf);
        db.StageDependencies.AddRange(DesiredLinks(wf.Stages, resolved).Select(l => ToLink(l.StageId, l.DependsOnStageId)));

        audit.Record(BusinessId, tenant.UserId, "workflow.created", nameof(ProductionWorkflow), wf.Id, new { name });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(wf.Id, ct);
    }

    public async Task<WorkflowDto> UpdateAsync(Guid id, UpsertWorkflowRequest req, CancellationToken ct = default)
    {
        var resolved = Validate(req);
        var wf = await db.Workflows.Include(w => w.Stages).FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw AppException.NotFound("workflow_not_found", "Workflow not found.");

        var name = req.Name.Trim();
        if (await db.Workflows.AnyAsync(w => w.Name == name && w.Id != id, ct))
            throw AppException.Conflict("workflow_exists", "A workflow with this name already exists.");
        if (wf.IsDefault && !req.IsDefault)
            throw AppException.BadRequest("default_required", "Make another workflow the default instead of unsetting this one.");
        if ((wf.IsDefault || req.IsDefault) && !req.IsActive)
            throw AppException.BadRequest("default_must_be_active", "The default workflow must be active.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (req.IsDefault && !wf.IsDefault)
        {
            await db.Workflows.Where(w => w.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsDefault, false), ct);
            wf.IsDefault = true;
        }

        wf.Name = name;
        wf.Description = Parse.Clean(req.Description);
        wf.IsActive = req.IsActive;
        wf.GarmentCategories = NormalizeCategories(req.GarmentCategories);

        var existingIds = wf.Stages.Select(s => s.Id).ToList();
        var existingDeps = await db.StageDependencies.Where(d => existingIds.Contains(d.StageId)).ToListAsync(ct);

        // Stages are matched by Key so ids stay stable across edits.
        var incomingKeys = req.Stages.Select(s => s.Key).ToHashSet();
        foreach (var gone in wf.Stages.Where(s => !incomingKeys.Contains(s.Key)).ToList())
            db.Stages.Remove(gone);   // dependency rows touching it are cascade-deleted

        for (var i = 0; i < req.Stages.Count; i++)
        {
            var input = req.Stages[i];
            var stage = wf.Stages.FirstOrDefault(s => s.Key == input.Key);
            if (stage is null) wf.Stages.Add(NewStage(wf, input, i));
            else ApplyStage(stage, input, i);
        }

        var kept = wf.Stages.Where(s => incomingKeys.Contains(s.Key)).ToList();
        var keptIds = kept.Select(s => s.Id).ToHashSet();
        var current = existingDeps.Where(d => keptIds.Contains(d.StageId) && keptIds.Contains(d.DependsOnStageId)).ToList();
        var desired = DesiredLinks(kept, resolved).ToHashSet();

        foreach (var extra in current.Where(d => !desired.Contains((d.StageId, d.DependsOnStageId))))
            db.StageDependencies.Remove(extra);
        foreach (var missing in desired.Where(l => current.All(d => (d.StageId, d.DependsOnStageId) != l)))
            db.StageDependencies.Add(ToLink(missing.StageId, missing.DependsOnStageId));

        audit.Record(BusinessId, tenant.UserId, "workflow.updated", nameof(ProductionWorkflow), wf.Id, new { name });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Validates the request and returns each stage's resolved prerequisite keys.</summary>
    private static Dictionary<string, List<string>> Validate(UpsertWorkflowRequest req)
    {
        var problems = new List<string>();
        var keys = req.Stages.Select(s => s.Key).ToList();
        foreach (var dup in keys.GroupBy(k => k).Where(g => g.Count() > 1)) problems.Add($"Duplicate stage key '{dup.Key}'.");

        var resolved = new Dictionary<string, List<string>>();
        for (var i = 0; i < req.Stages.Count; i++)
        {
            var s = req.Stages[i];
            var fallback = i > 0 ? new List<string> { req.Stages[i - 1].Key } : new List<string>();
            var deps = (s.DependsOn ?? fallback).Distinct().ToList();
            foreach (var d in deps)
            {
                if (d == s.Key) problems.Add($"'{s.Key}' cannot depend on itself.");
                else if (!keys.Contains(d)) problems.Add($"'{s.Key}' depends on unknown stage '{d}'.");
            }
            if (s.IsConditional && deps.Count == 0) problems.Add($"Conditional stage '{s.Key}' needs at least one prerequisite.");
            if (s.CompletionChecks.Any(c => string.IsNullOrWhiteSpace(c) || c.Length > 200))
                problems.Add($"'{s.Key}': checklist items must be 1-200 characters.");
            resolved[s.Key] = deps;
        }

        if (req.Stages.All(s => s.IsConditional)) problems.Add("At least one stage must be non-conditional.");
        if (problems.Count == 0 && DependencyGraph.HasCycle(keys, resolved.SelectMany(kv => kv.Value.Select(d => (d, kv.Key)))))
            problems.Add("The stage dependencies contain a cycle.");

        if (problems.Count > 0) throw AppException.BadRequest("invalid_workflow", string.Join(" ", problems));
        return resolved;
    }

    private static IEnumerable<(Guid StageId, Guid DependsOnStageId)> DesiredLinks(
        IEnumerable<ProductionStage> stages, Dictionary<string, List<string>> resolved)
    {
        var list = stages.ToList();
        var byKey = list.ToDictionary(s => s.Key, s => s.Id);
        foreach (var stage in list)
            if (resolved.TryGetValue(stage.Key, out var deps))
                foreach (var d in deps) yield return (stage.Id, byKey[d]);
    }

    private StageDependency ToLink(Guid stageId, Guid dependsOn)
        => new() { BusinessId = BusinessId, StageId = stageId, DependsOnStageId = dependsOn };

    private ProductionStage NewStage(ProductionWorkflow wf, StageInput i, int order)
    {
        var s = new ProductionStage { BusinessId = BusinessId, WorkflowId = wf.Id, Key = i.Key };
        ApplyStage(s, i, order);
        return s;
    }

    private static void ApplyStage(ProductionStage s, StageInput i, int order)
    {
        s.Name = i.Name.Trim();
        s.SortOrder = order;
        s.Instructions = Parse.Clean(i.Instructions);
        s.RequiredSkill = SkillName.Normalize(i.RequiredSkill);
        s.DefaultMinutes = i.DefaultMinutes;
        s.IsConditional = i.IsConditional;
        s.CompletionChecks = i.CompletionChecks.Select(c => c.Trim()).ToList();
    }

    private static List<string> NormalizeCategories(IEnumerable<string> input)
        => input.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private async Task<IReadOnlyList<WorkflowDto>> ToDtosAsync(IReadOnlyList<ProductionWorkflow> workflows, CancellationToken ct)
    {
        var stageIds = workflows.SelectMany(w => w.Stages).Select(s => s.Id).ToList();
        var links = await db.StageDependencies.AsNoTracking().Where(d => stageIds.Contains(d.StageId)).ToListAsync(ct);

        return workflows.Select(w =>
        {
            var keyById = w.Stages.ToDictionary(s => s.Id, s => s.Key);
            var stages = w.Stages.OrderBy(s => s.SortOrder).Select(s => new WorkflowStageDto(
                s.Id, s.Key, s.Name, s.SortOrder, s.Instructions, s.RequiredSkill, s.DefaultMinutes, s.IsConditional,
                s.CompletionChecks.ToList(),
                links.Where(l => l.StageId == s.Id && keyById.ContainsKey(l.DependsOnStageId))
                     .Select(l => keyById[l.DependsOnStageId]).OrderBy(k => k).ToList())).ToList();
            return new WorkflowDto(w.Id, w.Name, w.Description, w.IsDefault, w.IsActive, w.GarmentCategories.ToList(), stages);
        }).ToList();
    }
}

internal static class SkillName
{
    public static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToLowerInvariant();
}
