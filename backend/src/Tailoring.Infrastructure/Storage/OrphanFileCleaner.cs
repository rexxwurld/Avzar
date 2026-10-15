using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailoring.Application.Media;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Storage;

public sealed partial class OrphanFileCleaner(AppDbContext db, IObjectStorage storage, ILogger<OrphanFileCleaner> log) : IOrphanFileCleaner
{
    // {businessId:N}/{assetId:N}/{variant}.webp, exactly what MediaVariants.KeyFor produces. Anything else is left alone.
    [GeneratedRegex(@"^([0-9a-f]{32})/([0-9a-f]{32})/(orig|md|sm)\.webp$")]
    private static partial Regex MediaKey();

    // {businessId:N}/d/{documentId:N}.bin, exactly what DocumentKeys.KeyFor produces.
    [GeneratedRegex(@"^([0-9a-f]{32})/d/([0-9a-f]{32})\.bin$")]
    private static partial Regex DocumentKey();

    private const int BatchSize = 200;

    private readonly record struct Candidate(string Key, Guid BusinessId, Guid OwnerId, bool IsDocument);

    public async Task<OrphanCleanupResult> RunAsync(TimeSpan grace, int maxDeletes, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var cutoff = (nowUtc ?? DateTime.UtcNow) - grace;
        int scanned = 0, deleted = 0, skipped = 0;
        var batch = new List<Candidate>();

        async Task FlushAsync()
        {
            if (batch.Count == 0) return;
            // No tenant is in scope in a background job, so the filters are bypassed on purpose; every row is matched on (Id, BusinessId).
            var mediaIds = batch.Where(b => !b.IsDocument).Select(b => b.OwnerId).Distinct().ToList();
            var docIds = batch.Where(b => b.IsDocument).Select(b => b.OwnerId).Distinct().ToList();
            var liveMedia = (await db.MediaAssets.IgnoreQueryFilters().AsNoTracking()
                .Where(a => mediaIds.Contains(a.Id)).Select(a => new { a.Id, a.BusinessId }).ToListAsync(ct))
                .Select(a => (a.Id, a.BusinessId)).ToHashSet();
            var liveDocs = (await db.Documents.IgnoreQueryFilters().AsNoTracking()
                .Where(d => docIds.Contains(d.Id)).Select(d => new { d.Id, d.BusinessId }).ToListAsync(ct))
                .Select(d => (d.Id, d.BusinessId)).ToHashSet();

            foreach (var c in batch)
            {
                if ((c.IsDocument ? liveDocs : liveMedia).Contains((c.OwnerId, c.BusinessId))) continue;
                if (deleted >= maxDeletes) { skipped++; continue; }
                await storage.DeleteAsync(c.Key, ct);
                deleted++;
                log.LogInformation("Deleted orphaned file {Key}", c.Key);
            }
            batch.Clear();
        }

        await foreach (var obj in storage.ListAsync("", ct))
        {
            scanned++;
            if (obj.LastModifiedUtc > cutoff) { skipped++; continue; }
            var m = MediaKey().Match(obj.Key);
            var isDoc = false;
            if (!m.Success) { m = DocumentKey().Match(obj.Key); isDoc = m.Success; }
            if (!m.Success) { skipped++; continue; } // unknown shape: never deleted
            batch.Add(new Candidate(obj.Key, Guid.ParseExact(m.Groups[1].Value, "N"), Guid.ParseExact(m.Groups[2].Value, "N"), isDoc));
            if (batch.Count >= BatchSize) await FlushAsync();
        }
        await FlushAsync();
        return new OrphanCleanupResult(scanned, deleted, skipped);
    }
}

public sealed class OrphanCleanupWorker(IServiceScopeFactory scopes, IOptions<StorageOptions> options, ILogger<OrphanCleanupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value.OrphanCleanup;
        if (!o.Enabled) { log.LogInformation("Orphan file cleanup disabled on this host."); return; }
        var interval = TimeSpan.FromMinutes(Math.Max(5, o.IntervalMinutes));
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var r = await scope.ServiceProvider.GetRequiredService<IOrphanFileCleaner>()
                    .RunAsync(TimeSpan.FromHours(Math.Max(1, o.GraceHours)), Math.Max(1, o.MaxDeletesPerRun), null, stoppingToken);
                log.LogInformation("Orphan cleanup: scanned {Scanned}, deleted {Deleted}, skipped {Skipped}.", r.Scanned, r.Deleted, r.Skipped);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Orphan cleanup failed; will retry next interval."); }

            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
