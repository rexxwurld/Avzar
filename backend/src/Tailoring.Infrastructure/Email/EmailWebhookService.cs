using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailoring.Application.Email;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Email;

/// <summary>
/// Records what the provider says happened to a message it accepted. The signature is the only authentication, so nothing is
/// read from the payload until it verifies. The update is ONE conditional statement (only moves the status up), so retries,
/// duplicates and parallel deliveries of different events converge on the same result without a lock.
/// </summary>
public sealed class EmailWebhookService(AppDbContext db, IOptions<EmailOptions> options, ILogger<EmailWebhookService> log, TimeProvider? clock = null) : IEmailWebhookService
{
    public async Task<WebhookOutcome> HandleResendAsync(string body, string? svixId, string? svixTimestamp, string? svixSignature, CancellationToken ct = default)
    {
        if (!SvixSignature.TryParseSecret(options.Value.Resend.WebhookSecret, out var key)) return WebhookOutcome.Disabled;
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (!SvixSignature.Verify(key, svixId, svixTimestamp, svixSignature, body, now, SvixSignature.DefaultTolerance))
            return WebhookOutcome.InvalidSignature;

        if (!ResendWebhookEvent.TryParse(body, out var ev)) return WebhookOutcome.Ignored;
        var status = EmailDeliveryRules.StatusFor(ev.Type);
        if (status is null || string.IsNullOrWhiteSpace(ev.EmailId) || ev.EmailId.Length > 200) return WebhookOutcome.Ignored;

        var replaceable = EmailDeliveryRules.ReplaceableBy(status);
        var emailId = ev.EmailId;
        var at = now.UtcDateTime;
        var detail = ev.Detail;

        // Stop mailing a dead or complaining address. Idempotent and independent of the status update below: a replay, a
        // duplicate or an out-of-order event still converges on "suppressed". The address is copied inside the database from
        // the outbox row we sent, never read from the (signed but external) payload.
        var suppressionReason = EmailDeliveryRules.SuppressionReasonFor(ev.Type, ev.BounceType);
        if (suppressionReason is not null)
        {
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "EmailSuppressions" ("Id", "CreatedAtUtc", "Address", "Reason")
                SELECT {id}, {at}, lower(btrim("ToAddress")), {suppressionReason}
                FROM "EmailOutbox" WHERE "ProviderMessageId" = {emailId}
                ORDER BY "CreatedAtUtc" LIMIT 1
                ON CONFLICT ("Address") DO NOTHING
                """, ct);
        }

        var changed = await db.EmailOutbox
            .Where(m => m.ProviderMessageId == emailId && (m.DeliveryStatus == null || replaceable.Contains(m.DeliveryStatus)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.DeliveryStatus, status)
                .SetProperty(m => m.DeliveryUpdatedAtUtc, (DateTime?)at)
                .SetProperty(m => m.DeliveryDetail, detail), ct);

        if (changed > 0)
        {
            log.LogInformation("Email delivery report {Status} recorded for provider message {ProviderMessageId}", status, emailId);
            return WebhookOutcome.Applied;
        }
        var known = await db.EmailOutbox.AnyAsync(m => m.ProviderMessageId == emailId, ct);
        return known ? WebhookOutcome.NoChange : WebhookOutcome.UnknownMessage;
    }
}
