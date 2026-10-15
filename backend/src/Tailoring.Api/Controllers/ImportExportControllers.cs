using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tailoring.Api.Security;
using Tailoring.Application.Common;
using Tailoring.Application.ImportExport;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

public sealed class CsvCommitForm
{
    [Required] public IFormFile File { get; set; } = null!;
    /// <summary>The fileHash returned by the preview.</summary>
    [Required] public string ExpectedHash { get; set; } = "";
    /// <summary>skip (default) or update.</summary>
    public string OnDuplicate { get; set; } = "skip";
    /// <summary>Required for update: the number of existing customers the preview said would change.</summary>
    public int? ConfirmUpdateCount { get; set; }
    public bool SkipInvalidRows { get; set; }
}

[ApiController, Route("api/customers")]
public class CustomerImportController(ICustomerImportService import) : ControllerBase
{
    private const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>UTF-8 CSV (with a byte-order mark so Excel opens it correctly). Cells that could run as spreadsheet formulas are defused.</summary>
    [HttpGet("export"), HasPermission(Permissions.CustomersView)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var csv = await import.ExportAsync(ct);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"customers-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    /// <summary>multipart/form-data with a "file". Changes nothing; returns what an import would do and the hash to confirm with.</summary>
    [HttpPost("import/preview"), HasPermission(Permissions.CustomersManage), EnableRateLimiting("upload")]
    [RequestSizeLimit(MaxBytes + 512 * 1024), RequestFormLimits(MultipartBodyLengthLimit = MaxBytes + 512 * 1024)]
    public async Task<CsvImportPreviewDto> Preview(IFormFile file, CancellationToken ct)
        => await import.PreviewAsync(await ReadAsync(file, ct), ct);

    /// <summary>409 file_changed if the file differs from the previewed one; 409 confirmation_required when updates are not confirmed.</summary>
    [HttpPost("import/commit"), HasPermission(Permissions.CustomersManage), EnableRateLimiting("upload")]
    [RequestSizeLimit(MaxBytes + 512 * 1024), RequestFormLimits(MultipartBodyLengthLimit = MaxBytes + 512 * 1024)]
    public async Task<CsvImportResultDto> Commit([FromForm] CsvCommitForm form, CancellationToken ct)
        => await import.CommitAsync(await ReadAsync(form.File, ct),
            new CsvImportCommitOptions(form.ExpectedHash, form.OnDuplicate, form.ConfirmUpdateCount, form.SkipInvalidRows), ct);

    private static async Task<string> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) throw AppException.BadRequest("csv_empty", "The file is empty.");
        if (file.Length > MaxBytes) throw AppException.BadRequest("file_too_large", "The file is larger than 5 MB.");
        await using var s = file.OpenReadStream();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (Array.IndexOf(bytes, (byte)0) >= 0) throw AppException.BadRequest("csv_not_text", "This does not look like a CSV text file.");
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { throw AppException.BadRequest("csv_not_utf8", "Save the file as \"CSV UTF-8\" and try again."); }
    }
}

[ApiController, Route("api/search")]
public class SearchController(ISearchService search) : ControllerBase
{
    /// <summary>Results are limited to what the caller's role may view.</summary>
    [HttpGet, Authorize, EnableRateLimiting("upload")]
    public async Task<SearchResultDto> Search([FromQuery] string q, [FromQuery] int limit = 5, CancellationToken ct = default)
        => await search.SearchAsync(q, limit, ct);
}
