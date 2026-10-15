using System.Threading.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Tailoring.Infrastructure.Coordination;

public sealed class RedisOptions
{
    /// <summary>Empty means Redis is not used and every feature below falls back to per-process behaviour.</summary>
    public string ConnectionString { get; set; } = "";
}

/// <summary>Atomic counter with expiry. A seam so the limiter can be tested without a Redis server.</summary>
public interface IRedisCounter
{
    (long Count, long TtlSeconds) Hit(string key, int permits, int windowSeconds);
    Task<(long Count, long TtlSeconds)> HitAsync(string key, int permits, int windowSeconds);
}

public sealed class RedisCounter(IConnectionMultiplexer redis) : IRedisCounter
{
    // INCRBY + EXPIRE in one script: a key can never be left without an expiry.
    private const string Script =
        "local c = redis.call('INCRBY', KEYS[1], ARGV[2]) if c == tonumber(ARGV[2]) then redis.call('EXPIRE', KEYS[1], ARGV[1]) end " +
        "local t = redis.call('TTL', KEYS[1]) return {c, t}";

    public (long, long) Hit(string key, int permits, int windowSeconds)
        => Read(redis.GetDatabase().ScriptEvaluate(Script, [(RedisKey)key], [(RedisValue)windowSeconds, (RedisValue)permits]));

    public async Task<(long, long)> HitAsync(string key, int permits, int windowSeconds)
        => Read(await redis.GetDatabase().ScriptEvaluateAsync(Script, [(RedisKey)key], [(RedisValue)windowSeconds, (RedisValue)permits]));

    private static (long, long) Read(RedisResult r) { var a = (RedisResult[])r!; return ((long)a[0], (long)a[1]); }
}

/// <summary>
/// Fixed-window limiter whose counter lives in Redis, so the limit holds across API replicas. If Redis is unreachable the request is
/// judged by a per-process limiter instead: the site keeps working and limits stay in force, just per instance.
/// </summary>
public sealed class RedisFixedWindowLimiter : RateLimiter
{
    private readonly IRedisCounter _counter;
    private readonly string _key;
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly FixedWindowRateLimiter _fallback;
    private readonly ILogger? _log;
    private DateTime _lastUsedUtc = DateTime.UtcNow;

    public RedisFixedWindowLimiter(IRedisCounter counter, string key, int permitLimit, TimeSpan window, ILogger? log = null)
    {
        (_counter, _key, _limit, _window, _log) = (counter, key, permitLimit, window, log);
        _fallback = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0, AutoReplenishment = true });
    }

    public override RateLimiterStatistics? GetStatistics() => null;
    public override TimeSpan? IdleDuration => DateTime.UtcNow - _lastUsedUtc;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        _lastUsedUtc = DateTime.UtcNow;
        try { var (count, ttl) = _counter.Hit(_key, permitCount, (int)Math.Ceiling(_window.TotalSeconds)); return Decide(count, ttl); }
        catch (Exception ex) { _log?.LogWarning(ex, "Redis rate limit unavailable; using the per-process limiter."); return _fallback.AttemptAcquire(permitCount); }
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        _lastUsedUtc = DateTime.UtcNow;
        try { var (count, ttl) = await _counter.HitAsync(_key, permitCount, (int)Math.Ceiling(_window.TotalSeconds)); return Decide(count, ttl); }
        catch (Exception ex) { _log?.LogWarning(ex, "Redis rate limit unavailable; using the per-process limiter."); return _fallback.AttemptAcquire(permitCount); }
    }

    private RateLimitLease Decide(long count, long ttlSeconds)
        => count <= _limit ? Lease.Granted : new Lease(false, TimeSpan.FromSeconds(Math.Max(1, ttlSeconds)));

    protected override void Dispose(bool disposing) { if (disposing) _fallback.Dispose(); base.Dispose(disposing); }

    private sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public static readonly Lease Granted = new(true, null);
        public override bool IsAcquired => acquired;
        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } r && metadataName == MetadataName.RetryAfter.Name) { metadata = r; return true; }
            metadata = null;
            return false;
        }
    }
}

/// <summary>Redis is optional, so an outage reports Degraded (the API still serves traffic) rather than Unhealthy.</summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try { var rtt = await redis.GetDatabase().PingAsync(); return HealthCheckResult.Healthy($"ping {rtt.TotalMilliseconds:0} ms"); }
        catch (Exception ex) { return HealthCheckResult.Degraded("Redis is unreachable; limits are per instance.", ex); }
    }
}

public static class RateLimitPolicies
{
    /// <summary>A fixed-window partition: shared through Redis when it is configured, per-process otherwise.</summary>
    public static RateLimitPartition<string> Fixed(IRedisCounter? redis, string policy, string partitionKey, int permitLimit, TimeSpan window)
        => redis is null
            ? RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window })
            : RateLimitPartition.Get(partitionKey, k => new RedisFixedWindowLimiter(redis, $"avzar:rl:{policy}:{k}", permitLimit, window));
}
