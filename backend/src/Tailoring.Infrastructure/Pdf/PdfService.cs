using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Application.Finance;
using Tailoring.Application.Measurements;
using Tailoring.Application.Orders;
using Tailoring.Application.Pdf;
using Tailoring.Application.Production;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Pdf;

public sealed class PdfService(
    AppDbContext db, ITenantContext tenant, IPdfRenderer renderer,
    IInvoiceService invoices, IPaymentService payments, IOrderService orders,
    IMeasurementService measurements, ICustomerService customers, ITaskService tasks, IMyTaskService myTasks) : IPdfService
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

    private byte[] Render(PdfDoc doc) => renderer.Render(doc);

    private async Task<PdfBranding> BrandingAsync(CancellationToken ct)
    {
        var name = await db.Businesses.IgnoreQueryFilters().AsNoTracking().Where(b => b.Id == BusinessId).Select(b => b.Name).FirstAsync(ct);
        var address = await db.Locations.AsNoTracking().Where(l => l.IsActive && l.Address != null).OrderBy(l => l.CreatedAtUtc).Select(l => l.Address).FirstOrDefaultAsync(ct);
        return new PdfBranding(name, address);
    }
}
