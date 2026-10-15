using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tailoring.Api.Security;
using Tailoring.Application.Common;
using Tailoring.Application.Media;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

[ApiController, Route("api/media")]
public class MediaController(IMediaService media) : ControllerBase
{
    /// <summary>multipart/form-data: file + purpose (default DesignImage). The file name and declared type are ignored; the bytes are inspected.</summary>
    [HttpPost, HasPermission(Permissions.MediaManage), EnableRateLimiting("upload")]
    [RequestSizeLimit(14 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 14 * 1024 * 1024)]
    public async Task<ActionResult<MediaAssetDto>> Upload(IFormFile file, [FromForm] string? purpose, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var created = await media.UploadAsync(stream, file.FileName, purpose ?? "DesignImage", ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet, HasPermission(Permissions.MediaView)]
    public async Task<PagedResult<MediaAssetDto>> List([FromQuery] MediaListQuery query, CancellationToken ct)
        => await media.ListAsync(query, ct);

    [HttpGet("usage"), HasPermission(Permissions.MediaView)]
    public async Task<MediaUsageDto> Usage(CancellationToken ct) => await media.GetUsageAsync(ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.MediaView)]
    public async Task<MediaAssetDto> Get(Guid id, CancellationToken ct) => await media.GetAsync(id, ct);

    /// <summary>Authenticated, tenant-scoped pixels. variant: sm | md | orig.</summary>
    [HttpGet("{id:guid}/file/{variant}"), HasPermission(Permissions.MediaView)]
    public async Task<IActionResult> File(Guid id, string variant, CancellationToken ct)
    {
        var content = await media.OpenAsync(id, variant, ct);
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.XContentTypeOptions = "nosniff";
        return base.File(content.Content, content.ContentType);
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.MediaManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await media.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Anonymous. Serves only images attached to a Public, Active design; everything else is a plain 404.</summary>
[ApiController, Route("public/media")]
public class PublicMediaController(IMediaService media) : ControllerBase
{
    [HttpGet("{id:guid}/{variant}"), AllowAnonymous, EnableRateLimiting("public")]
    public async Task<IActionResult> Get(Guid id, string variant, CancellationToken ct)
    {
        var content = await media.OpenPublicAsync(id, variant, ct);
        Response.Headers.CacheControl = "public, max-age=3600";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content.Content, content.ContentType);
    }
}
