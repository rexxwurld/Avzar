using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tailoring.Application.ImportExport;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Locations;
using Tailoring.Domain.Inventory;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed record MaterialDto(Guid Id, Guid? LocationId, string Sku, string Name, string Unit, decimal OnHand, decimal Reserved, decimal Available, decimal ReorderLevel, bool LowStock, uint Version);
public sealed record MaterialCreateRequest(string Sku, string Name, string Unit, Guid? LocationId, decimal OpeningStock, decimal ReorderLevel, string? Notes);
public sealed record StockChangeRequest(decimal Quantity, string? Note, Guid? OrderId, Guid? TaskId, uint? ExpectedVersion);
public sealed record TaskMaterialQuantityRequest(Guid MaterialId, decimal Quantity);
public sealed record TaskMaterialBatchRequest(IReadOnlyList<TaskMaterialQuantityRequest> Materials, string? Note);
public sealed record StockMovementDto(Guid Id, Guid MaterialId, string Kind, decimal Quantity, decimal OnHandAfter, decimal ReservedAfter, Guid? OrderId, Guid? TaskId, string? Note, DateTime CreatedAtUtc);
public sealed record InventoryImportRowDto(int Row, string Outcome, string? Message, string? Sku, string? Name);
public sealed record InventoryImportPreviewDto(string FileHash, int TotalRows, int ValidRows, int DuplicateRows, int InvalidRows, IReadOnlyList<InventoryImportRowDto> Problems);
public sealed record InventoryImportResultDto(int Created, int Skipped, int Failed, IReadOnlyList<InventoryImportRowDto> Rows);
public sealed record InventoryImportCommitRequest(string ExpectedHash, bool SkipInvalidRows);
public interface IInventoryService
{
    Task<IReadOnlyList<MaterialDto>> ListAsync(CancellationToken ct);
    Task<InventoryImportPreviewDto> PreviewImportAsync(string csv, CancellationToken ct);
    Task<InventoryImportResultDto> CommitImportAsync(string csv, InventoryImportCommitRequest request, CancellationToken ct);
    Task<MaterialDto> CreateAsync(MaterialCreateRequest request, CancellationToken ct);
    Task<MaterialDto> ChangeAsync(Guid id, string action, StockChangeRequest request, CancellationToken ct);
    Task<IReadOnlyList<MaterialDto>> ReserveTaskAsync(Guid taskId, TaskMaterialBatchRequest request, CancellationToken ct);
    Task<IReadOnlyList<MaterialDto>> ConsumeTaskAsync(Guid taskId, TaskMaterialBatchRequest request, CancellationToken ct);
    Task<IReadOnlyList<StockMovementDto>> HistoryAsync(Guid id, int take, CancellationToken ct);
}

