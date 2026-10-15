using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tailoring.Api.Security;
using Tailoring.Application.Common;
using Tailoring.Application.Documents;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

[ApiController, Route("api/documents")]
public class DocumentsController(IDocumentService documents) : ControllerBase
{
    /// <summary>multipart/form-data: file, optional description, customerId, orderId. The file name and declared type are ignored for decisions; the bytes are inspected.</summary>
    [HttpPost, HasPermission(Permissions.DocumentsManage), EnableRateLimiting("upload")]
    [RequestSizeLimit(17 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 17 * 1024 * 1024)]
    public async Task<ActionResult<DocumentDto>> Upload(IFormFile file, [FromForm] string? description, [FromForm] Guid? customerId, [FromForm] Guid? orderId, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var created = await documents.UploadAsync(stream, file.FileName, description, customerId, orderId, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet, HasPermission(Permissions.DocumentsView)]
    public async Task<PagedResult<DocumentDto>> List([FromQuery] DocumentListQuery query, CancellationToken ct) => await documents.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.DocumentsView)]
    public async Task<DocumentDto> Get(Guid id, CancellationToken ct) => await documents.GetAsync(id, ct);

    /// <summary>Always a download, never rendered in the page: the stored type is chosen by the server and sniffing is disabled.</summary>
    [HttpGet("{id:guid}/file"), HasPermission(Permissions.DocumentsView)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var content = await documents.OpenAsync(id, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content.Content, content.ContentType, content.FileName);
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.DocumentsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await documents.DeleteAsync(id, ct);
        return NoContent();
    }
}
