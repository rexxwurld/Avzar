using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Tailoring.Application.Email;
using Tailoring.Domain.Access;
using Tailoring.Domain.Email;
using Tailoring.Domain.Production;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>
/// Queues at most one overdue reminder per assigned worker/task/UTC day. Delivery is delegated to the durable email dispatcher;
/// this worker never sends mail directly. The unique outbox key makes concurrent app instances safe.
/// </summary>
public sealed class OverdueTaskReminderWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<OverdueTaskReminderWorker> log) : BackgroundService
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(15);
    private const int BatchLimit = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Notifications:OverdueTaskRemindersEnabled", true))
        {
            log.LogInformation("Overdue task reminders are disabled by configuration.");
            return;
        }

        var pollSeconds = Math.Clamp(configuration.GetValue("Notifications:OverdueTaskReminderPollSeconds", (int)DefaultPollInterval.TotalSeconds), 60, 3600);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var queued = await QueueOverdueRemindersAsync(db, DateTime.UtcNow, BatchLimit, stoppingToken);
                if (queued > 0) log.LogInformation("Queued {Count} overdue production-task reminder(s).", queued);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Overdue task reminder cycle failed; it will retry."); }

            try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal static async Task<int> QueueOverdueRemindersAsync(AppDbContext db, DateTime nowUtc, int batchLimit, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(nowUtc);
        var dayStartUtc = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var nextDayUtc = dayStartUtc.AddDays(1);
        var existingKeys = await db.EmailOutbox.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TemplateKey == "task.overdue" && x.CreatedAtUtc >= dayStartUtc && x.CreatedAtUtc < nextDayUtc && x.DeduplicationKey != null)
            .Select(x => x.DeduplicationKey!).ToListAsync(ct);
        var remindedTaskIds = existingKeys.Select(key => key.Split(':'))
            .Where(parts => parts.Length == 3 && Guid.TryParseExact(parts[1], "N", out _))
            .Select(parts => Guid.ParseExact(parts[1], "N")).Distinct().ToArray();

        var dueTasks = await db.ProductionTasks.IgnoreQueryFilters().AsNoTracking()
            .Where(t => !remindedTaskIds.Contains(t.Id) && t.DeadlineUtc != null && t.DeadlineUtc < nowUtc && t.AssignedMembershipId != null
                && (t.Status == ProductionTaskStatus.Pending || t.Status == ProductionTaskStatus.Scheduled
                    || t.Status == ProductionTaskStatus.InProgress || t.Status == ProductionTaskStatus.Paused)
                && db.Businesses.IgnoreQueryFilters().Any(b => b.Id == t.BusinessId && b.IsActive))
            .OrderBy(t => t.DeadlineUtc).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.BusinessId, t.Title, t.DeadlineUtc, MembershipId = t.AssignedMembershipId })
            .Take(Math.Clamp(batchLimit, 1, 5000))
            .ToListAsync(ct);

        var queued = 0;
        foreach (var task in dueTasks)
        {
            if (task.MembershipId is not { } membershipId || task.DeadlineUtc is not { } deadline) continue;
            var recipient = await (from membership in db.Memberships.IgnoreQueryFilters().AsNoTracking()
                                   join user in db.Users.IgnoreQueryFilters().AsNoTracking() on membership.UserId equals user.Id
                                   where membership.Id == membershipId && membership.BusinessId == task.BusinessId
                                      && membership.Status == MembershipStatus.Active
                                   select new { membership.DisplayName, user.Email }).FirstOrDefaultAsync(ct);
            if (recipient is null || !EmailAddressRules.TryNormalize(recipient.Email, out var email)) continue;

            var key = OverdueTaskReminderRules.DeduplicationKey(task.Id, day);
            if (await db.EmailOutbox.IgnoreQueryFilters().AnyAsync(x => x.DeduplicationKey == key, ct)) continue;
            db.EmailOutbox.Add(new EmailOutboxMessage
            {
                BusinessId = task.BusinessId,
                ToAddress = email,
                Subject = "A production task is overdue",
                TextBody = $"Hello {recipient.DisplayName},\n\nTask '{task.Title}' is overdue (deadline: {deadline:yyyy-MM-dd HH:mm} UTC). Please open Avzar to review its status and next steps.",
                TemplateKey = "task.overdue",
                DeduplicationKey = key,
                NextAttemptUtc = nowUtc
            });
            queued++;
        }

        if (queued == 0) return 0;
        try
        {
            await db.SaveChangesAsync(ct);
            return queued;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another app instance may have queued the same daily key. Detach this cycle's unsaved rows and let the next poll retry.
            foreach (var entry in db.ChangeTracker.Entries<EmailOutboxMessage>().Where(x => x.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            return 0;
        }
    }
}

internal static class OverdueTaskReminderRules
{
    public static string DeduplicationKey(Guid taskId, DateOnly dayUtc)
        => $"task.overdue:{taskId:N}:{dayUtc:yyyyMMdd}";
}
