using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Tailoring.Infrastructure.Coordination;

/// <summary>At-least-once Redis Streams queue. Handlers must be idempotent because a worker can crash after side effects but before ACK.</summary>
public interface IRedisJobQueue
{
    Task<string> EnqueueAsync(string jobType, string payloadJson, string? idempotencyKey = null, CancellationToken ct = default);
}

public interface IRedisJobHandler
{
    string JobType { get; }
    Task HandleAsync(string payloadJson, CancellationToken ct);
}

public sealed class RedisJobQueue(IConnectionMultiplexer redis) : IRedisJobQueue
{
    public const string Stream = "avzar:jobs:pending";
    private const string IdempotencyPrefix = "avzar:jobs:idem:";
    private const string EnqueueScript = "if ARGV[3] ~= '' then local existing = redis.call('GET', KEYS[2]); if existing then return existing end end; local id = redis.call('XADD', KEYS[1], '*', 'type', ARGV[1], 'payload', ARGV[2], 'attempt', '0'); if ARGV[3] ~= '' then redis.call('SET', KEYS[2], id, 'EX', ARGV[4]) end; return id";

    public async Task<string> EnqueueAsync(string jobType, string payloadJson, string? idempotencyKey = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(jobType) || jobType.Length > 120 || jobType.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException("Job type must be a 1-120 character identifier.", nameof(jobType));
        if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson.Length > 256_000)
            throw new ArgumentException("Job payload must be non-empty and at most 256 KB.", nameof(payloadJson));
        using var document = JsonDocument.Parse(payloadJson);
        var idem = string.IsNullOrWhiteSpace(idempotencyKey) ? "" : idempotencyKey.Trim();
        if (idem.Length > 200) throw new ArgumentException("Idempotency key is too long.", nameof(idempotencyKey));
        var result = await redis.GetDatabase().ScriptEvaluateAsync(EnqueueScript,
            [ (RedisKey)Stream, (RedisKey)(IdempotencyPrefix + idem) ],
            [ (RedisValue)jobType, (RedisValue)payloadJson, (RedisValue)idem, (RedisValue)86400 ]);
        return (string)result!;
    }
}

/// <summary>Processes a consumer group, retries transient failures, and moves exhausted/unknown jobs to a dead-letter stream.</summary>
public sealed class RedisJobWorker(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopes,
    ILogger<RedisJobWorker> logger) : BackgroundService
{
    private const string Group = "avzar-workers-v1";
    private const string DeadLetter = "avzar:jobs:dead";
    private const int MaxAttempts = 5;
    private static readonly TimeSpan ClaimAfter = TimeSpan.FromMinutes(2);
    private readonly string _consumer = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                try { await db.StreamCreateConsumerGroupAsync(RedisJobQueue.Stream, Group, "0-0", createStream: true); }
                catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase)) { }

                // Recover abandoned jobs from crashed consumers. The server returns only entries idle beyond ClaimAfter.
                var pending = await db.StreamPendingMessagesAsync(RedisJobQueue.Stream, Group, 20, minId: "-", maxId: "+");
                foreach (var p in pending.Where(p => p.IdleTimeInMilliseconds >= ClaimAfter.TotalMilliseconds))
                {
                    var claimed = await db.StreamClaimAsync(RedisJobQueue.Stream, Group, _consumer, ClaimAfter, [p.Id]);
                    await ProcessAsync(db, claimed, stoppingToken);
                }

                var entries = await db.StreamReadGroupAsync(RedisJobQueue.Stream, Group, _consumer, ">", count: 10);
                if (entries.Length == 0) { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); continue; }
                await ProcessAsync(db, entries, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Redis job worker loop failed; retrying after a short delay.");
                try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    private async Task ProcessAsync(IDatabase db, StreamEntry[] entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var fields = entry.Values.ToDictionary(v => (string)v.Name!, v => (string)v.Value!, StringComparer.Ordinal);
            fields.TryGetValue("type", out var type);
            fields.TryGetValue("payload", out var payload);
            _ = int.TryParse(fields.GetValueOrDefault("attempt"), out var attempt);
            try
            {
                using var scope = scopes.CreateScope();
                var handler = scope.ServiceProvider.GetServices<IRedisJobHandler>().FirstOrDefault(h => h.JobType == type)
                    ?? throw new InvalidOperationException($"No handler registered for job type '{type}'.");
                await handler.HandleAsync(payload ?? "{}", ct);
                await db.StreamAcknowledgeAsync(RedisJobQueue.Stream, Group, entry.Id);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                attempt++;
                logger.LogWarning(ex, "Job {JobId} ({JobType}) failed on attempt {Attempt}/{MaxAttempts}.", entry.Id, type, attempt, MaxAttempts);
                if (attempt >= MaxAttempts)
                {
                    await db.StreamAddAsync(DeadLetter, [ new NameValueEntry("originalId", entry.Id), new NameValueEntry("type", type ?? ""), new NameValueEntry("payload", payload ?? "{}"), new NameValueEntry("attempt", attempt), new NameValueEntry("error", ex.GetType().Name), new NameValueEntry("failedUtc", DateTimeOffset.UtcNow.ToString("O")) ]);
                    await db.StreamAcknowledgeAsync(RedisJobQueue.Stream, Group, entry.Id);
                }
                else
                {
                    // Add the retry before acknowledging the old entry. A crash between these operations can duplicate work,
                    // but never silently lose it; handlers therefore must enforce their own idempotency.
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt))), ct);
                    await db.StreamAddAsync(RedisJobQueue.Stream, [ new NameValueEntry("type", type ?? ""), new NameValueEntry("payload", payload ?? "{}"), new NameValueEntry("attempt", attempt) ]);
                    await db.StreamAcknowledgeAsync(RedisJobQueue.Stream, Group, entry.Id);
                }
            }
        }
    }
}
