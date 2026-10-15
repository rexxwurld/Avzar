using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Auth;
using Tailoring.Application.Common;
using Tailoring.Domain.Access;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>
/// Refresh-token sessions for the mobile client. Every refresh rotates the token. Presenting an already-rotated token revokes
/// the session (theft or replay). Parallel refreshes are serialised by the row version and the loser gets a retryable 409.
/// </summary>
public sealed class SessionService(
    AppDbContext db, UserManager<AppUser> users, IPermissionService permissions,
    IOptions<AuthOptions> options, ILogger<SessionService> log) : ISessionService
{
    private AuthOptions O => options.Value;

    public async Task<TokenPair> CreateAsync(Guid userId, Guid businessId, string? deviceName, CancellationToken ct = default)
    {
        if (await permissions.GetAccessAsync(userId, businessId, ct) is null)
            throw AppException.Forbidden("not_a_member", "You are not an active member of that business.");

        var now = DateTime.UtcNow;

        // Keep the number of live sessions bounded: drop the least recently used ones.
        var live = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.AbsoluteExpiresAtUtc > now && s.IdleExpiresAtUtc > now)
            .OrderBy(s => s.LastUsedAtUtc).ToListAsync(ct);
        var excess = live.Count - (Math.Max(1, O.MaxSessionsPerUser) - 1);
        foreach (var old in live.Take(Math.Max(0, excess))) Revoke(old, "session_limit", now);

        var secret = TokenHasher.NewToken();
        var absolute = now.AddDays(O.RefreshAbsoluteDays);
        var session = new UserSession
        {
            UserId = userId, BusinessId = businessId,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Unknown device" : Parse.Truncate(deviceName.Trim(), 80),
            CurrentTokenHash = TokenHasher.Hash(secret),
            LastUsedAtUtc = now,
            AbsoluteExpiresAtUtc = absolute,
            IdleExpiresAtUtc = SessionRules.NextIdleExpiry(now, TimeSpan.FromDays(O.RefreshIdleDays), absolute),
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return new TokenPair(session.Id, Compose(session.Id, secret), session.IdleExpiresAtUtc);
    }

    public async Task<RefreshResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (!SessionRules.TryParseToken(refreshToken, out var sessionId, out var secret))
            throw Invalid();

        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct) ?? throw Invalid();
        var now = DateTime.UtcNow;

        switch (SessionRules.Decide(session, TokenHasher.Hash(secret), now))
        {
            case RefreshDecision.Rotate: break;
            case RefreshDecision.ReuseDetected:
                await RevokeAtomicAsync(session.Id, "token_reuse", now, ct);
                log.LogWarning("Refresh token reuse detected; session {SessionId} revoked.", session.Id);
                throw AppException.Unauthorized("token_reuse_detected", "This session was ended because a token was used twice. Please sign in again.");
            case RefreshDecision.Expired:
                await RevokeAtomicAsync(session.Id, "expired", now, ct);
                throw AppException.Unauthorized("session_expired", "Your session has expired. Please sign in again.");
            default: throw Invalid();
        }

        // The account must still be allowed in: user exists and is not locked out, membership still active.
        var user = await users.FindByIdAsync(session.UserId.ToString());
        if (user is null || await users.IsLockedOutAsync(user)
            || await permissions.GetAccessAsync(session.UserId, session.BusinessId, ct) is null)
        {
            await RevokeAtomicAsync(session.Id, "account_inactive", now, ct);
            throw AppException.Unauthorized("session_revoked", "This session is no longer valid. Please sign in again.");
        }

        var newSecret = TokenHasher.NewToken();
        session.PreviousTokenHash = session.CurrentTokenHash;
        session.CurrentTokenHash = TokenHasher.Hash(newSecret);
        session.LastUsedAtUtc = now;
        session.IdleExpiresAtUtc = SessionRules.NextIdleExpiry(now, TimeSpan.FromDays(O.RefreshIdleDays), session.AbsoluteExpiresAtUtc);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict("refresh_in_progress", "This session is being refreshed by another request. Retry with the newest token.");
        }

        return new RefreshResult(new SessionIdentity(session.UserId, session.BusinessId),
            new TokenPair(session.Id, Compose(session.Id, newSecret), session.IdleExpiresAtUtc));
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        // Logout is idempotent and reveals nothing: a bad token is simply ignored.
        if (!SessionRules.TryParseToken(refreshToken, out var sessionId, out var secret)) return;
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.RevokedAtUtc is not null) return;
        var hash = TokenHasher.Hash(secret);
        if (hash != session.CurrentTokenHash && hash != session.PreviousTokenHash) return; // only the holder of a real token may end it
        Revoke(session, "logout", DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SessionDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.AbsoluteExpiresAtUtc > now && s.IdleExpiresAtUtc > now)
            .OrderByDescending(s => s.LastUsedAtUtc)
            .Select(s => new SessionDto(s.Id, s.DeviceName, s.CreatedAtUtc, s.LastUsedAtUtc, s.IdleExpiresAtUtc))
            .ToListAsync(ct);
    }

    public async Task RevokeByIdAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        // Another user's session id behaves exactly like a missing one.
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw AppException.NotFound("session_not_found", "Session not found.");
        if (session.RevokedAtUtc is null) Revoke(session, "revoked_by_user", DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAtUtc, (DateTime?)now).SetProperty(s => s.RevokedReason, reason), ct);
    }

    public Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return db.UserSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && s.UserId == userId
            && s.RevokedAtUtc == null && s.AbsoluteExpiresAtUtc > now, ct);
    }

    /// <summary>
    /// Revokes without the row-version check: a concurrent rotation or revocation must not turn a security decision into a 500.
    /// Idempotent (first reason wins).
    /// </summary>
    private async Task RevokeAtomicAsync(Guid sessionId, string reason, DateTime now, CancellationToken ct)
        => await db.UserSessions.Where(s => s.Id == sessionId && s.RevokedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAtUtc, (DateTime?)now).SetProperty(s => s.RevokedReason, reason), ct);

    private static void Revoke(UserSession s, string reason, DateTime now) { s.RevokedAtUtc = now; s.RevokedReason = reason; }
    private static string Compose(Guid id, string secret) => $"{id:N}.{secret}";
    private static AppException Invalid() => AppException.Unauthorized("invalid_refresh_token", "The refresh token is invalid.");
}
