using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using Tailoring.Application.ImportExport;
using Tailoring.Application.Common;
using Tailoring.Api.Security;
using Tailoring.Domain.Access;
using Tailoring.Infrastructure.Services;

namespace Tailoring.Api.Controllers;

[ApiController, Route("api/inventory")]
public sealed class InventoryController(IInventoryService inventory) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<MaterialDto>> List(CancellationToken ct) => inventory.ListAsync(ct);
    [HttpGet("export"), HasPermission(Permissions.InventoryView)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var rows = await inventory.ListAsync(ct);
        var csv = new StringBuilder().Append(CsvWriter.Row("SKU", "Name", "Unit", "Opening stock", "Reorder level", "Location ID", "Reserved", "Available", "Low stock")).Append("\r\n");
        foreach (var x in rows) csv.Append(CsvWriter.Row(x.Sku, x.Name, x.Unit, x.OnHand.ToString(System.Globalization.CultureInfo.InvariantCulture), x.ReorderLevel.ToString(System.Globalization.CultureInfo.InvariantCulture), x.LocationId?.ToString(), x.Reserved.ToString(System.Globalization.CultureInfo.InvariantCulture), x.Available.ToString(System.Globalization.CultureInfo.InvariantCulture), x.LowStock ? "yes" : "no")).Append("\r\n");
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"inventory-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
    [HttpPost("import/preview"), HasPermission(Permissions.InventoryManage), EnableRateLimiting("upload")]
    [RequestSizeLimit(5 * 1024 * 1024 + 512 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 5 * 1024 * 1024 + 512 * 1024)]
    public async Task<InventoryImportPreviewDto> PreviewImport(IFormFile file, CancellationToken ct)
        => await inventory.PreviewImportAsync(await ReadCsvAsync(file, ct), ct);

    [HttpPost("import/commit"), HasPermission(Permissions.InventoryManage), EnableRateLimiting("upload")]
    [RequestSizeLimit(5 * 1024 * 1024 + 512 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 5 * 1024 * 1024 + 512 * 1024)]
    public async Task<InventoryImportResultDto> CommitImport([FromForm] InventoryCsvCommitForm form, CancellationToken ct)
        => await inventory.CommitImportAsync(await ReadCsvAsync(form.File, ct), new InventoryImportCommitRequest(form.ExpectedHash, form.SkipInvalidRows), ct);

    private static async Task<string> ReadCsvAsync(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw AppException.BadRequest("csv_empty", "Choose a non-empty CSV file.");
        if (file.Length > 5 * 1024 * 1024) throw AppException.BadRequest("file_too_large", "CSV must be under 5 MB.");
        await using var stream = file.OpenReadStream(); using var memory = new MemoryStream(); await stream.CopyToAsync(memory, ct); var bytes = memory.ToArray();
        if (Array.IndexOf(bytes, (byte)0) >= 0) throw AppException.BadRequest("csv_not_text", "This does not look like a CSV text file.");
        try { return new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { throw AppException.BadRequest("csv_not_utf8", "Save the file as CSV UTF-8 and try again."); }
    }

    [HttpPost, HasPermission(Permissions.InventoryManage)]
    public async Task<ActionResult<MaterialDto>> Create(MaterialCreateRequest request, CancellationToken ct) => StatusCode(201, await inventory.CreateAsync(request, ct));
    [HttpPost("{id:guid}/{actionName}"), HasPermission(Permissions.InventoryManage)]
    public Task<MaterialDto> Change(Guid id, string actionName, StockChangeRequest request, CancellationToken ct) => inventory.ChangeAsync(id, actionName, request, ct);
    [HttpPost("tasks/{taskId:guid}/reserve"), HasPermission(Permissions.InventoryManage)]
    public Task<IReadOnlyList<MaterialDto>> ReserveTask(Guid taskId, TaskMaterialBatchRequest request, CancellationToken ct) => inventory.ReserveTaskAsync(taskId, request, ct);
    [HttpPost("tasks/{taskId:guid}/consume"), HasPermission(Permissions.InventoryManage)]
    public Task<IReadOnlyList<MaterialDto>> ConsumeTask(Guid taskId, TaskMaterialBatchRequest request, CancellationToken ct) => inventory.ConsumeTaskAsync(taskId, request, ct);
    [HttpGet("{id:guid}/movements"), HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<StockMovementDto>> History(Guid id, [FromQuery] int take = 100, CancellationToken ct = default) => inventory.HistoryAsync(id, take, ct);
}

public sealed class InventoryCsvCommitForm
{
    [System.ComponentModel.DataAnnotations.Required] public IFormFile File { get; set; } = null!;
    [System.ComponentModel.DataAnnotations.Required] public string ExpectedHash { get; set; } = "";
    public bool SkipInvalidRows { get; set; }
}
