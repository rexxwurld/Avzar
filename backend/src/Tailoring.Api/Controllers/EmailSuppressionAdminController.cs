using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Api.Controllers;

/// <summary>Read-only, platform-admin view of global email suppressions. Removal is intentionally not exposed here so a routine
/// admin action cannot silently re-enable delivery to a complained-about or permanently bounced address.</summary>
[ApiController, Route("api/platform-admin/email-suppressions"), Authorize]
public sealed class EmailSuppressionAdminController(
    DbContextOptions<AppDbContext> options,
    IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        var allowed = (config.GetSection("PlatformAdmin:Emails").Get<string[]>() ?? Array.Empty<string>())
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.Identity?.Name;
        if (email is null || !allowed.Contains(email))
            throw Tailoring.Application.Common.AppException.Forbidden("platform_admin_required", "Platform administrator access is required.");

        await using var db = new AppDbContext(options, new PlatformAdminTenantContextForSuppressions());
        var rows = db.EmailSuppressions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            rows = rows.Where(x => x.Address.Contains(term));
        }
        var take = Math.Clamp(limit, 1, 500);
        var result = await rows.OrderByDescending(x => x.CreatedAtUtc).Take(take)
            .Select(x => new { x.Id, x.Address, x.Reason, x.CreatedAtUtc }).ToListAsync(ct);
        return Ok(new { items = result, limit = take });
    }
}

internal sealed class PlatformAdminTenantContextForSuppressions : Tailoring.Application.Abstractions.ITenantContext
{
    public Guid? UserId => null;
    public Guid? BusinessId => null;
}
