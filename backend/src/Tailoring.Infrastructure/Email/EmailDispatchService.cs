using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailoring.Application.Email;
using Tailoring.Domain.Email;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Email;

/// <summary>
/// Delivers due outbox rows. Safe to run from several processes at once: rows are claimed with
/// FOR UPDATE SKIP LOCKED plus a lease, so each message is attempted by one dispatcher at a time.
/// </summary>
public sealed class EmailDispatchService(AppDbContext db, IEmailTransport transport, IOptions<EmailOptions> options, ILogger<EmailDispatchService> log)
{
    /// <summary>Claims and processes one batch. Returns the number of messages attempted. <paramref name="nowUtc"/> exists for tests.</summary>
    public async Task<int> DispatchOnceAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var o = options.Value;
        var now = nowUtc ?? DateTime.UtcNow;
        var leaseUntil = now.AddMinutes(o.LeaseMinutes);
        var batch = o.BatchSize;

        // Attempts is incremented at claim time, so a crash mid-send still counts toward MaxAttempts (a poison message cannot loop forever).
        var ids = await db.Database.SqlQuery<Guid>($"""
            UPDATE "EmailOutbox" SET "LeaseUntilUtc" = {leaseUntil}, "Attempts" = "Attempts" + 1
            WHERE "Id" IN (
                SELECT "Id" FROM "EmailOutbox"
                WHERE "Status" = 'Pending' AND "NextAttemptUtc" <= {now} AND ("LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" < {now})
                ORDER BY "NextAttemptUtc"
                LIMIT {batch}
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);

        foreach (var id in ids)
        {
            // Fresh load: the raw UPDATE bypassed the change tracker.
            var m = await db.EmailOutbox.AsTracking().FirstAsync(x => x.Id == id, ct);
            TransportResult result;
            // Checked at send time (not enqueue time) so a bounce that arrives while a message is queued still stops it.
            var suppressionKey = EmailAddressRules.SuppressionKey(m.ToAddress);
            if (await db.EmailSuppressions.AnyAsync(x => x.Address == suppressionKey, ct))
            {
                // Rejected = permanent: the row goes to Failed and its body is purged, and the transport is never called.
                result = TransportResult.Reject("Recipient is on the suppression list (earlier permanent bounce or complaint)");
            }
            else
            try
            {
                result = await transport.SendAsync(new OutboundEmail(m.Id, m.ToAddress, m.Subject, m.TextBody, m.HtmlBody), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // shutting down: the lease expires and the message is retried by the next dispatcher
            }
            catch (Exception ex)
            {
                result = TransportResult.Retry($"Transport threw {ex.GetType().Name}");
                log.LogWarning(ex, "Email transport threw for message {MessageId}", m.Id);
            }

            Apply(m, result, now, o.MaxAttempts);
            await db.SaveChangesAsync(ct);
            // Never log addresses' bodies or links; ids and outcomes are enough.
            log.LogInformation("Email {MessageId} attempt {Attempt}: {Status}", m.Id, m.Attempts, m.Status);
        }
        return ids.Count;
    }

    /// <summary>The state transition, kept separate and pure-ish so it is easy to reason about.</summary>
    internal static void Apply(EmailOutboxMessage m, TransportResult r, DateTime now, int maxAttempts)
    {
        m.LeaseUntilUtc = null;
        m.UpdatedAtUtc = now;
        if (r.Success)
        {
            m.Status = EmailStatus.Sent;
            m.SentAtUtc = now;
            m.ProviderMessageId = r.ProviderMessageId;
            m.LastError = null;
            Purge(m, now);
        }
        else if (!r.Transient || m.Attempts >= maxAttempts)
        {
            m.Status = EmailStatus.Failed;
            m.LastError = r.Transient ? $"Gave up after {m.Attempts} attempts: {r.Error}" : $"Rejected: {r.Error}";
            Purge(m, now);
        }
        else
        {
            m.LastError = r.Error;
            m.NextAttemptUtc = now + EmailRetryPolicy.NextDelay(m.Attempts);
        }
    }

    private static void Purge(EmailOutboxMessage m, DateTime now)
    {
        // Bodies can hold one-time links; nothing needs them once the message is terminal.
        m.TextBody = "";
        m.HtmlBody = null;
        m.BodyPurgedAtUtc = now;
    }
}
