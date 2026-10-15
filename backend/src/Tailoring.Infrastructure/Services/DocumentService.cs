using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Documents;
using Tailoring.Application.Media;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Documents;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Storage;

namespace Tailoring.Infrastructure.Services;

public sealed class DocumentService(
    AppDbContext db, ITenantContext tenant, IAuditLogger audit, IEntitlementService entitlements,
    IObjectStorage storage, IVirusScanner scanner,
    IOptions<StorageOptions> storageOptions, IOptions<DocumentOptions> documentOptions, IOptions<AntivirusOptions> antivirusOptions) : IDocumentService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<DocumentDto> UploadAsync(Stream content, string fileName, string? description, Guid? customerId, Guid? orderId, CancellationToken ct = default)
    {
        var bytes = await ReadBoundedAsync(content, documentOptions.Value.MaxUploadBytes, ct);
        var sniffed = DocumentSniffer.Inspect(bytes, fileName);

        // Tenant-filtered lookups: another business's customer or order behaves exactly like a missing one.
        if (orderId is not null)
        {
            var order = await db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => new { o.CustomerId }).FirstOrDefaultAsync(ct)
                ?? throw AppException.NotFound("order_not_found", "Order not found.");
            if (customerId is not null && customerId != order.CustomerId)
                throw AppException.BadRequest("customer_order_mismatch", "That order belongs to a different customer.");
            customerId ??= order.CustomerId;
        }
        else if (customerId is not null && !await db.Customers.AnyAsync(c => c.Id == customerId, ct))
            throw AppException.NotFound("customer_not_found", "Customer not found.");

        var scan = await scanner.ScanAsync(bytes, ct);
        var status = DocumentScanStatus.NotScanned;
        switch (scan.Outcome)
        {
            case ScanOutcome.Clean: status = DocumentScanStatus.Clean; break;
            case ScanOutcome.Infected:
                audit.Record(BusinessId, tenant.UserId, "document.rejected_infected", nameof(StoredDocument), null, new { signature = scan.Detail });
                await db.SaveChangesAsync(ct);
                throw new AppException(422, "file_infected", "This file was rejected because it contains malware.");
            case ScanOutcome.Unavailable when antivirusOptions.Value.Required:
                throw new AppException(503, "scan_unavailable", "The virus scanner is unavailable, so the file was not accepted. Try again shortly.");
            // Unavailable (not required) and Disabled: stored as NotScanned
        }

        var doc = new StoredDocument
        {
            BusinessId = BusinessId, FileName = MediaService.SafeFileName(fileName, "document"), Kind = sniffed.Kind, ContentType = sniffed.ContentType,
            SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ScanStatus = status,
            Description = string.IsNullOrWhiteSpace(description) ? null : Parse.Truncate(description.Trim(), 500),
            CustomerId = customerId, OrderId = orderId, UploadedByUserId = tenant.UserId,
        };
        var key = DocumentKeys.KeyFor(BusinessId, doc.Id);

        var written = false;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Documents and images share the plan's storage allowance.
            await entitlements.EnsureWithinLimitAsync(BusinessId, EntitlementKeys.StorageGbMax,
                async c => await UsedBytesAsync(c), doc.SizeBytes, storageOptions.Value.QuotaBytesPerGb, ct);

            await storage.PutAsync(key, bytes, sniffed.ContentType, ct);
            written = true;
            db.Documents.Add(doc);
            audit.Record(BusinessId, tenant.UserId, "document.uploaded", nameof(StoredDocument), doc.Id, new { doc.SizeBytes, kind = doc.Kind.ToString() });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            if (written) { try { await storage.DeleteAsync(key, CancellationToken.None); } catch { /* orphan cleanup job handles it */ } }
            throw;
        }
        return ToDto(doc);
    }

    public async Task<PagedResult<DocumentDto>> ListAsync(DocumentListQuery query, CancellationToken ct = default)
    {
        var q = db.Documents.AsNoTracking();
        if (query.CustomerId is { } cid) q = q.Where(d => d.CustomerId == cid);
        if (query.OrderId is { } oid) q = q.Where(d => d.OrderId == oid);
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            var kind = Parse.Value<DocumentKind>(query.Kind, "kind");
            q = q.Where(d => d.Kind == kind);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(d => d.CreatedAtUtc).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<DocumentDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<DocumentDto> GetAsync(Guid id, CancellationToken ct = default)
        => ToDto(await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw NotFound());

    public async Task<DocumentContent> OpenAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw NotFound();
        var stream = await storage.OpenReadAsync(DocumentKeys.KeyFor(doc.BusinessId, doc.Id), ct)
            ?? throw AppException.NotFound("document_missing", "The file for this document is missing.");
        return new DocumentContent(stream, doc.ContentType, doc.FileName, doc.SizeBytes);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw NotFound();
        db.Documents.Remove(doc);
        audit.Record(BusinessId, tenant.UserId, "document.deleted", nameof(StoredDocument), doc.Id, new { doc.SizeBytes });
        await db.SaveChangesAsync(ct); // row first: a failure below leaves an orphan file, never a dangling row
        try { await storage.DeleteAsync(DocumentKeys.KeyFor(doc.BusinessId, doc.Id), ct); } catch { /* orphan cleanup job later */ }
    }

    private async Task<long> UsedBytesAsync(CancellationToken ct)
        => (await db.MediaAssets.SumAsync(a => (long?)a.SizeBytes, ct) ?? 0L) + (await db.Documents.SumAsync(d => (long?)d.SizeBytes, ct) ?? 0L);

    private static AppException NotFound() => AppException.NotFound("document_not_found", "Document not found.");

    private static async Task<byte[]> ReadBoundedAsync(Stream s, int max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await s.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > max) throw new AppException(413, "file_too_large", $"Documents can be at most {max / (1024 * 1024)} MB.");
            ms.Write(buffer, 0, read);
        }
        if (ms.Length == 0) throw AppException.BadRequest("empty_file", "The uploaded file is empty.");
        return ms.ToArray();
    }

    private static DocumentDto ToDto(StoredDocument d)
        => new(d.Id, d.FileName, d.Kind.ToString(), d.ContentType, d.SizeBytes, d.Description, d.CustomerId, d.OrderId, d.ScanStatus.ToString(), d.CreatedAtUtc);
}
