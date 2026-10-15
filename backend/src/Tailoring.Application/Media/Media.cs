using System.ComponentModel.DataAnnotations;
using Tailoring.Application.Common;

namespace Tailoring.Application.Media;

public sealed record MediaAssetDto(Guid Id, string Purpose, string FileName, int Width, int Height, long SizeBytes, DateTime CreatedAtUtc, bool InUse);
public sealed record MediaUsageDto(long UsedBytes, long? LimitBytes);
public sealed record MediaListQuery : PagedQuery { public string? Purpose { get; init; } }

/// <summary>Caller owns and must dispose <see cref="Content"/>.</summary>
public sealed record MediaContent(Stream Content, string ContentType, long Length);

public sealed record ProcessedImage(int Width, int Height, IReadOnlyDictionary<string, byte[]> Variants);

/// <summary>Decodes, validates, strips metadata and re-encodes. Throws AppException(400) for anything it will not accept.</summary>
public interface IImageProcessor
{
    Task<ProcessedImage> ProcessAsync(byte[] data, CancellationToken ct = default);
}

/// <summary>Keys are opaque, server-generated strings. Implementations must refuse anything that could escape their root.</summary>
public interface IObjectStorage
{
    Task PutAsync(string key, ReadOnlyMemory<byte> data, string contentType, CancellationToken ct = default);
    /// <summary>Returns null when the object does not exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    /// <summary>Enumerates stored objects whose key starts with <paramref name="prefix"/> (empty = everything). Used by maintenance jobs only.</summary>
    IAsyncEnumerable<StoredObject> ListAsync(string prefix, CancellationToken ct = default);
}

public sealed record StoredObject(string Key, long SizeBytes, DateTime LastModifiedUtc);

public sealed record OrphanCleanupResult(int Scanned, int Deleted, int Skipped);

/// <summary>Deletes stored image files that no <c>MediaAsset</c> row points at (left behind by a crash between the file write and the DB commit).</summary>
public interface IOrphanFileCleaner
{
    /// <summary>Only files older than <paramref name="grace"/> are considered (in-flight uploads write files before the row commits). Keys of unknown shape are never deleted.</summary>
    Task<OrphanCleanupResult> RunAsync(TimeSpan grace, int maxDeletes, DateTime? nowUtc = null, CancellationToken ct = default);
}

public sealed class S3StorageOptions
{
    /// <summary>Custom endpoint for S3-compatible services (Cloudflare R2, MinIO, Backblaze B2, Wasabi...). Leave empty for AWS S3.</summary>
    public string ServiceUrl { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    /// <summary>Path-style addressing (bucket in the path). Required by MinIO and some others.</summary>
    public bool ForcePathStyle { get; set; }
    /// <summary>Optional folder inside the bucket, e.g. "avzar/prod". No leading or trailing slash.</summary>
    public string KeyPrefix { get; set; } = "";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Bucket)) throw new InvalidOperationException("Storage:S3:Bucket is required when Storage:Provider=s3.");
        if (string.IsNullOrWhiteSpace(AccessKey) || string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("Storage:S3:AccessKey and Storage:S3:SecretKey are required when Storage:Provider=s3 (set them as environment variables, never in source).");
        if (string.IsNullOrWhiteSpace(Region)) throw new InvalidOperationException("Storage:S3:Region is required.");
        if (!string.IsNullOrWhiteSpace(ServiceUrl) && !Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var u)) throw new InvalidOperationException("Storage:S3:ServiceUrl is not a valid URL.");
        if (KeyPrefix.StartsWith('/') || KeyPrefix.EndsWith('/') || KeyPrefix.Contains("..") || KeyPrefix.Contains("//"))
            throw new InvalidOperationException("Storage:S3:KeyPrefix must not start/end with '/' or contain '..'.");
    }
}

public sealed class OrphanCleanupOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Files younger than this are never touched.</summary>
    public int GraceHours { get; set; } = 24;
    public int IntervalMinutes { get; set; } = 360;
    /// <summary>Safety cap per run: a bug or a wrong database pointer cannot wipe the bucket in one pass.</summary>
    public int MaxDeletesPerRun { get; set; } = 500;
}

public sealed class StorageOptions
{
    /// <summary>"local" (dev / single server) or "s3" (any S3-compatible service).</summary>
    public string Provider { get; set; } = "local";
    public S3StorageOptions S3 { get; set; } = new();
    public OrphanCleanupOptions OrphanCleanup { get; set; } = new();
    public string LocalRoot { get; set; } = "./data/media";
    public int MaxUploadBytes { get; set; } = 12 * 1024 * 1024;
    /// <summary>Decompression-bomb guard: checked from the image header BEFORE the pixels are decoded.</summary>
    public long MaxPixels { get; set; } = 50_000_000;
    /// <summary>One "GB" of plan storage in bytes. Only tests change this.</summary>
    public long QuotaBytesPerGb { get; set; } = 1_073_741_824;
}

public interface IMediaService
{
    /// <summary>Counts against the plan's storage limit. Nothing is stored if validation or the quota check fails.</summary>
    Task<MediaAssetDto> UploadAsync(Stream content, string fileName, string purpose, CancellationToken ct = default);
    Task<PagedResult<MediaAssetDto>> ListAsync(MediaListQuery query, CancellationToken ct = default);
    Task<MediaAssetDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<MediaUsageDto> GetUsageAsync(CancellationToken ct = default);
    /// <summary>Tenant-scoped (authenticated callers). variant: orig | md | sm.</summary>
    Task<MediaContent> OpenAsync(Guid id, string variant, CancellationToken ct = default);
    /// <summary>No tenant in scope. Serves ONLY images attached to a Public, Active design; anything else is indistinguishable from "not found".</summary>
    Task<MediaContent> OpenPublicAsync(Guid id, string variant, CancellationToken ct = default);
    /// <summary>Blocked while the image is attached to a design.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
