using Microsoft.AspNetCore.Mvc;
using Tailoring.Api.Security;
using Tailoring.Application.Pdf;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

/// <summary>
/// Printable documents, drawn on demand from live records. Each route needs the same permission as viewing the record itself.
/// 503 pdf_license_not_configured until Pdf:QuestPdfLicense is set. Sharing by emailed link arrives with the customer portal.
/// </summary>
[ApiController]
public class PdfController(IPdfService pdf) : ControllerBase
{
    private FileContentResult Pdf(PdfFile f) => File(f.Content, "application/pdf", f.FileName);

    [HttpGet("api/invoices/{id:guid}/pdf"), HasPermission(Permissions.InvoicesView)]
    public async Task<IActionResult> Invoice(Guid id, CancellationToken ct) => Pdf(await pdf.InvoiceAsync(id, ct));

    [HttpGet("api/invoices/{id:guid}/credit-notes/{creditNoteId:guid}/pdf"), HasPermission(Permissions.InvoicesView)]
    public async Task<IActionResult> CreditNote(Guid id, Guid creditNoteId, CancellationToken ct) => Pdf(await pdf.CreditNoteAsync(id, creditNoteId, ct));

    [HttpGet("api/receipts/{id:guid}/pdf"), HasPermission(Permissions.PaymentsView)]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct) => Pdf(await pdf.ReceiptAsync(id, ct));

    [HttpGet("api/orders/{id:guid}/quotation-pdf"), HasPermission(Permissions.OrdersView)]
    public async Task<IActionResult> Quotation(Guid id, CancellationToken ct) => Pdf(await pdf.QuotationAsync(id, ct));

    [HttpGet("api/customers/{customerId:guid}/measurement-sets/{setId:guid}/pdf"), HasPermission(Permissions.CustomersView)]
    public async Task<IActionResult> MeasurementSheet(Guid customerId, Guid setId, CancellationToken ct) => Pdf(await pdf.MeasurementSheetAsync(customerId, setId, ct));

    [HttpGet("api/tasks/{id:guid}/job-sheet-pdf"), HasPermission(Permissions.TasksView)]
    public async Task<IActionResult> JobSheet(Guid id, CancellationToken ct) => Pdf(await pdf.JobSheetAsync(id, asWorker: false, ct));

    /// <summary>Only tasks assigned to the caller are reachable (same rule as the worker task view); no customer details.</summary>
    [HttpGet("api/my/tasks/{id:guid}/job-sheet-pdf"), HasPermission(Permissions.TasksWork)]
    public async Task<IActionResult> MyJobSheet(Guid id, CancellationToken ct) => Pdf(await pdf.JobSheetAsync(id, asWorker: true, ct));
}
