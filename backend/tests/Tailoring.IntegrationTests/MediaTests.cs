using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Tailoring.Application.Common;
using Tailoring.Application.Designs;
using Tailoring.Application.Media;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Media;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Services;
using Tailoring.Infrastructure.Storage;
using Xunit;

namespace Tailoring.IntegrationTests;

internal static class TestImages
{
    public static byte[] Png(int w, int h)
    {
        using var img = new Image<Rgba32>(w, h, Color.SteelBlue);
        using var ms = new MemoryStream(); img.SaveAsPng(ms); return ms.ToArray();
    }

    public static byte[] JpegWithExif(int w, int h)
    {
        using var img = new Image<Rgb24>(w, h, Color.Orange);
        img.Metadata.ExifProfile = new ExifProfile();
        img.Metadata.ExifProfile.SetValue(ExifTag.Software, "SecretCam 9000");
        using var ms = new MemoryStream(); img.SaveAsJpeg(ms); return ms.ToArray();
    }

    public static byte[] Gif()
    {
        using var img = new Image<Rgba32>(8, 8, Color.Red);
        using var ms = new MemoryStream(); img.SaveAsGif(ms); return ms.ToArray();
    }
}

internal sealed class InMemoryObjectStorage : IObjectStorage
{
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
    public Task PutAsync(string key, ReadOnlyMemory<byte> data, string contentType, CancellationToken ct = default)
    { Objects[key] = data.ToArray(); return Task.CompletedTask; }
    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
        => Task.FromResult<Stream?>(Objects.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    public Task DeleteAsync(string key, CancellationToken ct = default) { Objects.TryRemove(key, out _); return Task.CompletedTask; }
}

internal sealed class FailsOnSecondPut(IObjectStorage inner) : IObjectStorage
{
    private int _puts;
    public Task PutAsync(string key, ReadOnlyMemory<byte> data, string contentType, CancellationToken ct = default)
        => ++_puts == 2 ? throw new IOException("disk full") : inner.PutAsync(key, data, contentType, ct);
    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => inner.OpenReadAsync(key, ct);
    public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(key, ct);
}

/// <summary>No database needed.</summary>
public sealed class ImageProcessorTests
{
    private static ImageSharpProcessor Proc(long maxPixels = 50_000_000)
        => new(Options.Create(new StorageOptions { MaxPixels = maxPixels }));

    [Fact]
    public async Task Produces_three_webp_variants_within_their_size_caps()
    {
        var result = await Proc().ProcessAsync(TestImages.Png(3000, 2000));
        Assert.Equal(new[] { "md", "orig", "sm" }, result.Variants.Keys.Order().ToArray());
        foreach (var (variant, cap) in new[] { ("orig", 2560), ("md", 960), ("sm", 320) })
        {
            var bytes = result.Variants[variant];
            Assert.Equal("WEBP", Image.DetectFormat(bytes).Name);
            var info = Image.Identify(bytes);
            Assert.True(Math.Max(info.Width, info.Height) <= cap, variant);
        }
        Assert.Equal(2560, result.Width);
        Assert.Equal(1707, result.Height); // aspect ratio kept
    }

    [Fact]
    public async Task Never_upscales_small_images()
    {
        var result = await Proc().ProcessAsync(TestImages.Png(100, 50));
        Assert.Equal((100, 50), (result.Width, result.Height));
        Assert.Equal(100, Image.Identify(result.Variants["md"]).Width);
    }

    [Fact]
    public async Task Strips_exif_metadata()
    {
        var source = TestImages.JpegWithExif(64, 64);
        Assert.NotNull(Image.Identify(source).Metadata.ExifProfile); // sanity: the input really had EXIF
        var result = await Proc().ProcessAsync(source);
        foreach (var bytes in result.Variants.Values)
        {
            var exif = Image.Identify(bytes).Metadata.ExifProfile;
            Assert.True(exif is null || !exif.TryGetValue(ExifTag.Software, out _));
        }
    }

