using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tailoring.Application.Abstractions;
using Tailoring.Domain.Access;
using Tailoring.Domain.Audit;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public static class TokenHasher
{
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public sealed class AuditLogger(AppDbContext db) : IAuditLogger
{
    public void Record(Guid businessId, Guid? actorUserId, string action, string entityType, Guid? entityId, object? details = null)
        => db.AuditLogs.Add(new AuditLog
        {
            BusinessId = businessId,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details)
        });
}

public sealed class PermissionService(AppDbContext db) : IPermissionService
{
    private readonly Dictionary<(Guid, Guid), MembershipAccess?> _cache = new();

    public async Task<MembershipAccess?> GetAccessAsync(Guid userId, Guid businessId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue((userId, businessId), out var cached)) return cached;

        var membership = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == userId && m.BusinessId == businessId && m.Status == MembershipStatus.Active)
            .Select(m => new { m.Id, m.IsOwner })
            .FirstOrDefaultAsync(ct);

        MembershipAccess? access = null;
        if (membership is not null)
        {
            var perms = await db.MembershipRoles.IgnoreQueryFilters().AsNoTracking()
                .Where(mr => mr.MembershipId == membership.Id && mr.BusinessId == businessId)
                .Join(db.RolePermissions.IgnoreQueryFilters(),
                      mr => new { mr.RoleId, mr.BusinessId },
                      rp => new { rp.RoleId, rp.BusinessId },
                      (mr, rp) => rp.Permission)
                .Distinct()
                .ToListAsync(ct);
            access = new MembershipAccess(membership.IsOwner, perms.ToHashSet());
        }

        _cache[(userId, businessId)] = access;
        return access;
    }
}
