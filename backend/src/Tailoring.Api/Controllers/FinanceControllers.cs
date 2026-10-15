using Microsoft.AspNetCore.Mvc;
using Tailoring.Api.Security;
using Tailoring.Application.Common;
using Tailoring.Application.Finance;
using Tailoring.Domain.Access;

namespace Tailoring.Api.Controllers;

[ApiController, Route("api/invoices")]
public class InvoicesController(IInvoiceService invoices) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.InvoicesView)]
    public async Task<PagedResult<InvoiceSummaryDto>> List([FromQuery] InvoiceListQuery query, CancellationToken ct) => await invoices.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.InvoicesView)]
    public async Task<InvoiceDto> Get(Guid id, CancellationToken ct) => await invoices.GetAsync(id, ct);

    /// <summary>Free-form invoice (alterations, services). Send an Idempotency-Key header so a retry cannot create a second one.</summary>
    [HttpPost, HasPermission(Permissions.InvoicesManage)]
    public async Task<ActionResult<InvoiceDto>> Create(CreateInvoiceRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        var (invoice, created) = await invoices.CreateAsync(request, key, ct);
        return created ? CreatedAtAction(nameof(Get), new { id = invoice.Id }, invoice) : Ok(invoice);
    }

    /// <summary>Bill a confirmed order: Full, Deposit or Balance. Returns a Draft; issue it when ready.</summary>
    [HttpPost("from-order/{orderId:guid}"), HasPermission(Permissions.InvoicesManage)]
    public async Task<ActionResult<InvoiceDto>> FromOrder(Guid orderId, CreateInvoiceFromOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        var (invoice, created) = await invoices.CreateFromOrderAsync(orderId, request, key, ct);
        return created ? CreatedAtAction(nameof(Get), new { id = invoice.Id }, invoice) : Ok(invoice);
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.InvoicesManage)]
    public async Task<InvoiceDto> Update(Guid id, UpdateInvoiceRequest request, CancellationToken ct) => await invoices.UpdateDraftAsync(id, request, ct);

    [HttpDelete("{id:guid}"), HasPermission(Permissions.InvoicesManage)]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken ct) { await invoices.DeleteDraftAsync(id, ct); return NoContent(); }

    [HttpPost("{id:guid}/issue"), HasPermission(Permissions.InvoicesManage)]
    public async Task<InvoiceDto> Issue(Guid id, CancellationToken ct) => await invoices.IssueAsync(id, ct);

    [HttpPost("{id:guid}/void"), HasPermission(Permissions.InvoicesManage)]
    public async Task<InvoiceDto> Void(Guid id, VoidInvoiceRequest request, CancellationToken ct) => await invoices.VoidAsync(id, request, ct);

    [HttpPost("{id:guid}/credit-notes"), HasPermission(Permissions.InvoicesManage)]
    public async Task<InvoiceDto> CreditNote(Guid id, CreditNoteRequest request, CancellationToken ct) => await invoices.AddCreditNoteAsync(id, request, ct);

    /// <summary>Records a payment claim (Pending). It does not count until verified. Send an Idempotency-Key header.</summary>
    [HttpPost("{id:guid}/payments"), HasPermission(Permissions.PaymentsRecord)]
    public async Task<ActionResult<PaymentDto>> RecordPayment(Guid id, RecordPaymentRequest request, [FromServices] IPaymentService payments,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        var (payment, created) = await payments.RecordAsync(id, request, key, ct);
        return created ? StatusCode(StatusCodes.Status201Created, payment) : Ok(payment);
    }
}

