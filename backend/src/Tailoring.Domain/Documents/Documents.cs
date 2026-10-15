using Tailoring.Domain.Common;

namespace Tailoring.Domain.Documents;

/// <summary>The file families we accept. Decided from the file's bytes, never from the name or the browser-declared type.</summary>
public enum DocumentKind { Pdf, Word, Excel, Text }

/// <summary>Infected files are never stored, so there is no "Infected" state.</summary>
public enum DocumentScanStatus { NotScanned, Clean }

public static class DocumentKeys
{
    /// <summary>Server-generated only. The "d" segment keeps documents apart from media in the same bucket.</summary>
    public static string KeyFor(Guid businessId, Guid documentId) => $"{businessId:N}/d/{documentId:N}.bin";
}

/// <summary>An uploaded attachment (PDF, Word, Excel, text/CSV), optionally linked to a customer and/or an order.</summary>
public class StoredDocument : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    /// <summary>Display and download name only: sanitised. Never used in a storage path.</summary>
    public string FileName { get; set; } = "";
    public DocumentKind Kind { get; set; }
    /// <summary>Chosen by the server from the detected kind.</summary>
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public DocumentScanStatus ScanStatus { get; set; }
    public string? Description { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? UploadedByUserId { get; set; }
}
