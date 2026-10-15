using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tailoring.Application.Media;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Media;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Tailoring.Infrastructure.Storage;
using Xunit;

namespace Tailoring.IntegrationTests;

public sealed class S3StorageConfigTests
{
    private static S3StorageOptions Valid() => new() { Bucket = "b", AccessKey = "k", SecretKey = "s", Region = "auto", ServiceUrl = "https://example.r2.cloudflarestorage.com" };

    [Fact] public void A_complete_config_is_accepted() => Valid().Validate();

    [Theory]
    [InlineData("Bucket")]
    [InlineData("AccessKey")]
    [InlineData("SecretKey")]
    [InlineData("Region")]
    public void Missing_required_values_fail_at_startup(string field)
    {
        var o = Valid();
        switch (field) { case "Bucket": o.Bucket = ""; break; case "AccessKey": o.AccessKey = " "; break; case "SecretKey": o.SecretKey = ""; break; default: o.Region = ""; break; }
        Assert.Throws<InvalidOperationException>(o.Validate);
    }

    [Theory]
    [InlineData("/lead")]
    [InlineData("trail/")]
    [InlineData("a/../b")]
    [InlineData("a//b")]
    public void Unsafe_key_prefixes_are_refused(string prefix)
    {
        var o = Valid(); o.KeyPrefix = prefix;
        Assert.Throws<InvalidOperationException>(o.Validate);
    }

    [Fact]
    public void A_bad_service_url_is_refused()
    {
        var o = Valid(); o.ServiceUrl = "not a url";
        Assert.Throws<InvalidOperationException>(o.Validate);
    }

    [Theory]
    [InlineData("../x.webp")]
    [InlineData("/etc/passwd")]
    [InlineData("a//b.webp")]
    [InlineData("")]
    public async Task Bad_keys_are_rejected_before_any_network_call(string key)
    {
        using var s = new S3ObjectStorage(Options.Create(new StorageOptions { Provider = "s3", S3 = Valid() }));
        await Assert.ThrowsAsync<ArgumentException>(() => s.PutAsync(key, new byte[] { 1 }, "image/webp"));
        await Assert.ThrowsAsync<ArgumentException>(() => s.OpenReadAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => s.DeleteAsync(key));
    }
}

public sealed class LocalDiskListingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "avzar-list-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact]
    public async Task Lists_by_prefix_with_forward_slash_keys_and_sizes()
    {
        var s = new LocalDiskObjectStorage(Options.Create(new StorageOptions { LocalRoot = _root }));
        await s.PutAsync("aaa/one/orig.webp", new byte[] { 1, 2, 3 }, "image/webp");
        await s.PutAsync("aaa/two/orig.webp", new byte[] { 1 }, "image/webp");
        await s.PutAsync("bbb/three/orig.webp", new byte[] { 1 }, "image/webp");

        var all = new List<StoredObject>();
        await foreach (var o in s.ListAsync("")) all.Add(o);
        Assert.Equal(3, all.Count);

        var aaa = new List<StoredObject>();
        await foreach (var o in s.ListAsync("aaa/")) aaa.Add(o);
        Assert.Equal(new[] { "aaa/one/orig.webp", "aaa/two/orig.webp" }, aaa.Select(x => x.Key).OrderBy(x => x).ToArray());
        Assert.Equal(3, aaa.Single(x => x.Key == "aaa/one/orig.webp").SizeBytes);
        Assert.All(aaa, o => Assert.True((DateTime.UtcNow - o.LastModifiedUtc).TotalMinutes < 5));
    }

    [Fact]
    public async Task An_unsafe_prefix_is_refused()
    {
        var s = new LocalDiskObjectStorage(Options.Create(new StorageOptions { LocalRoot = _root }));
        await Assert.ThrowsAsync<ArgumentException>(async () => { await foreach (var _ in s.ListAsync("../")) { } });
    }
}

