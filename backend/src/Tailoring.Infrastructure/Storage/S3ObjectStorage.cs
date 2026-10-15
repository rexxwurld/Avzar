using System.Net;
using System.Runtime.CompilerServices;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Tailoring.Application.Media;

namespace Tailoring.Infrastructure.Storage;

/// <summary>
/// S3-compatible object storage (AWS S3, Cloudflare R2, MinIO, Backblaze B2, ...). Keys go through the same
/// validation as local storage, then are placed under the optional <c>KeyPrefix</c>. Credentials come from configuration only.
/// </summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly string _prefix; // "" or "folder/"

    public S3ObjectStorage(IOptions<StorageOptions> options) : this(Build(options.Value.S3), options.Value.S3) { }

    /// <summary>Test seam: supply a client pointed at a fake endpoint.</summary>
    internal S3ObjectStorage(IAmazonS3 client, S3StorageOptions options)
    {
        options.Validate();
        _s3 = client;
        _bucket = options.Bucket;
        _prefix = options.KeyPrefix.Length == 0 ? "" : options.KeyPrefix + "/";
    }

    private static AmazonS3Client Build(S3StorageOptions o)
    {
        o.Validate();
        var cfg = new AmazonS3Config
        {
            ForcePathStyle = o.ForcePathStyle,
            // Newer SDKs add checksum headers many S3-compatible services reject; only send them when the API requires it.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (!string.IsNullOrWhiteSpace(o.ServiceUrl)) { cfg.ServiceURL = o.ServiceUrl; cfg.AuthenticationRegion = o.Region; }
        else cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(o.Region);
        return new AmazonS3Client(new BasicAWSCredentials(o.AccessKey, o.SecretKey), cfg);
    }

    private string Full(string key) { StorageKeys.EnsureValid(key); return _prefix + key; }

    public async Task PutAsync(string key, ReadOnlyMemory<byte> data, string contentType, CancellationToken ct = default)
    {
        using var body = new MemoryStream(data.ToArray(), writable: false);
        await _s3.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = Full(key), InputStream = body, ContentType = contentType }, ct);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = Full(key) }, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey") { return null; }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
        => await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = Full(key) }, ct); // S3 delete is idempotent

    public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        StorageKeys.EnsureValidPrefix(prefix);
        string? token = null;
        do
        {
            var page = await _s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, Prefix = _prefix + prefix, ContinuationToken = token }, ct);
            foreach (var o in page.S3Objects ?? [])
            {
                if (o.Key is null || !o.Key.StartsWith(_prefix, StringComparison.Ordinal)) continue;
                long? size = o.Size;
                DateTime? modified = o.LastModified;
                // A missing timestamp is treated as "just now" so such an object is never considered old enough to delete.
                yield return new StoredObject(o.Key[_prefix.Length..], size ?? 0, (modified ?? DateTime.UtcNow).ToUniversalTime());
            }
            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (token is not null);
    }

    public void Dispose() => _s3.Dispose();
}
