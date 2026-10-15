using System.Threading.RateLimiting;
using Tailoring.Infrastructure.Coordination;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>No Redis server needed: the counter is a fake. Talking to a real Redis is blocked by external configuration.</summary>
public sealed class RedisLimiterTests
{
    private sealed class FakeCounter : IRedisCounter
    {
        private readonly Dictionary<string, long> _counts = new();
        public bool Broken { get; set; }
        public (long, long) Hit(string key, int permits, int windowSeconds)
        {
            if (Broken) throw new InvalidOperationException("redis down");
            lock (_counts) { _counts[key] = _counts.GetValueOrDefault(key) + permits; return (_counts[key], 42); }
        }
        public Task<(long, long)> HitAsync(string key, int permits, int windowSeconds) => Task.FromResult(Hit(key, permits, windowSeconds));
    }

    [Fact]
    public void Allows_up_to_the_limit_then_refuses_with_a_retry_after()
    {
        using var limiter = new RedisFixedWindowLimiter(new FakeCounter(), "k", permitLimit: 3, TimeSpan.FromMinutes(1));
        for (var i = 0; i < 3; i++) Assert.True(limiter.AttemptAcquire(1).IsAcquired);

        var denied = limiter.AttemptAcquire(1);
        Assert.False(denied.IsAcquired);
        Assert.True(denied.TryGetMetadata(MetadataName.RetryAfter, out var retry));
        Assert.Equal(TimeSpan.FromSeconds(42), retry);
    }

    [Fact]
    public async Task Async_path_counts_the_same_way()
    {
        using var limiter = new RedisFixedWindowLimiter(new FakeCounter(), "k", 1, TimeSpan.FromMinutes(1));
        Assert.True((await limiter.AcquireAsync(1)).IsAcquired);
        Assert.False((await limiter.AcquireAsync(1)).IsAcquired);
    }

    [Fact]
    public void Two_limiters_sharing_one_counter_share_the_limit_like_two_replicas()
    {
        var shared = new FakeCounter();
        using var replicaA = new RedisFixedWindowLimiter(shared, "same-key", 2, TimeSpan.FromMinutes(1));
        using var replicaB = new RedisFixedWindowLimiter(shared, "same-key", 2, TimeSpan.FromMinutes(1));
        Assert.True(replicaA.AttemptAcquire(1).IsAcquired);
        Assert.True(replicaB.AttemptAcquire(1).IsAcquired);
        Assert.False(replicaA.AttemptAcquire(1).IsAcquired);
        Assert.False(replicaB.AttemptAcquire(1).IsAcquired);
    }

    [Fact]
    public void When_redis_fails_the_per_process_limit_still_applies_instead_of_failing_requests_or_opening_the_gate()
    {
        using var limiter = new RedisFixedWindowLimiter(new FakeCounter { Broken = true }, "k", 2, TimeSpan.FromMinutes(1));
        Assert.True(limiter.AttemptAcquire(1).IsAcquired);
        Assert.True(limiter.AttemptAcquire(1).IsAcquired);
        Assert.False(limiter.AttemptAcquire(1).IsAcquired);
    }

    [Fact]
    public void Without_redis_a_plain_in_process_partition_is_used()
    {
        var p = RateLimitPolicies.Fixed(null, "auth", "1.2.3.4", 5, TimeSpan.FromMinutes(1));
        Assert.Equal("1.2.3.4", p.PartitionKey);
    }
}
