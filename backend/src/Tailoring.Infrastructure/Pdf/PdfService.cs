using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Application.Finance;
using Tailoring.Application.Measurements;
using Tailoring.Application.Media;
using Tailoring.Application.Orders;
using Tailoring.Application.Pdf;
using Tailoring.Application.Production;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Pdf;

public sealed class PdfService(
    AppDbContext db, ITenantContext tenant, IPdfRenderer renderer,
    IInvoiceService invoices, IPaymentService payments, IOrderService orders,
    IMeasurementService measurements, ICustomerService customers, ITaskService tasks, IMyTaskService myTasks,
    IMediaService? media = null) : IPdfService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<PdfFile> InvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await invoices.GetAsync(invoiceId, ct);
        var file = Render(PdfDocuments.Invoice(invoice, await BrandingAsync(ct)));
        return new PdfFile(file, PdfFormat.SafeFileName("Invoice", invoice.Number, "draft"));
    }

    public async Task<PdfFile> ReceiptAsync(Guid receiptId, CancellationToken ct = default)
    {
        var receipt = await payments.GetReceiptAsync(receiptId, ct);
        return new PdfFile(Render(PdfDocuments.Receipt(receipt, await BrandingAsync(ct))), PdfFormat.SafeFileName("Receipt", receipt.Number, "receipt"));
    }

    public async Task<PdfFile> CreditNoteAsync(Guid invoiceId, Guid creditNoteId, CancellationToken ct = default)
    {
        var invoice = await invoices.GetAsync(invoiceId, ct);
        var note = invoice.CreditNotes.FirstOrDefault(c => c.Id == creditNoteId)
            ?? throw AppException.NotFound("credit_note_not_found", "Credit note not found.");
        return new PdfFile(Render(PdfDocuments.CreditNote(invoice, note, await BrandingAsync(ct))), PdfFormat.SafeFileName("CreditNote", note.Number, "credit-note"));
    }

    public async Task<PdfFile> QuotationAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await orders.GetAsync(orderId, ct);
        return new PdfFile(Render(PdfDocuments.Quotation(order, await BrandingAsync(ct))), PdfFormat.SafeFileName("Quotation", order.OrderNumber, "quotation"));
    }

    public async Task<PdfFile> MeasurementSheetAsync(Guid customerId, Guid setId, CancellationToken ct = default)
    {
        var customer = await customers.GetAsync(customerId, ct);
        var set = (await measurements.ListSetsAsync(customerId, ct)).FirstOrDefault(s => s.Id == setId)
            ?? throw AppException.NotFound("measurement_set_not_found", "Measurement profile not found.");
        return new PdfFile(Render(PdfDocuments.MeasurementSheet(set, customer.FullName, await BrandingAsync(ct))),
            PdfFormat.SafeFileName("Measurements", set.Label, "profile"));
    }

    public async Task<PdfFile> JobSheetAsync(Guid taskId, bool asWorker, CancellationToken ct = default)
    {
        var detail = asWorker ? await myTasks.GetAsync(taskId, ct) : await tasks.GetAsync(taskId, ct);
        return new PdfFile(Render(PdfDocuments.JobSheet(detail, await BrandingAsync(ct))),
            PdfFormat.SafeFileName("JobSheet", $"{detail.Task.OrderNumber}-{detail.Task.StageName}", "task"));
    }

    // Scoped per request. BrandingAsync always runs before Render in every method above, so Render can attach the business's look.
    private PdfStyle? style;

    private byte[] Render(PdfDoc doc) => renderer.Render(style is null ? doc : doc with { Style = style });

    private const int LogoMaxWidth = 240, LogoMaxHeight = 80;

    /// <summary>The logo as a small PNG (the stored image is WebP). A missing or unreadable logo never blocks a document: it just renders without one.</summary>
    private async Task<byte[]?> LogoAsync(Guid? mediaId, CancellationToken ct)
    {
        if (mediaId is null || media is null) return null;
        try
        {
            var content = await media.OpenAsync(mediaId.Value, "md", ct);
            await using var stream = content.Content;
            using var image = await Image.LoadAsync(stream, ct);
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(LogoMaxWidth, LogoMaxHeight), Mode = ResizeMode.Max }));
            using var png = new MemoryStream();
            await image.SaveAsPngAsync(png, ct);
            return png.ToArray();
        }
        catch (AppException) { return null; }          // image deleted or not found
        catch (ImageFormatException) { return null; }  // not decodable
    }

    private async Task<PdfBranding> BrandingAsync(CancellationToken ct)
    {
        var b = await db.Businesses.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == BusinessId)
            .Select(x => new { x.Name, x.BrandColor, x.LogoMediaId, x.ContactPhone, x.ContactEmail, x.TaxNumber, x.PdfFooterNote }).FirstAsync(ct);
        var address = await db.Locations.AsNoTracking().Where(l => l.IsActive && l.Address != null).OrderBy(l => l.CreatedAtUtc).Select(l => l.Address).FirstOrDefaultAsync(ct);
        style = new PdfStyle(b.BrandColor ?? BrandingDto.DefaultAccent, await LogoAsync(b.LogoMediaId, ct), b.ContactPhone, b.ContactEmail, b.TaxNumber, b.PdfFooterNote);
        return new PdfBranding(b.Name, address);
    }
}