    [Fact]
    public async Task Bytes_hidden_after_a_valid_image_are_not_carried_into_the_output()
    {
        var payload = System.Text.Encoding.ASCII.GetBytes("<script>alert(1)</script>");
        var polyglot = TestImages.Png(32, 32).Concat(payload).ToArray();
        var result = await Proc().ProcessAsync(polyglot);
        foreach (var bytes in result.Variants.Values)
            Assert.DoesNotContain("<script>", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public async Task Rejects_everything_that_is_not_jpeg_png_or_webp_whatever_it_is_called()
    {
        var samples = new Dictionary<string, byte[]>
        {
            ["text"] = System.Text.Encoding.UTF8.GetBytes("hello, I am not an image"),
            ["svg"] = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"),
            ["html"] = System.Text.Encoding.UTF8.GetBytes("<html><body onload=alert(1)>"),
            ["gif"] = TestImages.Gif(),
            ["truncated png"] = TestImages.Png(50, 50)[..20],
            ["empty"] = [],
        };
        foreach (var (name, bytes) in samples)
        {
            var ex = await Assert.ThrowsAsync<AppException>(() => Proc().ProcessAsync(bytes));
            Assert.True(ex.Code == "unsupported_image", $"{name}: {ex.Code}");
        }
    }

    [Fact]
    public async Task Rejects_images_over_the_pixel_cap_before_decoding_them()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => Proc(maxPixels: 100).ProcessAsync(TestImages.Png(20, 20)));
        Assert.Equal("image_too_large", ex.Code);
    }
}

public sealed class LocalDiskObjectStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "avzar-test-" + Guid.NewGuid().ToString("N"));
    private LocalDiskObjectStorage Make() => new(Options.Create(new StorageOptions { LocalRoot = _root }));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact]
    public async Task Round_trips_and_deletes()
    {
        var s = Make();
        await s.PutAsync("aaaa/bbbb/orig.webp", new byte[] { 1, 2, 3 }, "image/webp");
        await using (var read = await s.OpenReadAsync("aaaa/bbbb/orig.webp"))
        {
            Assert.NotNull(read);
            using var ms = new MemoryStream(); await read!.CopyToAsync(ms);
            Assert.Equal(new byte[] { 1, 2, 3 }, ms.ToArray());
        }
        await s.DeleteAsync("aaaa/bbbb/orig.webp");
        Assert.Null(await s.OpenReadAsync("aaaa/bbbb/orig.webp"));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories)); // no temp leftovers
    }

    [Theory]
    [InlineData("../escape.webp")]
    [InlineData("a/../../escape.webp")]
    [InlineData("/etc/passwd")]
    [InlineData("a//b.webp")]
    [InlineData("a\\b.webp")]
    [InlineData("a/./b.webp")]
    [InlineData("")]
    [InlineData("a/b c.webp")]
    public async Task Refuses_keys_that_could_leave_the_root(string key)
    {
        var s = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => s.PutAsync(key, new byte[] { 1 }, "image/webp"));
        await Assert.ThrowsAsync<ArgumentException>(() => s.OpenReadAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => s.DeleteAsync(key));
    }
}

