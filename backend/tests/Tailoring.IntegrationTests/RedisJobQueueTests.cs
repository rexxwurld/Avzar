using StackExchange.Redis;
using Tailoring.Infrastructure.Coordination;
using Testcontainers.Redis;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>
/// These tests use a real disposable Redis instance. They are intended to run in CI's
/// Docker-enabled test environment; a fake cannot prove Lua/Streams behavior.
/// </summary>
public sealed class RedisJobQueueTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();
    private IConnectionMultiplexer _connection = null!;
    private RedisJobQueue _queue = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _queue = new RedisJobQueue(_connection);
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task Unkeyed_jobs_are_each_appended_to_the_stream()
    {
        var first = await _queue.EnqueueAsync("email.send", "{\"id\":1}");
        var second = await _queue.EnqueueAsync("email.send", "{\"id\":2}");

        Assert.NotEqual(first, second);
        var entries = await _connection.GetDatabase().StreamRangeAsync(RedisJobQueue.Stream);
        Assert.Equal(2, entries.Length);
        Assert.Contains(entries, e => (string)e.Id! == first);
        Assert.Contains(entries, e => (string)e.Id! == second);
    }

    [Fact]
    public async Task Repeated_idempotency_key_returns_original_id_without_adding_an_entry()
    {
        var first = await _queue.EnqueueAsync("email.send", "{\"recipient\":\"one\"}", "welcome:account-42");
        var repeated = await _queue.EnqueueAsync("email.send", "{\"recipient\":\"changed\"}", "welcome:account-42");

        Assert.Equal(first, repeated);
        Assert.Single(await _connection.GetDatabase().StreamRangeAsync(RedisJobQueue.Stream));
    }

    [Fact]
    public async Task Different_idempotency_keys_create_distinct_jobs()
    {
        var first = await _queue.EnqueueAsync("email.send", "{}", "welcome:account-1");
        var second = await _queue.EnqueueAsync("email.send", "{}", "welcome:account-2");

        Assert.NotEqual(first, second);
        Assert.Equal(2, (await _connection.GetDatabase().StreamRangeAsync(RedisJobQueue.Stream)).Length);
    }

    [Theory]
    [InlineData("", "{}")]
    [InlineData("invalid job type", "{}")]
    [InlineData("email.send", "not-json")]
    [InlineData("email.send", "")]
    public async Task Invalid_job_type_or_payload_is_rejected_before_redis_write(string jobType, string payload)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _queue.EnqueueAsync(jobType, payload));
        Assert.Empty(await _connection.GetDatabase().StreamRangeAsync(RedisJobQueue.Stream));
    }
}
