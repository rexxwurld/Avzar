using Tailoring.Domain.Common;

namespace Tailoring.Domain.Media;

/// <summary>What an upload is for. Later steps add Logo, Banner, Favicon and ShareImage for the storefront.</summary>
public enum MediaPurpose { DesignImage, SiteImage }

public static class MediaVariants
{
    public const string Original = "orig";  // re-encoded, longest side capped
    public const string Medium = "md";
    public const string Small = "sm";
    public static readonly IReadOnlyList<string> All = [Original, Medium, Small];
    public static bool IsValid(string v) => All.Contains(v);

    /// <summary>Server-generated only. Client-supplied text never reaches a storage key.</summary>
    public static string KeyFor(Guid businessId, Guid assetId, string variant) => $"{businessId:N}/{assetId:N}/{variant}.webp";
}

/// <summary>
/// An uploaded image that has been decoded, validated and RE-ENCODED by the server (metadata stripped).
/// The original bytes the user sent are never stored or served.
/// </summary>
public class MediaAsset : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public MediaPurpose Purpose { get; set; }
    /// <summary>Display only: sanitised, truncated. Never used in a storage path.</summary>
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "image/webp";
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Sum of all stored variants. This is what counts against the plan's storage limit.</summary>
    public long SizeBytes { get; set; }
    /// <summary>SHA-256 of the bytes the user uploaded (kept for future duplicate detection / abuse takedown).</summary>
    public string Sha256 { get; set; } = "";
    public Guid? UploadedByUserId { get; set; }
}
