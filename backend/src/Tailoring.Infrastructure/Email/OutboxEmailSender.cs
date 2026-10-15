using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Email;
using Tailoring.Domain.Email;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Email;

/// <summary>
/// Queues mail durably. Delivery, retries and dead-lettering happen in <see cref="EmailDispatchService"/>, so a provider
/// outage never fails the caller's request. Note: SaveChanges persists anything else pending on the same context.
/// </summary>
public sealed class OutboxEmailSender(AppDbContext db) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        => QueueAsync(new EmailRequest(to, subject, body), ct);

    public async Task QueueAsync(EmailRequest request, CancellationToken ct = default)
    {
        if (!EmailAddressRules.TryNormalize(request.To, out var to))
            throw AppException.BadRequest("invalid_email", "That email address is not valid.");
        if (!EmailAddressRules.IsSafeHeaderValue(request.Subject) || string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 200)
            throw AppException.BadRequest("invalid_email_subject", "The email subject must be one line of at most 200 characters.");
        if (string.IsNullOrWhiteSpace(request.TextBody))
            throw AppException.BadRequest("empty_email_body", "The email body cannot be empty.");

        db.EmailOutbox.Add(new EmailOutboxMessage
        {
            BusinessId = request.BusinessId,
            ToAddress = to,
            Subject = request.Subject.Trim(),
            TextBody = request.TextBody,
            HtmlBody = request.HtmlBody,
            TemplateKey = request.TemplateKey,
        });
        await db.SaveChangesAsync(ct);
    }
}
