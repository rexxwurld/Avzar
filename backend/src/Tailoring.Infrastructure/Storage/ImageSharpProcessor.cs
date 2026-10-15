using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using Tailoring.Application.Common;
using Tailoring.Application.Media;
using Tailoring.Domain.Media;

namespace Tailoring.Infrastructure.Storage;

/// <summary>
/// The security boundary for uploads. The file's NAME and declared content type are ignored; the bytes are sniffed.
/// Only JPEG, PNG and WebP are accepted (no SVG: it can carry script; no GIF: animation). The image is decoded and
/// re-encoded to WebP, which drops EXIF (GPS!), ICC, XMP and anything appended to or hidden in the original file.
/// This is content disarm, NOT antivirus: there is no malware scanner in this step.
/// </summary>
public sealed class ImageSharpProcessor(IOptions<StorageOptions> options) : IImageProcessor
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "JPEG", "PNG", "WEBP" };
    private static readonly (string Variant, int MaxSide)[] Plan = [(MediaVariants.Original, 2560), (MediaVariants.Medium, 960), (MediaVariants.Small, 320)];

    /// <summary>Decoding a large image needs hundreds of MB; never do many at once.</summary>
    private static readonly SemaphoreSlim Gate = new(2, 2);

    public async Task<ProcessedImage> ProcessAsync(byte[] data, CancellationToken ct = default)
    {
        IImageFormat? format;
        ImageInfo info;
        try
        {
            using var probe = new MemoryStream(data, writable: false);
            format = await Image.DetectFormatAsync(probe, ct);
            probe.Position = 0;
            info = await Image.IdentifyAsync(probe, ct);
        }
        catch (Exception ex) when (ex is ImageFormatException or ImageProcessingException or NotSupportedException)
        {
            throw Unsupported();
        }

        if (format is null || !Allowed.Contains(format.Name)) throw Unsupported();
        if (info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > options.Value.MaxPixels)
            throw AppException.BadRequest("image_too_large", "This image has too many pixels. Please resize it and try again.");

        await Gate.WaitAsync(ct);
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 1 }, stream, ct);
            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            var variants = new Dictionary<string, byte[]>();
            int width = image.Width, height = image.Height;
            foreach (var (variant, maxSide) in Plan)
            {
                using var copy = image.Clone(ctx =>
                {
                    if (Math.Max(image.Width, image.Height) > maxSide)
                        ctx.Resize(new ResizeOptions { Size = new Size(maxSide, maxSide), Mode = ResizeMode.Max });
                });
                if (variant == MediaVariants.Original) (width, height) = (copy.Width, copy.Height);

                using var output = new MemoryStream();
                await copy.SaveAsync(output, new WebpEncoder { Quality = 82, SkipMetadata = true }, ct);
                variants[variant] = output.ToArray();
            }
            return new ProcessedImage(width, height, variants);
        }
        catch (Exception ex) when (ex is ImageFormatException or ImageProcessingException or NotSupportedException)
        {
            throw Unsupported();
        }
        finally { Gate.Release(); }
    }

    private static AppException Unsupported() => AppException.BadRequest("unsupported_image",
        "Upload a JPEG, PNG or WebP image. Other file types (including SVG and GIF) are not accepted.");
}