public sealed class OrphanCleanerTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Grace = TimeSpan.FromHours(24);

    private async Task<Guid> NewBusinessAsync()
    {
        await using var db = fx.Create(null);
        var p = await db.Plans.FirstAsync(x => x.Code == "professional");
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = p.Id });
        await db.SaveChangesAsync();
        return b.Id;
    }

    private async Task<MediaAssetDto> UploadAsync(Guid bid, InMemoryObjectStorage storage)
    {
        var o = Options.Create(new StorageOptions());
        var db = fx.Create(bid);
        var media = new MediaService(db, new FakeTenant(bid), new AuditLogger(db), new EntitlementService(db), new ImageSharpProcessor(o), storage, o);
        return await media.UploadAsync(new MemoryStream(TestImages.Png(200, 200)), "x.png", "DesignImage");
    }

    private OrphanFileCleaner Cleaner(InMemoryObjectStorage storage) => new(fx.Create(null), storage, NullLogger<OrphanFileCleaner>.Instance);

    private static void Put(InMemoryObjectStorage s, string key, DateTime modifiedUtc)
    { s.Objects[key] = new byte[] { 1 }; s.Modified[key] = modifiedUtc; }

    private static void Age(InMemoryObjectStorage s, IEnumerable<string> keys) { foreach (var k in keys) s.Modified[k] = DateTime.UtcNow.AddDays(-30); }

    [Fact]
    public async Task Removes_old_orphans_and_keeps_everything_else()
    {
        var bid = await NewBusinessAsync();
        var storage = new InMemoryObjectStorage();
        var live = await UploadAsync(bid, storage);
        var liveKeys = MediaVariants.All.Select(v => MediaVariants.KeyFor(bid, live.Id, v)).ToList();
        Age(storage, liveKeys); // old, but a row points at them

        var oldOrphan = MediaVariants.KeyFor(bid, Guid.NewGuid(), "orig");
        var youngOrphan = MediaVariants.KeyFor(bid, Guid.NewGuid(), "orig");
        const string unknownShape = "notes/readme.txt";
        Put(storage, oldOrphan, DateTime.UtcNow.AddDays(-3));
        Put(storage, youngOrphan, DateTime.UtcNow.AddMinutes(-10));
        Put(storage, unknownShape, DateTime.UtcNow.AddDays(-30));

        var r = await Cleaner(storage).RunAsync(Grace, 100);

        Assert.Equal(1, r.Deleted);
        Assert.False(storage.Objects.ContainsKey(oldOrphan));
        Assert.True(storage.Objects.ContainsKey(youngOrphan), "in-flight uploads must be protected by the grace period");
        Assert.True(storage.Objects.ContainsKey(unknownShape), "keys of unknown shape are never deleted");
        Assert.All(liveKeys, k => Assert.True(storage.Objects.ContainsKey(k), k));
    }

    [Fact]
    public async Task A_file_under_the_wrong_business_is_an_orphan_even_if_the_asset_id_exists()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var storage = new InMemoryObjectStorage();
        var asset = await UploadAsync(a, storage);
        var wrongTenantKey = MediaVariants.KeyFor(b, asset.Id, "orig");
        Put(storage, wrongTenantKey, DateTime.UtcNow.AddDays(-3));

        var r = await Cleaner(storage).RunAsync(Grace, 100);

        Assert.Equal(1, r.Deleted);
        Assert.False(storage.Objects.ContainsKey(wrongTenantKey));
        Assert.True(storage.Objects.ContainsKey(MediaVariants.KeyFor(a, asset.Id, "orig")));
    }

    [Fact]
    public async Task The_per_run_cap_limits_deletions_and_a_second_run_finishes_the_job()
    {
        var bid = await NewBusinessAsync();
        var storage = new InMemoryObjectStorage();
        var keys = Enumerable.Range(0, 5).Select(_ => MediaVariants.KeyFor(bid, Guid.NewGuid(), "sm")).ToList();
        foreach (var k in keys) Put(storage, k, DateTime.UtcNow.AddDays(-5));

        var first = await Cleaner(storage).RunAsync(Grace, 3);
        Assert.Equal(3, first.Deleted);
        Assert.Equal(2, storage.Objects.Count);

        var second = await Cleaner(storage).RunAsync(Grace, 3);
        Assert.Equal(2, second.Deleted);
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task Running_twice_is_harmless()
    {
        var bid = await NewBusinessAsync();
        var storage = new InMemoryObjectStorage();
        var live = await UploadAsync(bid, storage);
        Age(storage, MediaVariants.All.Select(v => MediaVariants.KeyFor(bid, live.Id, v)));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Cleaner(storage).RunAsync(Grace, 100)));

        Assert.Equal(3, storage.Objects.Count(kv => kv.Key.StartsWith(bid.ToString("N"))));
    }
}
