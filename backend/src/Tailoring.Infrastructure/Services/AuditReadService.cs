using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Audit;
using Tailoring.Application.Common;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class AuditReadService(AppDbContext db) : IAuditReadService
{
    public async Task<PagedResult<AuditEntryDto>> ListAsync(AuditListQuery query, CancellationToken ct = default)
    {
        // The tenant query filter on AuditLogs limits this to the caller's business.
        var q = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(a => a.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(a => a.EntityType == query.EntityType);
        if (query.EntityId is { } entityId) q = q.Where(a => a.EntityId == entityId);
        if (query.ActorUserId is { } actorId) q = q.Where(a => a.ActorUserId == actorId);
        if (query.FromUtc is { } fromUtc) q = q.Where(a => a.CreatedAtUtc >= AsUtc(fromUtc));
        if (query.ToUtc is { } toUtc) q = q.Where(a => a.CreatedAtUtc < AsUtc(toUtc));

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(a => new AuditEntryDto(
                a.Id, a.CreatedAtUtc, a.ActorUserId,
                db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.FullName).FirstOrDefault(),
                a.Action, a.EntityType, a.EntityId, a.DetailsJson))
            .ToListAsync(ct);

        return new PagedResult<AuditEntryDto>(items, query.Page, query.PageSize, total);
    }

    /// <summary>A query-string date with no zone is read as UTC, never as server-local time (Npgsql needs Kind=Utc).</summary>
    private static DateTime AsUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };
}
