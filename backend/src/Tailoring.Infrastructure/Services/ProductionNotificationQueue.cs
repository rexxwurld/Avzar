using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Common;
using Tailoring.Application.Email;
using Tailoring.Domain.Access;
using Tailoring.Domain.Email;
using Tailoring.Domain.Production;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>
/// Queues production email notifications in the same DbContext save as the task mutation.
/// Delivery is asynchronous through the existing durable outbox; no provider call is made in a request.
/// </summary>
internal static class ProductionNotificationQueue
{
    public static async Task QueueMembershipAsync(
        AppDbContext db,
        Guid businessId,
        ProductionTask task,
        Guid membershipId,
        string templateKey,
        string subject,
        string message,
        CancellationToken ct)
    {
        var recipient = await (from membership in db.Memberships.AsNoTracking()
                               join user in db.Users.AsNoTracking() on membership.UserId equals user.Id
                               where membership.Id == membershipId
                                  && membership.BusinessId == businessId
                                  && membership.Status == MembershipStatus.Active
                               select new { membership.DisplayName, user.Email })
            .FirstOrDefaultAsync(ct);

        if (recipient is null || !EmailAddressRules.TryNormalize(recipient.Email, out var email)) return;

        var key = $"{templateKey}:{task.Id:N}:{membershipId:N}:{task.Version}";
        if (await db.EmailOutbox.AnyAsync(x => x.DeduplicationKey == key, ct)) return;

        db.EmailOutbox.Add(new EmailOutboxMessage
        {
            BusinessId = businessId,
            ToAddress = email,
            Subject = subject,
            TextBody = $"Hello {recipient.DisplayName},\n\n{message}\n\nPlease open Avzar to view the task details.",
            TemplateKey = templateKey,
            DeduplicationKey = key,
            NextAttemptUtc = DateTime.UtcNow
        });
    }

    public static async Task QueueIssueForManagersAsync(
        AppDbContext db,
        Guid businessId,
        ProductionTask task,
        CancellationToken ct)
    {
        var recipients = await (from membership in db.Memberships.AsNoTracking()
                                join user in db.Users.AsNoTracking() on membership.UserId equals user.Id
                                where membership.BusinessId == businessId
                                   && membership.Status == MembershipStatus.Active
                                   && (membership.IsOwner ||
                                       db.MembershipRoles.Any(role => role.MembershipId == membership.Id &&
                                           db.RolePermissions.Any(permission => permission.RoleId == role.RoleId &&
                                               permission.Permission == Permissions.TasksManage)))
                                select new { membership.Id, membership.DisplayName, user.Email })
            .Distinct()
            .ToListAsync(ct);

        foreach (var recipient in recipients)
        {
            if (!EmailAddressRules.TryNormalize(recipient.Email, out var email)) continue;
            var key = $"task.issue:{task.Id:N}:{recipient.Id:N}:{task.Version}";
            if (await db.EmailOutbox.AnyAsync(x => x.DeduplicationKey == key, ct)) continue;

            db.EmailOutbox.Add(new EmailOutboxMessage
            {
                BusinessId = businessId,
                ToAddress = email,
                Subject = "A production task needs attention",
                TextBody = $"Hello {recipient.DisplayName},\n\nTask '{task.Title}' has been blocked. Reason: {task.BlockedReason ?? "No reason supplied"}.\n\nPlease open Avzar to review the issue.",
                TemplateKey = "task.issue",
                DeduplicationKey = key,
                NextAttemptUtc = DateTime.UtcNow
            });
        }
    }
}