public sealed class MediaTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private readonly InMemoryObjectStorage _storage = new();

    private async Task<Guid> NewBusinessAsync(string plan = "professional")
    {
        await using var db = fx.Create(null);
        var p = await db.Plans.FirstAsync(x => x.Code == plan);
        var b = new Business { Name = "Biz-" + Guid.NewGuid().ToString("N")[..6], CurrencyCode = "NGN", TimeZoneId = "UTC" };
        db.Businesses.Add(b);
        db.Subscriptions.Add(new TenantSubscription { BusinessId = b.Id, PlanId = p.Id });
        await db.SaveChangesAsync();
        return b.Id;
    }

    private MediaService Media(Guid? bid, IObjectStorage? storage = null, StorageOptions? opts = null)
    {
        var o = Options.Create(opts ?? new StorageOptions());
        var db = fx.Create(bid);
        return new MediaService(db, new FakeTenant(bid), new AuditLogger(db), new EntitlementService(db),
            new ImageSharpProcessor(o), storage ?? _storage, o);
    }

    private DesignService Designs(Guid bid)
    { var db = fx.Create(bid); return new DesignService(db, new FakeTenant(bid), new AuditLogger(db), new EntitlementService(db)); }

    private Task<MediaAssetDto> Upload(Guid bid, byte[]? bytes = null, string name = "dress.png", IObjectStorage? storage = null, StorageOptions? opts = null)
        => Media(bid, storage, opts).UploadAsync(new MemoryStream(bytes ?? TestImages.Png(400, 300)), name, "DesignImage");

    private static IEnumerable<string> Keys(Guid bid, Guid id) => MediaVariants.All.Select(v => MediaVariants.KeyFor(bid, id, v));

    [Fact]
    public async Task Upload_stores_three_variants_and_a_row()
    {
        var bid = await NewBusinessAsync();
        var dto = await Upload(bid);
        Assert.Equal((400, 300), (dto.Width, dto.Height));
        Assert.Equal("DesignImage", dto.Purpose);
        Assert.All(Keys(bid, dto.Id), k => Assert.True(_storage.Objects.ContainsKey(k), k));
        Assert.Equal(Keys(bid, dto.Id).Sum(k => (long)_storage.Objects[k].Length), dto.SizeBytes);
        Assert.Single((await Media(bid).ListAsync(new MediaListQuery())).Items);
    }

    [Fact]
    public async Task A_rejected_file_stores_nothing()
    {
        var bid = await NewBusinessAsync();
        var before = _storage.Objects.Count;
        var ex = await Assert.ThrowsAsync<AppException>(() => Upload(bid, System.Text.Encoding.UTF8.GetBytes("<svg onload=alert(1)>"), "logo.png"));
        Assert.Equal("unsupported_image", ex.Code);
        Assert.Equal(before, _storage.Objects.Count);
        Assert.Empty((await Media(bid).ListAsync(new MediaListQuery())).Items);
    }

    [Fact]
    public async Task Oversized_uploads_are_refused_with_413()
    {
        var bid = await NewBusinessAsync();
        var ex = await Assert.ThrowsAsync<AppException>(() => Upload(bid, TestImages.Png(400, 300), opts: new StorageOptions { MaxUploadBytes = 100 }));
        Assert.Equal(413, ex.StatusCode);
    }

    [Fact]
    public async Task File_names_are_sanitised_and_never_become_paths()
    {
        var bid = await NewBusinessAsync();
        var dto = await Upload(bid, name: "..\\..\\etc/evil\u0000.png");
        Assert.Equal("evil.png", dto.FileName);
        Assert.All(_storage.Objects.Keys.Where(k => k.StartsWith(bid.ToString("N"))), k => Assert.DoesNotContain("evil", k));
    }

    [Fact]
    public async Task A_failed_write_leaves_no_files_and_no_row()
    {
        var bid = await NewBusinessAsync();
        var storage = new InMemoryObjectStorage();
        await Assert.ThrowsAsync<IOException>(() => Upload(bid, storage: new FailsOnSecondPut(storage)));
        Assert.Empty(storage.Objects);
        Assert.Empty((await Media(bid).ListAsync(new MediaListQuery())).Items);
    }

    [Fact]
    public async Task Storage_limit_is_enforced_and_deleting_frees_space()
    {
        var bid = await NewBusinessAsync("free");
        var first = await Upload(bid); // measure with the normal 1 GB quota
        var tight = new StorageOptions { QuotaBytesPerGb = first.SizeBytes + first.SizeBytes / 2 }; // 1 "GB" = 1.5 uploads

        var ex = await Assert.ThrowsAsync<AppException>(() => Upload(bid, opts: tight));
        Assert.Equal("plan_limit_reached", ex.Code);

        var usage = await Media(bid, opts: tight).GetUsageAsync();
        Assert.Equal(first.SizeBytes, usage.UsedBytes);
        Assert.Equal(tight.QuotaBytesPerGb, usage.LimitBytes);

        await Media(bid).DeleteAsync(first.Id);
        await Upload(bid, opts: tight); // fits again
    }

    [Fact]
    public async Task Another_business_cannot_see_open_or_delete_an_image()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var dto = await Upload(a);

        Assert.Equal("media_not_found", (await Assert.ThrowsAsync<AppException>(() => Media(b).GetAsync(dto.Id))).Code);
        Assert.Equal("media_not_found", (await Assert.ThrowsAsync<AppException>(() => Media(b).OpenAsync(dto.Id, "md"))).Code);
        Assert.Equal("media_not_found", (await Assert.ThrowsAsync<AppException>(() => Media(b).DeleteAsync(dto.Id))).Code);
        Assert.Empty((await Media(b).ListAsync(new MediaListQuery())).Items);
        await using var stream = (await Media(a).OpenAsync(dto.Id, "md")).Content; // the owner still can
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task Unknown_variants_are_not_found()
    {
        var bid = await NewBusinessAsync();
        var dto = await Upload(bid);
        await Assert.ThrowsAsync<AppException>(() => Media(bid).OpenAsync(dto.Id, "../orig"));
        await Assert.ThrowsAsync<AppException>(() => Media(bid).OpenAsync(dto.Id, "huge"));
    }

    [Fact]
    public async Task An_image_in_use_cannot_be_deleted_and_deleting_removes_every_stored_variant()
    {
        var bid = await NewBusinessAsync();
        var img = await Upload(bid);
        var design = await Designs(bid).CreateAsync(new UpsertDesignRequest { Name = "Gown", Kind = "MadeToOrder" });
        await Designs(bid).SetImagesAsync(design.Id, new SetDesignImagesRequest { Images = [new DesignImageInput { MediaId = img.Id }] });

        Assert.True((await Media(bid).GetAsync(img.Id)).InUse);
        Assert.Equal("media_in_use", (await Assert.ThrowsAsync<AppException>(() => Media(bid).DeleteAsync(img.Id))).Code);

        await Designs(bid).SetImagesAsync(design.Id, new SetDesignImagesRequest());
        await Media(bid).DeleteAsync(img.Id);
        Assert.All(Keys(bid, img.Id), k => Assert.False(_storage.Objects.ContainsKey(k), k));
    }

    [Fact]
    public async Task Public_serving_only_works_for_images_on_public_active_designs()
    {
        var bid = await NewBusinessAsync();
        var img = await Upload(bid);
        var design = await Designs(bid).CreateAsync(new UpsertDesignRequest { Name = "Agbada", Kind = "ReadyMade" });
        await Designs(bid).SetImagesAsync(design.Id, new SetDesignImagesRequest { Images = [new DesignImageInput { MediaId = img.Id }] });

        async Task<string?> TryPublic()
        {
            try { await using var c = (await Media(null).OpenPublicAsync(img.Id, "md")).Content; return null; }
            catch (AppException ex) { return ex.Code; }
        }

        Assert.Equal("media_not_found", await TryPublic());          // private design
        await Designs(bid).SetVisibilityAsync(design.Id, new SetDesignVisibilityRequest { Visibility = "Public" });
        Assert.Null(await TryPublic());                               // public + active
        await Designs(bid).SetStatusAsync(design.Id, new SetDesignStatusRequest { Status = "Archived" });
        Assert.Equal("media_not_found", await TryPublic());          // archived is private again

        var orphan = await Upload(bid);                               // uploaded but attached to nothing
        await Assert.ThrowsAsync<AppException>(() => Media(null).OpenPublicAsync(orphan.Id, "md"));
    }

    [Fact]
    public async Task Gallery_keeps_order_cover_and_alt_text_and_can_be_replaced()
    {
        var bid = await NewBusinessAsync();
        var (a, b, c) = (await Upload(bid), await Upload(bid), await Upload(bid));
        var design = await Designs(bid).CreateAsync(new UpsertDesignRequest { Name = "Suit", Kind = "MadeToOrder" });

        var set = await Designs(bid).SetImagesAsync(design.Id, new SetDesignImagesRequest
        {
            Images = [new DesignImageInput { MediaId = b.Id, AltText = " Front view " }, new DesignImageInput { MediaId = a.Id }]
        });
        Assert.Equal(new[] { b.Id, a.Id }, set.Images.Select(i => i.MediaId));
        Assert.Equal("Front view", set.Images[0].AltText);
        Assert.Equal(b.Id, (await Designs(bid).ListAsync(new DesignListQuery())).Items.Single().CoverMediaId);

        var replaced = await Designs(bid).SetImagesAsync(design.Id, new SetDesignImagesRequest
        {
            Images = [new DesignImageInput { MediaId = c.Id }, new DesignImageInput { MediaId = a.Id }]
        });
        Assert.Equal(new[] { c.Id, a.Id }, replaced.Images.Select(i => i.MediaId));
        Assert.False((await Media(bid).GetAsync(b.Id)).InUse);
    }

    [Fact]
    public async Task Gallery_rejects_duplicates_unknown_and_other_tenants_images_and_archived_designs()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var mine = await Upload(a); var theirs = await Upload(b);
        var design = await Designs(a).CreateAsync(new UpsertDesignRequest { Name = "Cape", Kind = "MadeToOrder" });

        Assert.Equal("duplicate_image", (await Assert.ThrowsAsync<AppException>(() => Designs(a).SetImagesAsync(design.Id,
            new SetDesignImagesRequest { Images = [new DesignImageInput { MediaId = mine.Id }, new DesignImageInput { MediaId = mine.Id }] }))).Code);
        Assert.Equal("unknown_image", (await Assert.ThrowsAsync<AppException>(() => Designs(a).SetImagesAsync(design.Id,
            new SetDesignImagesRequest { Images = [new DesignImageInput { MediaId = theirs.Id }] }))).Code);
        Assert.Equal("unknown_image", (await Assert.ThrowsAsync<AppException>(() => Designs(a).SetImagesAsync(design.Id,
            new SetDesignImagesRequest { Images = [new DesignImageInput { MediaId = Guid.NewGuid() }] }))).Code);

        await Designs(a).SetStatusAsync(design.Id, new SetDesignStatusRequest { Status = "Archived" });
        Assert.Equal("design_archived", (await Assert.ThrowsAsync<AppException>(() => Designs(a).SetImagesAsync(design.Id,
            new SetDesignImagesRequest { Images = [new DesignImageInput { MediaId = mine.Id }] }))).Code);
    }
}
