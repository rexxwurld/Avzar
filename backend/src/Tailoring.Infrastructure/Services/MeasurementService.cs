using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Measurements;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Measurements;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

internal static class MeasurementJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize(IEnumerable<MeasurementValue> values) => JsonSerializer.Serialize(values, Options);
    public static List<MeasurementValue> Deserialize(string json)
        => JsonSerializer.Deserialize<List<MeasurementValue>>(json, Options) ?? new();
}

public sealed class MeasurementService(AppDbContext db, ITenantContext tenant, IAuditLogger audit) : IMeasurementService
{
    private static readonly string[] Units = ["cm", "mm", "in", "kg", "lb"];
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    // ------------------------------------------------------------------ templates

    public async Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken ct = default)
    {
        var q = db.MeasurementTemplates.AsNoTracking().Include(t => t.Fields).AsQueryable();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        return (await q.OrderBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<TemplateDto> GetTemplateAsync(Guid id, CancellationToken ct = default)
        => ToDto(await db.MeasurementTemplates.AsNoTracking().Include(t => t.Fields).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw AppException.NotFound("template_not_found", "Measurement template not found."));

    public async Task<TemplateDto> CreateTemplateAsync(UpsertTemplateRequest req, CancellationToken ct = default)
    {
        ValidateFields(req.Fields);
        var name = req.Name.Trim();
        if (await db.MeasurementTemplates.AnyAsync(t => t.Name == name, ct))
            throw AppException.Conflict("template_exists", "A template with this name already exists.");

        var template = new MeasurementTemplate
        {
            BusinessId = BusinessId, Name = name, Description = Parse.Clean(req.Description), IsActive = req.IsActive
        };
        template.Fields = req.Fields.Select((f, i) => NewField(template, f, i)).ToList();
        db.MeasurementTemplates.Add(template);
        audit.Record(BusinessId, tenant.UserId, "measurement_template.created", nameof(MeasurementTemplate), template.Id, new { name });
        await db.SaveChangesAsync(ct);
        return ToDto(template);
    }

    public async Task<TemplateDto> UpdateTemplateAsync(Guid id, UpsertTemplateRequest req, CancellationToken ct = default)
    {
        ValidateFields(req.Fields);
        var template = await db.MeasurementTemplates.Include(t => t.Fields).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw AppException.NotFound("template_not_found", "Measurement template not found.");

        var name = req.Name.Trim();
        if (await db.MeasurementTemplates.AnyAsync(t => t.Name == name && t.Id != id, ct))
            throw AppException.Conflict("template_exists", "A template with this name already exists.");

        template.Name = name;
        template.Description = Parse.Clean(req.Description);
        template.IsActive = req.IsActive;

        // Diff by key (keeps ids stable and avoids delete/insert unique-index clashes).
        var incomingKeys = req.Fields.Select(f => f.Key).ToHashSet();
        foreach (var gone in template.Fields.Where(f => !incomingKeys.Contains(f.Key)).ToList())
            db.MeasurementTemplateFields.Remove(gone);

        for (var i = 0; i < req.Fields.Count; i++)
        {
            var input = req.Fields[i];
            var existing = template.Fields.FirstOrDefault(f => f.Key == input.Key);
            if (existing is null) db.MeasurementTemplateFields.Add(NewField(template, input, i));
            else
            {
                existing.Label = input.Label.Trim(); existing.Unit = input.Unit; existing.MinValue = input.MinValue;
                existing.MaxValue = input.MaxValue; existing.IsRequired = input.IsRequired; existing.SortOrder = i;
            }
        }

        audit.Record(BusinessId, tenant.UserId, "measurement_template.updated", nameof(MeasurementTemplate), template.Id, new { name });
        await db.SaveChangesAsync(ct);
        return ToDto((await db.MeasurementTemplates.AsNoTracking().Include(t => t.Fields).FirstAsync(t => t.Id == id, ct)));
    }

    // ------------------------------------------------------------------ customer measurement sets

    public async Task<IReadOnlyList<MeasurementSetDto>> ListSetsAsync(Guid customerId, CancellationToken ct = default)
    {
        await EnsureCustomerAsync(customerId, requireActive: false, ct);

        var sets = await db.MeasurementSets.AsNoTracking().Where(s => s.CustomerId == customerId)
            .Join(db.MeasurementTemplates, s => s.TemplateId, t => t.Id, (s, t) => new { Set = s, TemplateName = t.Name })
            .OrderBy(x => x.Set.Label).ToListAsync(ct);
        if (sets.Count == 0) return [];

        var ids = sets.Select(x => x.Set.Id).ToList();
        var revisions = await db.MeasurementRevisions.AsNoTracking().Where(r => ids.Contains(r.SetId)).ToListAsync(ct);

        return sets.Select(x =>
        {
            var current = revisions.First(r => r.SetId == x.Set.Id && r.RevisionNumber == x.Set.CurrentRevision);
            return ToDto(x.Set, x.TemplateName, current);
        }).ToList();
    }

    public async Task<MeasurementSetDto> CreateSetAsync(Guid customerId, CreateMeasurementSetRequest req, CancellationToken ct = default)
    {
        await EnsureCustomerAsync(customerId, requireActive: true, ct);
        var template = await db.MeasurementTemplates.AsNoTracking().Include(t => t.Fields)
            .FirstOrDefaultAsync(t => t.Id == req.TemplateId, ct)
            ?? throw AppException.BadRequest("unknown_template", "Measurement template not found.");
        if (!template.IsActive) throw AppException.Conflict("template_inactive", "This template is inactive.");

        var label = req.Label.Trim();
        if (await db.MeasurementSets.AnyAsync(s => s.CustomerId == customerId && s.Label == label, ct))
            throw AppException.Conflict("label_exists", "This customer already has a measurement profile with that label.");

        var values = ValidateValues(template.Fields, req.Values);
        var set = new CustomerMeasurementSet
        {
            BusinessId = BusinessId, CustomerId = customerId, TemplateId = template.Id, Label = label, CurrentRevision = 1
        };
        var revision = new MeasurementRevision
        {
            BusinessId = BusinessId, SetId = set.Id, RevisionNumber = 1,
            ValuesJson = MeasurementJson.Serialize(values), Notes = Parse.Clean(req.Notes), RecordedByUserId = tenant.UserId
        };
        db.MeasurementSets.Add(set);
        db.MeasurementRevisions.Add(revision);
        audit.Record(BusinessId, tenant.UserId, "measurements.recorded", nameof(CustomerMeasurementSet), set.Id, new { customerId, template = template.Name });
        await SaveOrConflictAsync(ct);
        return ToDto(set, template.Name, revision);
    }

    public async Task<MeasurementSetDto> AddRevisionAsync(Guid setId, AddRevisionRequest req, CancellationToken ct = default)
    {
        var set = await db.MeasurementSets.FirstOrDefaultAsync(s => s.Id == setId, ct)
            ?? throw AppException.NotFound("measurement_set_not_found", "Measurement profile not found.");
        await EnsureCustomerAsync(set.CustomerId, requireActive: true, ct);

        var template = await db.MeasurementTemplates.AsNoTracking().Include(t => t.Fields).FirstAsync(t => t.Id == set.TemplateId, ct);
        var values = ValidateValues(template.Fields, req.Values);

        set.CurrentRevision += 1;
        var revision = new MeasurementRevision
        {
            BusinessId = BusinessId, SetId = set.Id, RevisionNumber = set.CurrentRevision,
            ValuesJson = MeasurementJson.Serialize(values), Notes = Parse.Clean(req.Notes), RecordedByUserId = tenant.UserId
        };
        db.MeasurementRevisions.Add(revision);
        audit.Record(BusinessId, tenant.UserId, "measurements.revised", nameof(CustomerMeasurementSet), set.Id, new { revision = set.CurrentRevision });
        await SaveOrConflictAsync(ct);
        return ToDto(set, template.Name, revision);
    }

    public async Task<IReadOnlyList<RevisionDto>> HistoryAsync(Guid setId, CancellationToken ct = default)
    {
        await RequireSetAsync(setId, ct);
        var rows = await db.MeasurementRevisions.AsNoTracking().Where(r => r.SetId == setId)
            .OrderByDescending(r => r.RevisionNumber).ToListAsync(ct);
        return rows.Select(r => new RevisionDto(r.RevisionNumber, ToValues(r.ValuesJson), r.Notes, r.RecordedByUserId, r.CreatedAtUtc)).ToList();
    }

    public async Task<IReadOnlyList<MeasurementDiffDto>> CompareAsync(Guid setId, int fromRevision, int toRevision, CancellationToken ct = default)
    {
        await RequireSetAsync(setId, ct);
        var rows = await db.MeasurementRevisions.AsNoTracking()
            .Where(r => r.SetId == setId && (r.RevisionNumber == fromRevision || r.RevisionNumber == toRevision)).ToListAsync(ct);
        var fromRev = rows.FirstOrDefault(r => r.RevisionNumber == fromRevision);
        var toRev = rows.FirstOrDefault(r => r.RevisionNumber == toRevision);
        if (fromRev is null || toRev is null)
            throw AppException.NotFound("revision_not_found", "One of the requested revisions does not exist.");

        var a = MeasurementJson.Deserialize(fromRev.ValuesJson).ToDictionary(v => v.Key);
        var b = MeasurementJson.Deserialize(toRev.ValuesJson).ToDictionary(v => v.Key);
        var keys = b.Keys.Concat(a.Keys.Where(k => !b.ContainsKey(k))).ToList();

        return keys.Select(k =>
        {
            var meta = b.TryGetValue(k, out var bv) ? bv : a[k];
            decimal? f = a.TryGetValue(k, out var av) ? av.Value : null;
            decimal? t = b.TryGetValue(k, out var bv2) ? bv2.Value : null;
            return new MeasurementDiffDto(k, meta.Label, meta.Unit, f, t, f is not null && t is not null ? t - f : null);
        }).ToList();
    }

    // ------------------------------------------------------------------ helpers

    private async Task EnsureCustomerAsync(Guid customerId, bool requireActive, CancellationToken ct)
    {
        var status = await db.Customers.AsNoTracking().Where(c => c.Id == customerId)
            .Select(c => (CustomerStatus?)c.Status).FirstOrDefaultAsync(ct)
            ?? throw AppException.NotFound("customer_not_found", "Customer not found.");
        if (status == CustomerStatus.Merged || (requireActive && status != CustomerStatus.Active))
            throw AppException.Conflict("customer_not_active", "This customer profile is not active.");
    }

    private async Task RequireSetAsync(Guid setId, CancellationToken ct)
    {
        if (!await db.MeasurementSets.AnyAsync(s => s.Id == setId, ct))
            throw AppException.NotFound("measurement_set_not_found", "Measurement profile not found.");
    }

    private async Task SaveOrConflictAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            throw AppException.Conflict("concurrent_edit", "These measurements were changed at the same time by someone else. Reload and try again.");
        }
    }

    private static void ValidateFields(List<TemplateFieldInput> fields)
    {
        var problems = new List<string>();
        foreach (var dup in fields.GroupBy(f => f.Key).Where(g => g.Count() > 1))
            problems.Add($"Duplicate field key '{dup.Key}'.");
        foreach (var f in fields)
        {
            if (!Units.Contains(f.Unit)) problems.Add($"'{f.Key}': unit must be one of {string.Join(", ", Units)}.");
            if (f.MinValue is < 0) problems.Add($"'{f.Key}': minimum cannot be negative.");
            if (f.MinValue is { } min && f.MaxValue is { } max && min > max) problems.Add($"'{f.Key}': minimum exceeds maximum.");
        }
        if (problems.Count > 0) throw AppException.BadRequest("invalid_template", string.Join(" ", problems));
    }

    /// <summary>Validates against the template and returns values in template order, each carrying its label and unit.</summary>
    internal static List<MeasurementValue> ValidateValues(IEnumerable<MeasurementTemplateField> fields, Dictionary<string, decimal> input)
    {
        var fieldList = fields.OrderBy(f => f.SortOrder).ToList();
        var problems = new List<string>();

        var unknown = input.Keys.Where(k => fieldList.All(f => f.Key != k)).ToList();
        if (unknown.Count > 0) problems.Add($"Unknown measurement(s): {string.Join(", ", unknown)}.");

        var result = new List<MeasurementValue>();
        foreach (var f in fieldList)
        {
            if (!input.TryGetValue(f.Key, out var value))
            {
                if (f.IsRequired) problems.Add($"{f.Label} is required.");
                continue;
            }
            if (decimal.Round(value, 2) != value) problems.Add($"{f.Label}: at most 2 decimal places.");
            else if (f.MinValue is { } min && value < min) problems.Add($"{f.Label}: must be at least {min}.");
            else if (f.MaxValue is { } max && value > max) problems.Add($"{f.Label}: must be at most {max}.");
            else result.Add(new MeasurementValue(f.Key, f.Label, f.Unit, value));
        }

        if (problems.Count > 0) throw AppException.BadRequest("invalid_measurements", string.Join(" ", problems));
        return result;
    }

    private MeasurementTemplateField NewField(MeasurementTemplate t, TemplateFieldInput f, int order) => new()
    {
        BusinessId = BusinessId, TemplateId = t.Id, Key = f.Key, Label = f.Label.Trim(), Unit = f.Unit,
        MinValue = f.MinValue, MaxValue = f.MaxValue, IsRequired = f.IsRequired, SortOrder = order
    };

    private static TemplateDto ToDto(MeasurementTemplate t) => new(t.Id, t.Name, t.Description, t.IsActive,
        t.Fields.OrderBy(f => f.SortOrder).Select(f => new TemplateFieldDto(f.Key, f.Label, f.Unit, f.MinValue, f.MaxValue, f.IsRequired, f.SortOrder)).ToList());

    private static List<MeasurementValueDto> ToValues(string json)
        => MeasurementJson.Deserialize(json).Select(v => new MeasurementValueDto(v.Key, v.Label, v.Unit, v.Value)).ToList();

    private static MeasurementSetDto ToDto(CustomerMeasurementSet s, string templateName, MeasurementRevision current)
        => new(s.Id, s.CustomerId, s.TemplateId, templateName, s.Label, s.CurrentRevision, ToValues(current.ValuesJson),
               s.UpdatedAtUtc ?? s.CreatedAtUtc);
}
