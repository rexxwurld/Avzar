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
}

public sealed class StorageOptions
{
    /// <summary>"local" is the only implemented provider. An S3-compatible one belongs to deployment hardening.</summary>
    public string Provider { get; set; } = "local";
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
