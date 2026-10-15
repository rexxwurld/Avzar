using Tailoring.Application.Common;

namespace Tailoring.Application.Documents;

public sealed record DocumentDto(Guid Id, string FileName, string Kind, string ContentType, long SizeBytes, string? Description,
    Guid? CustomerId, Guid? OrderId, string ScanStatus, DateTime CreatedAtUtc);

public sealed record DocumentListQuery : PagedQuery
{
    public Guid? CustomerId { get; init; }
    public Guid? OrderId { get; init; }
    public string? Kind { get; init; }
}

/// <summary>Caller owns and must dispose <see cref="Content"/>.</summary>
public sealed record DocumentContent(Stream Content, string ContentType, string FileName, long Length);

public interface IDocumentService
{
    /// <summary>Inspects the bytes, scans them, counts against the plan's storage limit. Nothing is stored if any step fails.</summary>
    Task<DocumentDto> UploadAsync(Stream content, string fileName, string? description, Guid? customerId, Guid? orderId, CancellationToken ct = default);
    Task<PagedResult<DocumentDto>> ListAsync(DocumentListQuery query, CancellationToken ct = default);
    Task<DocumentDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<DocumentContent> OpenAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public enum ScanOutcome { Clean, Infected, Unavailable, Disabled }

public sealed record ScanResult(ScanOutcome Outcome, string? Detail = null);

/// <summary>Antivirus seam. Implementations must never throw for "could not scan": return <see cref="ScanOutcome.Unavailable"/>.</summary>
public interface IVirusScanner
{
    Task<ScanResult> ScanAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
}

public sealed class AntivirusOptions
{
    /// <summary>"none" or "clamav" (a clamd daemon reachable over TCP).</summary>
    public string Provider { get; set; } = "none";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>true: if the scanner cannot be reached the upload is refused (503). false: the file is stored as NotScanned.</summary>
    public bool Required { get; set; }

    public void Validate()
    {
        var p = Provider.Trim().ToLowerInvariant();
        if (p is not ("none" or "clamav")) throw new InvalidOperationException("Antivirus:Provider must be 'none' or 'clamav'.");
        if (p == "clamav" && (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535))
            throw new InvalidOperationException("Antivirus:Host and a valid Antivirus:Port are required for clamav.");
        if (Required && p == "none") throw new InvalidOperationException("Antivirus:Required=true needs a scanner (Antivirus:Provider=clamav).");
    }
}

public sealed class DocumentOptions
{
    public int MaxUploadBytes { get; set; } = 15 * 1024 * 1024;
}
