using System.ComponentModel.DataAnnotations;
using Tailoring.Application.Common;

namespace Tailoring.Application.Finance;

// ------------------------------------------------------------------ invoices

public sealed record InvoiceLineInput
{
    [Required, StringLength(200, MinimumLength = 1)] public string Description { get; init; } = "";
    [Range(0.01, 100_000)] public decimal Quantity { get; init; } = 1;
    [Range(0, 1_000_000_000)] public decimal UnitPrice { get; init; }
    /// <summary>An amount taken off this line before tax (not a percentage).</summary>
    [Range(0, 1_000_000_000)] public decimal Discount { get; init; }
    /// <summary>Omit to use the invoice's tax rate.</summary>
    [Range(0, 100)] public decimal? TaxPercent { get; init; }
}

public sealed record CreateInvoiceRequest
{
    [Required] public Guid CustomerId { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<InvoiceLineInput> Lines { get; init; } = new();
    public DateOnly? DueDate { get; init; }
    /// <summary>Default tax for lines without their own. Omit to use the business pricing profile's tax rate.</summary>
    [Range(0, 100)] public decimal? TaxPercent { get; init; }
    [StringLength(2000)] public string? Notes { get; init; }
    [StringLength(2000)] public string? Terms { get; init; }
}

public sealed record CreateInvoiceFromOrderRequest
{
    /// <summary>Full (itemised, whole order) | Deposit (needs DepositPercent or DepositAmount) | Balance (everything not yet billed).</summary>
    [Required] public string Mode { get; init; } = "Full";
    [Range(0.01, 99.99)] public decimal? DepositPercent { get; init; }
    [Range(0.01, 1_000_000_000)] public decimal? DepositAmount { get; init; }
    public DateOnly? DueDate { get; init; }
    [Range(0, 100)] public decimal? TaxPercent { get; init; }
    [StringLength(2000)] public string? Notes { get; init; }
    [StringLength(2000)] public string? Terms { get; init; }
}

public sealed record UpdateInvoiceRequest
{
    [Required] public uint? Version { get; init; }
    public DateOnly? DueDate { get; init; }
    [StringLength(2000)] public string? Notes { get; init; }
    [StringLength(2000)] public string? Terms { get; init; }
    /// <summary>Free-form invoices only: replaces all lines. Omit to keep them. Order invoices reject this: void and recreate instead.</summary>
    [MaxLength(100)] public List<InvoiceLineInput>? Lines { get; init; }
    [Range(0, 100)] public decimal? TaxPercent { get; init; }
}

public sealed record InvoiceListQuery : PagedQuery
{
    /// <summary>Invoice number or customer name.</summary>
    public string? Search { get; init; }
    public string? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? OrderId { get; init; }
    /// <summary>Issued invoices with a balance above zero.</summary>
    public bool? Outstanding { get; init; }
    /// <summary>Issued invoices with a balance, past their due date.</summary>
    public bool? Overdue { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record VoidInvoiceRequest { [Required, StringLength(500, MinimumLength = 3)] public string Reason { get; init; } = ""; }

public sealed record CreditNoteRequest
{
    [Range(0.01, 1_000_000_000)] public decimal Amount { get; init; }
    [Required, StringLength(500, MinimumLength = 3)] public string Reason { get; init; } = "";
}

public sealed record InvoiceLineDto(Guid Id, int LineNumber, string Description, decimal Quantity, decimal UnitPrice,
    decimal Discount, decimal TaxPercent, decimal NetAmount, decimal TaxAmount, decimal LineTotal);

public sealed record CreditNoteDto(Guid Id, string Number, decimal Amount, string Reason, DateTime CreatedAtUtc);

/// <param name="PaymentStatus">Unpaid | PartPaid | Paid | Credited. Null for drafts and void invoices.</param>
public sealed record InvoiceSummaryDto(Guid Id, string? Number, string Kind, string Status, Guid CustomerId, string CustomerName,
    Guid? OrderId, string CurrencyCode, decimal Total, decimal AmountPaid, decimal AmountCredited, decimal Balance,
    string? PaymentStatus, bool IsOverdue, DateOnly? IssueDate, DateOnly? DueDate);

public sealed record InvoiceDto(Guid Id, string? Number, string Kind, string Status, Guid CustomerId, string CustomerName,
    Guid? OrderId, string? OrderNumber, string CurrencyCode, DateOnly? IssueDate, DateOnly? DueDate, string? Notes, string? Terms,
    decimal Subtotal, decimal TaxTotal, decimal Total, decimal AmountPaid, decimal AmountPending, decimal AmountCredited, decimal Balance,
    string? PaymentStatus, bool IsOverdue, string? VoidReason, uint Version, DateTime CreatedAtUtc, DateTime? IssuedAtUtc,
    IReadOnlyList<InvoiceLineDto> Lines, IReadOnlyList<PaymentDto> Payments, IReadOnlyList<CreditNoteDto> CreditNotes);

public interface IInvoiceService
{
    Task<PagedResult<InvoiceSummaryDto>> ListAsync(InvoiceListQuery query, CancellationToken ct = default);
    Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>A repeated Idempotency-Key returns the original invoice (Created=false) instead of a duplicate.</summary>
    Task<(InvoiceDto Invoice, bool Created)> CreateAsync(CreateInvoiceRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<(InvoiceDto Invoice, bool Created)> CreateFromOrderAsync(Guid orderId, CreateInvoiceFromOrderRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<InvoiceDto> UpdateDraftAsync(Guid id, UpdateInvoiceRequest request, CancellationToken ct = default);
    /// <summary>Drafts only. Issued invoices are never deleted; void them.</summary>
    Task DeleteDraftAsync(Guid id, CancellationToken ct = default);
    /// <summary>Assigns the number and freezes the document.</summary>
    Task<InvoiceDto> IssueAsync(Guid id, CancellationToken ct = default);
    /// <summary>Only when no payment (pending or verified) and no credit note exists.</summary>
    Task<InvoiceDto> VoidAsync(Guid id, VoidInvoiceRequest request, CancellationToken ct = default);
    Task<InvoiceDto> AddCreditNoteAsync(Guid id, CreditNoteRequest request, CancellationToken ct = default);
}

// ------------------------------------------------------------------ payments + receipts

public sealed record RecordPaymentRequest
{
    [Range(0.01, 1_000_000_000)] public decimal Amount { get; init; }
    /// <summary>Cash | BankTransfer | Card | MobileMoney | Other</summary>
    [Required] public string Method { get; init; } = "";
    [StringLength(100)] public string? Reference { get; init; }
    /// <summary>Defaults to today in the business's time zone. Cannot be in the future.</summary>
    public DateOnly? ReceivedOn { get; init; }
    [StringLength(500)] public string? Notes { get; init; }
    /// <summary>Confirm in the same step (e.g. cash in hand). Needs payments.verify; without it the payment stays Pending.</summary>
    public bool VerifyNow { get; init; }
}

public sealed record PaymentReasonRequest { [Required, StringLength(500, MinimumLength = 3)] public string Reason { get; init; } = ""; }

public sealed record PaymentListQuery : PagedQuery
{
    public Guid? InvoiceId { get; init; }
    public Guid? CustomerId { get; init; }
    public string? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record PaymentDto(Guid Id, Guid InvoiceId, string? InvoiceNumber, Guid CustomerId, decimal Amount, string CurrencyCode,
    string Method, string? Reference, DateOnly ReceivedOn, string Status, string? Notes, string? StatusReason,
    Guid? RecordedByUserId, Guid? VerifiedByUserId, DateTime? VerifiedAtUtc, Guid? ReceiptId, string? ReceiptNumber, uint Version, DateTime CreatedAtUtc);

public sealed record ReceiptDto(Guid Id, string Number, Guid PaymentId, Guid InvoiceId, string? InvoiceNumber, Guid CustomerId,
    string CustomerName, string CurrencyCode, decimal Amount, string Method, string? Reference, decimal BalanceAfter,
    DateTime IssuedAtUtc, bool IsVoid);

public interface IPaymentService
{
    /// <summary>Creates a Pending payment (Verified straight away only when VerifyNow is set AND the caller may verify). Idempotent by key.</summary>
    Task<(PaymentDto Payment, bool Created)> RecordAsync(Guid invoiceId, RecordPaymentRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<PagedResult<PaymentDto>> ListAsync(PaymentListQuery query, CancellationToken ct = default);
    Task<PaymentDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Pending to Verified: this is the moment the money counts and the receipt is issued.</summary>
    Task<PaymentDto> VerifyAsync(Guid id, CancellationToken ct = default);
    Task<PaymentDto> RejectAsync(Guid id, PaymentReasonRequest request, CancellationToken ct = default);
    /// <summary>Verified to Reversed (refund, chargeback, mistake). The receipt is voided, never deleted.</summary>
    Task<PaymentDto> ReverseAsync(Guid id, PaymentReasonRequest request, CancellationToken ct = default);
    Task<ReceiptDto> GetReceiptAsync(Guid id, CancellationToken ct = default);
    Task<ReceiptDto> GetReceiptForPaymentAsync(Guid paymentId, CancellationToken ct = default);
}

// ------------------------------------------------------------------ expenses

public sealed record ExpenseCategoryDto(Guid Id, string Name, bool IsActive, int SortOrder);
public sealed record UpsertExpenseCategoryRequest
{
    [Required, StringLength(80, MinimumLength = 2)] public string Name { get; init; } = "";
    public bool IsActive { get; init; } = true;
    [Range(0, 10000)] public int SortOrder { get; init; }
}

public sealed record ExpenseDto(Guid Id, Guid CategoryId, string CategoryName, DateOnly ExpenseDate, decimal Amount, string CurrencyCode,
    string Description, string? Vendor, string? Reference, bool IsVoid, string? VoidReason, uint Version, DateTime CreatedAtUtc);

public sealed record UpsertExpenseRequest
{
    [Required] public Guid CategoryId { get; init; }
    [Required] public DateOnly ExpenseDate { get; init; }
    [Range(0.01, 1_000_000_000)] public decimal Amount { get; init; }
    [Required, StringLength(300, MinimumLength = 2)] public string Description { get; init; } = "";
    [StringLength(120)] public string? Vendor { get; init; }
    [StringLength(100)] public string? Reference { get; init; }
    /// <summary>Required on update.</summary>
    public uint? Version { get; init; }
}

public sealed record VoidExpenseRequest { [Required, StringLength(500, MinimumLength = 3)] public string Reason { get; init; } = ""; }

public sealed record ExpenseListQuery : PagedQuery
{
    public Guid? CategoryId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Search { get; init; }
    public bool IncludeVoid { get; init; }
}

public sealed record ExpenseTotalDto(Guid CategoryId, string CategoryName, decimal Total, int Count);
public sealed record ExpenseSummaryDto(string CurrencyCode, DateOnly? From, DateOnly? To, decimal Total, IReadOnlyList<ExpenseTotalDto> ByCategory);

public interface IExpenseService
{
    Task<IReadOnlyList<ExpenseCategoryDto>> ListCategoriesAsync(CancellationToken ct = default);
    Task<ExpenseCategoryDto> CreateCategoryAsync(UpsertExpenseCategoryRequest request, CancellationToken ct = default);
    Task<ExpenseCategoryDto> UpdateCategoryAsync(Guid id, UpsertExpenseCategoryRequest request, CancellationToken ct = default);
    Task<PagedResult<ExpenseDto>> ListAsync(ExpenseListQuery query, CancellationToken ct = default);
    Task<ExpenseDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ExpenseDto> CreateAsync(UpsertExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseDto> UpdateAsync(Guid id, UpsertExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseDto> VoidAsync(Guid id, VoidExpenseRequest request, CancellationToken ct = default);
    /// <summary>Money out only. Not profit: revenue, receivables and profit are separate reports (Phase 5).</summary>
    Task<ExpenseSummaryDto> SummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default);
}