[ApiController, Route("api/payments")]
public class PaymentsController(IPaymentService payments) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.PaymentsView)]
    public async Task<PagedResult<PaymentDto>> List([FromQuery] PaymentListQuery query, CancellationToken ct) => await payments.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.PaymentsView)]
    public async Task<PaymentDto> Get(Guid id, CancellationToken ct) => await payments.GetAsync(id, ct);

    /// <summary>The moment the money counts. Issues the receipt.</summary>
    [HttpPost("{id:guid}/verify"), HasPermission(Permissions.PaymentsVerify)]
    public async Task<PaymentDto> Verify(Guid id, CancellationToken ct) => await payments.VerifyAsync(id, ct);

    [HttpPost("{id:guid}/reject"), HasPermission(Permissions.PaymentsVerify)]
    public async Task<PaymentDto> Reject(Guid id, PaymentReasonRequest request, CancellationToken ct) => await payments.RejectAsync(id, request, ct);

    [HttpPost("{id:guid}/reverse"), HasPermission(Permissions.PaymentsVerify)]
    public async Task<PaymentDto> Reverse(Guid id, PaymentReasonRequest request, CancellationToken ct) => await payments.ReverseAsync(id, request, ct);

    [HttpGet("{id:guid}/receipt"), HasPermission(Permissions.PaymentsView)]
    public async Task<ReceiptDto> ReceiptForPayment(Guid id, CancellationToken ct) => await payments.GetReceiptForPaymentAsync(id, ct);
}

[ApiController, Route("api/receipts")]
public class ReceiptsController(IPaymentService payments) : ControllerBase
{
    [HttpGet("{id:guid}"), HasPermission(Permissions.PaymentsView)]
    public async Task<ReceiptDto> Get(Guid id, CancellationToken ct) => await payments.GetReceiptAsync(id, ct);
}

[ApiController]
public class ExpensesController(IExpenseService expenses) : ControllerBase
{
    [HttpGet("api/expense-categories"), HasPermission(Permissions.ExpensesView)]
    public async Task<IReadOnlyList<ExpenseCategoryDto>> Categories(CancellationToken ct) => await expenses.ListCategoriesAsync(ct);

    [HttpPost("api/expense-categories"), HasPermission(Permissions.ExpensesManage)]
    public async Task<ActionResult<ExpenseCategoryDto>> CreateCategory(UpsertExpenseCategoryRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await expenses.CreateCategoryAsync(request, ct));

    [HttpPut("api/expense-categories/{id:guid}"), HasPermission(Permissions.ExpensesManage)]
    public async Task<ExpenseCategoryDto> UpdateCategory(Guid id, UpsertExpenseCategoryRequest request, CancellationToken ct)
        => await expenses.UpdateCategoryAsync(id, request, ct);

    [HttpGet("api/expenses"), HasPermission(Permissions.ExpensesView)]
    public async Task<PagedResult<ExpenseDto>> List([FromQuery] ExpenseListQuery query, CancellationToken ct) => await expenses.ListAsync(query, ct);

    [HttpGet("api/expenses/summary"), HasPermission(Permissions.ExpensesView)]
    public async Task<ExpenseSummaryDto> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => await expenses.SummaryAsync(from, to, ct);

    [HttpGet("api/expenses/{id:guid}"), HasPermission(Permissions.ExpensesView)]
    public async Task<ExpenseDto> Get(Guid id, CancellationToken ct) => await expenses.GetAsync(id, ct);

    [HttpPost("api/expenses"), HasPermission(Permissions.ExpensesManage)]
    public async Task<ActionResult<ExpenseDto>> Create(UpsertExpenseRequest request, CancellationToken ct)
    {
        var created = await expenses.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("api/expenses/{id:guid}"), HasPermission(Permissions.ExpensesManage)]
    public async Task<ExpenseDto> Update(Guid id, UpsertExpenseRequest request, CancellationToken ct) => await expenses.UpdateAsync(id, request, ct);

    [HttpPost("api/expenses/{id:guid}/void"), HasPermission(Permissions.ExpensesManage)]
    public async Task<ExpenseDto> Void(Guid id, VoidExpenseRequest request, CancellationToken ct) => await expenses.VoidAsync(id, request, ct);
}