public sealed class InventoryService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, Tailoring.Application.Billing.IEntitlementService entitlements, ILocationScope locations) : IInventoryService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("business_required", "Select a business first.");
    public async Task<IReadOnlyList<MaterialDto>> ListAsync(CancellationToken ct)
    {
        await entitlements.RequireTierAsync(BusinessId, Tailoring.Domain.Billing.EntitlementKeys.InventoryLevel, "basic", ct);
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        var query = db.Materials.AsNoTracking().AsQueryable();
        if (allowed is not null) query = query.Where(x => x.LocationId.HasValue && allowed.Contains(x.LocationId.Value));
        return await query.OrderBy(x => x.Name).Select(x => new MaterialDto(x.Id,x.LocationId,x.Sku,x.Name,x.Unit,x.OnHand,x.Reserved,x.OnHand-x.Reserved,x.ReorderLevel,x.OnHand-x.Reserved<=x.ReorderLevel,x.Version)).ToListAsync(ct);
    }

    private sealed record ParsedInventoryRow(int Row, MaterialCreateRequest? Request, string? Error, bool Duplicate);

    public async Task<InventoryImportPreviewDto> PreviewImportAsync(string csv, CancellationToken ct)
    {
        await entitlements.RequireTierAsync(BusinessId, Tailoring.Domain.Billing.EntitlementKeys.InventoryLevel, "basic", ct);
        var analysis = await AnalyzeCsvAsync(csv, ct);
        var rows = analysis.Select(x => new InventoryImportRowDto(x.Row, x.Error is not null ? "Invalid" : x.Duplicate ? "Duplicate" : "Create", x.Error ?? (x.Duplicate ? "SKU already exists or repeats in this file." : null), x.Request?.Sku, x.Request?.Name)).ToList();
        return new(HashCsv(csv), analysis.Count, rows.Count(x => x.Outcome == "Create"), rows.Count(x => x.Outcome == "Duplicate"), rows.Count(x => x.Outcome == "Invalid"), rows.Where(x => x.Outcome != "Create").Take(300).ToList());
    }

    public async Task<InventoryImportResultDto> CommitImportAsync(string csv, InventoryImportCommitRequest request, CancellationToken ct)
    {
        await entitlements.RequireTierAsync(BusinessId, Tailoring.Domain.Billing.EntitlementKeys.InventoryLevel, "basic", ct);
        if (string.IsNullOrWhiteSpace(request.ExpectedHash) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashCsv(csv)), Encoding.ASCII.GetBytes(request.ExpectedHash.ToUpperInvariant())))
            throw AppException.Conflict("file_changed", "The CSV differs from the preview. Preview the current file again.");
        var analysis = await AnalyzeCsvAsync(csv, ct);
        if (!request.SkipInvalidRows && analysis.Any(x => x.Error is not null)) throw AppException.Conflict("invalid_rows", "The file has invalid rows. Fix them or explicitly choose to skip invalid rows.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var created = 0; var skipped = 0; var failed = 0; var resultRows = new List<InventoryImportRowDto>();
        foreach (var row in analysis)
        {
            if (row.Error is not null) { failed++; resultRows.Add(new(row.Row,"Invalid",row.Error,row.Request?.Sku,row.Request?.Name)); continue; }
            if (row.Duplicate) { skipped++; resultRows.Add(new(row.Row,"Skipped","SKU already exists or repeats in this file.",row.Request!.Sku,row.Request.Name)); continue; }
            var r = row.Request!; var material = new MaterialStock { BusinessId = BusinessId, LocationId = r.LocationId, Sku = r.Sku.Trim().ToUpperInvariant(), Name = r.Name.Trim(), Unit = r.Unit.Trim(), OnHand = r.OpeningStock, ReorderLevel = r.ReorderLevel, Notes = r.Notes?.Trim() };
            db.Materials.Add(material);
            if (r.OpeningStock > 0) db.StockMovements.Add(new StockMovement { BusinessId = BusinessId, MaterialId = material.Id, Kind = StockMovementKind.Receive, Quantity = r.OpeningStock, OnHandAfter = r.OpeningStock, ActorUserId = tenant.UserId, Note = "CSV opening stock import" });
            audit.Record(BusinessId, tenant.UserId, "inventory.material_imported", "MaterialStock", material.Id, new { material.Sku, material.Name, row.Row });
            created++; resultRows.Add(new(row.Row,"Created",null,material.Sku,material.Name));
        }
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505") { throw AppException.Conflict("import_conflict", "Stock changed during import or a SKU was added concurrently. Preview the file again and retry."); }
        return new(created, skipped, failed, resultRows.Take(1000).ToList());
    }

    private async Task<List<ParsedInventoryRow>> AnalyzeCsvAsync(string csv, CancellationToken ct)
    {
        if (csv.Length > 5 * 1024 * 1024) throw AppException.BadRequest("file_too_large", "CSV must be under 5 MB.");
        var table = CsvReader.Parse(csv, 10000);
        if (table.Count < 2) throw AppException.BadRequest("csv_empty", "CSV needs a header and at least one material row.");
        var headers = table[0].Select((value,index)=>(Key:NormalizeHeader(value),Index:index)).Where(x=>x.Key.Length>0).GroupBy(x=>x.Key).ToDictionary(g=>g.Key,g=>g.First().Index);
        foreach (var required in new[]{"sku","name","unit","openingstock","reorderlevel"}) if (!headers.ContainsKey(required)) throw AppException.BadRequest("csv_missing_column", $"CSV is missing the required '{required}' column.");
        string Cell(string[] row,string key)
        {
            if(!headers.TryGetValue(key,out var index)||index>=row.Length)return "";
            var value=row[index].Trim();
            if(value.Length>1 && value[0]=='\'' && "=+-@\t\r".Contains(value[1]))value=value[1..];
            return value;
        }
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        var knownLocations = (await db.Locations.Select(x => x.Id).ToListAsync(ct)).ToHashSet();
        var parsed = new List<ParsedInventoryRow>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(var i=1;i<table.Count;i++)
        {
            var row=table[i]; var sku=Cell(row,"sku").ToUpperInvariant(); var name=Cell(row,"name"); var unit=Cell(row,"unit"); var openingText=Cell(row,"openingstock"); var reorderText=Cell(row,"reorderlevel"); var locationText=Cell(row,"locationid");
            string? error=null; decimal opening=0,reorder=0; Guid? locationId=null;
            if(sku.Length is <1 or >64 || name.Length is <1 or >160 || unit.Length is <1 or >24) error="SKU, name and unit are required and must fit their field limits.";
            else if(!decimal.TryParse(openingText,NumberStyles.Number,CultureInfo.InvariantCulture,out opening)||opening<0) error="Opening stock must be a non-negative decimal using a dot for decimals.";
            else if(!decimal.TryParse(reorderText,NumberStyles.Number,CultureInfo.InvariantCulture,out reorder)||reorder<0) error="Reorder level must be a non-negative decimal using a dot for decimals.";
            else if(locationText.Length>0 && !Guid.TryParse(locationText,out _)) error="Location ID must be a valid GUID.";
            if(error is null && locationText.Length>0) locationId=Guid.Parse(locationText);
            if(error is null && allowed is not null && (!locationId.HasValue || !allowed.Contains(locationId.Value))) error="A permitted location ID is required for this user.";
            if(error is null && locationId.HasValue && !knownLocations.Contains(locationId.Value)) error="Location does not belong to this business.";
            var duplicate=error is null && !seen.Add(sku);
            var request=error is null?new MaterialCreateRequest(sku,name,unit,locationId,opening,reorder,null):null;
            parsed.Add(new ParsedInventoryRow(i+1,request,error,duplicate));
        }
        var skus=parsed.Where(x=>x.Request is not null).Select(x=>x.Request!.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing=await db.Materials.Where(x=>skus.Contains(x.Sku)).Select(x=>x.Sku).ToListAsync(ct); var set=existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return parsed.Select(x=>x.Request is not null && set.Contains(x.Request.Sku)?x with{Duplicate=true}:x).ToList();
    }
    private static string NormalizeHeader(string value)=>new(value.Trim().Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string HashCsv(string csv)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv)));

    public async Task<MaterialDto> CreateAsync(MaterialCreateRequest r, CancellationToken ct)
    {
        await entitlements.RequireTierAsync(BusinessId, Tailoring.Domain.Billing.EntitlementKeys.InventoryLevel, "basic", ct);
        if (string.IsNullOrWhiteSpace(r.Sku) || string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Unit) || r.OpeningStock < 0 || r.ReorderLevel < 0) throw AppException.BadRequest("invalid_material", "SKU, name, unit and non-negative quantities are required.");
        var sku = r.Sku.Trim().ToUpperInvariant();
        if (await db.Materials.AnyAsync(x => x.Sku == sku, ct)) throw AppException.Conflict("duplicate_sku", "A material with this SKU already exists.");
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        if (allowed is not null && (!r.LocationId.HasValue || !allowed.Contains(r.LocationId.Value))) throw AppException.Forbidden("location_forbidden", "A permitted location must be selected for this material.");
        if (r.LocationId.HasValue) await locations.EnsureAllowedAsync(r.LocationId.Value, ct);
        if (r.LocationId.HasValue && !await db.Locations.AnyAsync(x => x.Id == r.LocationId, ct)) throw AppException.BadRequest("invalid_location", "Location does not belong to this business.");
        var m = new MaterialStock { BusinessId = BusinessId, LocationId = r.LocationId, Sku = sku, Name = r.Name.Trim(), Unit = r.Unit.Trim(), OnHand = r.OpeningStock, ReorderLevel = r.ReorderLevel, Notes = r.Notes?.Trim() };
        db.Materials.Add(m);
        if (r.OpeningStock > 0) db.StockMovements.Add(new StockMovement { BusinessId = BusinessId, MaterialId = m.Id, Kind = StockMovementKind.Receive, Quantity = r.OpeningStock, OnHandAfter = r.OpeningStock, Note = "Opening stock" });
        audit.Record(BusinessId, tenant.UserId, "inventory.material_created", "MaterialStock", m.Id, new { m.Sku, m.Name });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505" && pg.ConstraintName == "IX_Materials_BusinessId_Sku")
        { throw AppException.Conflict("duplicate_sku", "A material with this SKU already exists."); }
        return ToDto(m);
    }

    public async Task<MaterialDto> ChangeAsync(Guid id, string action, StockChangeRequest r, CancellationToken ct)
    {
        await entitlements.RequireTierAsync(BusinessId, Tailoring.Domain.Billing.EntitlementKeys.InventoryLevel, "basic", ct);
        if (r.Quantity <= 0 || !decimal.IsFinite(r.Quantity)) throw AppException.BadRequest("invalid_quantity", "Quantity must be greater than zero.");
        var kind = action.ToLowerInvariant() switch { "receive" => StockMovementKind.Receive, "adjust" => StockMovementKind.Adjust, "reserve" => StockMovementKind.Reserve, "release" => StockMovementKind.Release, "consume" => StockMovementKind.Consume, _ => throw AppException.BadRequest("invalid_action", "Unsupported stock action.") };
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var m = await db.Materials.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("material_not_found", "Material was not found.");
        await EnsureMaterialLocationAsync(m, ct);
        if (r.ExpectedVersion.HasValue && m.Version != r.ExpectedVersion.Value) throw AppException.Conflict("stock_version_conflict", "Stock changed since it was read. Refresh and retry.");
        if (r.TaskId.HasValue)
        {
            var task = await db.ProductionTasks.SingleOrDefaultAsync(x => x.Id == r.TaskId.Value, ct) ?? throw AppException.NotFound("task_not_found", "Production task was not found.");
            if (task.Status is Tailoring.Domain.Production.ProductionTaskStatus.Completed or Tailoring.Domain.Production.ProductionTaskStatus.Cancelled)
                throw AppException.Conflict("task_closed", "Materials cannot be reserved or consumed for a completed or cancelled task.");
            var order = await db.Orders.SingleAsync(x => x.Id == task.OrderId, ct);
            if (m.LocationId.HasValue && m.LocationId.Value != order.LocationId)
                throw AppException.Conflict("material_location_mismatch", "Material and production order must belong to the same location.");
        }
        if (kind is StockMovementKind.Release or StockMovementKind.Consume)
        {
            var ledger = await db.StockMovements.AsNoTracking().Where(x => x.MaterialId == m.Id && x.TaskId == r.TaskId).ToListAsync(ct);
            var taskReserved = ledger.Where(x => x.Kind == StockMovementKind.Reserve).Sum(x => x.Quantity)
                - ledger.Where(x => x.Kind is StockMovementKind.Release or StockMovementKind.Consume).Sum(x => x.Quantity);
            if (r.Quantity > taskReserved) throw AppException.Conflict("reservation_exceeded", r.TaskId.HasValue
                ? "This task has insufficient reserved quantity for the requested release or consumption."
                : "Unassigned stock reservations are insufficient; specify the task to release or consume its reservation.");
        }
        switch (kind)
        {
            case StockMovementKind.Receive: m.OnHand += r.Quantity; break;
            case StockMovementKind.Adjust:
                if (r.Quantity < m.Reserved) throw AppException.Conflict("reserved_stock_protected", "Physical stock count cannot be lower than the quantity already reserved.");
                m.OnHand = r.Quantity; break; // adjustment quantity is the desired physical count
            case StockMovementKind.Reserve:
                if (r.Quantity > m.OnHand - m.Reserved) throw AppException.Conflict("insufficient_stock", "Available stock is insufficient for this reservation.");
                m.Reserved += r.Quantity; break;
            case StockMovementKind.Release:
                if (r.Quantity > m.Reserved) throw AppException.Conflict("reservation_exceeded", "Cannot release more than the reserved quantity.");
                m.Reserved -= r.Quantity; break;
            case StockMovementKind.Consume:
                if (r.Quantity > m.Reserved) throw AppException.Conflict("reservation_required", "Consume stock only from an existing reservation.");
                m.Reserved -= r.Quantity; m.OnHand -= r.Quantity; break;
        }
        m.UpdatedAtUtc = DateTime.UtcNow;
        db.StockMovements.Add(new StockMovement { BusinessId = BusinessId, MaterialId = m.Id, Kind = kind, Quantity = r.Quantity, OnHandAfter = m.OnHand, ReservedAfter = m.Reserved, OrderId = r.OrderId, TaskId = r.TaskId, ActorUserId = tenant.UserId, Note = r.Note?.Trim() });
        audit.Record(BusinessId, tenant.UserId, "inventory.stock_" + action.ToLowerInvariant(), "MaterialStock", m.Id, new { r.Quantity, m.OnHand, m.Reserved, r.OrderId, r.TaskId });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("stock_concurrency_conflict", "Stock changed concurrently. Refresh and retry."); }
        catch (PostgresException ex) when (ex.SqlState is "40001" or "40P01") { throw AppException.Conflict("stock_concurrency_conflict", "Concurrent stock activity changed the result. Refresh and retry."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && (pg.SqlState is "40001" or "40P01")) { throw AppException.Conflict("stock_concurrency_conflict", "Concurrent stock activity changed the result. Refresh and retry."); }
        return ToDto(m);
    }
    public Task<IReadOnlyList<MaterialDto>> ReserveTaskAsync(Guid taskId, TaskMaterialBatchRequest request, CancellationToken ct)
        => ChangeTaskReservationsAsync(taskId, request, StockMovementKind.Reserve, ct);

    public Task<IReadOnlyList<MaterialDto>> ConsumeTaskAsync(Guid taskId, TaskMaterialBatchRequest request, CancellationToken ct)
        => ChangeTaskReservationsAsync(taskId, request, StockMovementKind.Consume, ct);

    private async Task<IReadOnlyList<MaterialDto>> ChangeTaskReservationsAsync(Guid taskId, TaskMaterialBatchRequest request, StockMovementKind action, CancellationToken ct)
    {
        await entitlements.RequireTierAsync(BusinessId, Tailoring.Domain.Billing.EntitlementKeys.InventoryLevel, "basic", ct);
        if (request.Materials is null || request.Materials.Count is < 1 or > 100 || request.Materials.Any(x => x.MaterialId == Guid.Empty || x.Quantity <= 0) || request.Materials.Select(x => x.MaterialId).Distinct().Count() != request.Materials.Count)
            throw AppException.BadRequest("invalid_material_batch", "Provide 1–100 unique materials with positive quantities.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var task = await db.ProductionTasks.SingleOrDefaultAsync(x => x.Id == taskId, ct) ?? throw AppException.NotFound("task_not_found", "Production task was not found.");
        if (task.Status is Tailoring.Domain.Production.ProductionTaskStatus.Completed or Tailoring.Domain.Production.ProductionTaskStatus.Cancelled)
            throw AppException.Conflict("task_closed", "Materials cannot be reserved or consumed for a completed or cancelled task.");
        var order = await db.Orders.SingleAsync(x => x.Id == task.OrderId, ct);
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        if (allowed is not null && !allowed.Contains(order.LocationId)) throw AppException.NotFound("task_not_found", "Production task was not found.");
        var updated = new List<MaterialStock>();
        foreach (var line in request.Materials)
        {
            var material = await db.Materials.SingleOrDefaultAsync(x => x.Id == line.MaterialId, ct) ?? throw AppException.NotFound("material_not_found", "Material was not found.");
            await EnsureMaterialLocationAsync(material, ct);
            if (material.LocationId.HasValue && material.LocationId.Value != order.LocationId) throw AppException.Conflict("material_location_mismatch", "Material and production order must belong to the same location.");
            var movements = await db.StockMovements.AsNoTracking().Where(x => x.MaterialId == material.Id && x.TaskId == taskId).ToListAsync(ct);
            var reserved = movements.Where(x => x.Kind == StockMovementKind.Reserve).Sum(x => x.Quantity) - movements.Where(x => x.Kind is StockMovementKind.Release or StockMovementKind.Consume).Sum(x => x.Quantity);
            if (action == StockMovementKind.Reserve)
            {
                if (reserved > 0) throw AppException.Conflict("reservation_exists", "This task already has a reservation for one or more selected materials.");
                if (line.Quantity > material.OnHand - material.Reserved) throw AppException.Conflict("insufficient_stock", $"Insufficient available stock for {material.Name}.");
                material.Reserved += line.Quantity;
            }
            else
            {
                if (line.Quantity > reserved || line.Quantity > material.Reserved) throw AppException.Conflict("reservation_exceeded", $"Task reservation is insufficient for {material.Name}.");
                material.Reserved -= line.Quantity; material.OnHand -= line.Quantity;
            }
            material.UpdatedAtUtc = DateTime.UtcNow; updated.Add(material);
            db.StockMovements.Add(new StockMovement { BusinessId = BusinessId, MaterialId = material.Id, Kind = action, Quantity = line.Quantity, OnHandAfter = material.OnHand, ReservedAfter = material.Reserved, OrderId = order.Id, TaskId = taskId, ActorUserId = tenant.UserId, Note = request.Note?.Trim() });
        }
        audit.Record(BusinessId, tenant.UserId, action == StockMovementKind.Reserve ? "inventory.task_materials_reserved" : "inventory.task_materials_consumed", "ProductionTask", taskId, new { count = request.Materials.Count });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("stock_concurrency_conflict", "Concurrent stock activity changed the result. Refresh and retry."); }
        catch (PostgresException ex) when (ex.SqlState is "40001" or "40P01") { throw AppException.Conflict("stock_concurrency_conflict", "Concurrent stock activity changed the result. Refresh and retry."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && (pg.SqlState is "40001" or "40P01")) { throw AppException.Conflict("stock_concurrency_conflict", "Concurrent stock activity changed the result. Refresh and retry."); }
        return updated.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<StockMovementDto>> HistoryAsync(Guid id, int take, CancellationToken ct)
    {
        await entitlements.RequireTierAsync(BusinessId, Tailoring.Domain.Billing.EntitlementKeys.InventoryLevel, "basic", ct);
        var material = await db.Materials.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("material_not_found", "Material was not found.");
        await EnsureMaterialLocationAsync(material, ct);
        return await db.StockMovements.AsNoTracking().Where(x => x.MaterialId == id).OrderByDescending(x => x.CreatedAtUtc).Take(Math.Clamp(take, 1, 200)).Select(x => new StockMovementDto(x.Id,x.MaterialId,x.Kind.ToString(),x.Quantity,x.OnHandAfter,x.ReservedAfter,x.OrderId,x.TaskId,x.Note,x.CreatedAtUtc)).ToListAsync(ct);
    }
    private async Task EnsureMaterialLocationAsync(MaterialStock material, CancellationToken ct)
    {
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        if (allowed is not null && (!material.LocationId.HasValue || !allowed.Contains(material.LocationId.Value))) throw AppException.NotFound("material_not_found", "Material was not found.");
        if (material.LocationId.HasValue) await locations.EnsureAllowedAsync(material.LocationId.Value, ct);
    }
    private static MaterialDto ToDto(MaterialStock x) => new(x.Id,x.LocationId,x.Sku,x.Name,x.Unit,x.OnHand,x.Reserved,x.OnHand-x.Reserved,x.ReorderLevel,x.OnHand-x.Reserved<=x.ReorderLevel,x.Version);
}
