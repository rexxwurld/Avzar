using Tailoring.Domain.Common;

namespace Tailoring.Domain.Finance;

/// <summary>Draft is editable and has no number. Issued is a numbered, frozen financial document. Void keeps its number and reason.</summary>
public enum InvoiceStatus { Draft, Issued, Void }
public enum InvoiceKind { Standard, Deposit, Balance }

/// <summary>Only Verified payments count toward a balance. Pending is a claim (cash handed over, transfer notification, customer proof) that staff have not confirmed.</summary>
public enum PaymentStatus { Pending, Verified, Rejected, Reversed }
public enum PaymentMethod { Cash, BankTransfer, Card, MobileMoney, Other }

public static class DocumentKinds
{
    public const string Invoice = "invoice";
    public const string Receipt = "receipt";
    public const string CreditNote = "credit_note";
}

/// <summary>Per-business, per-kind, per-year counter. Numbers are minted atomically, only when a document is issued, so there are no gaps from abandoned drafts.</summary>
public class DocumentCounter : ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Kind { get; set; } = "";
    public int Year { get; set; }
    public long LastValue { get; set; }
}

public class Invoice : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid CustomerId { get; set; }
    /// <summary>Set for invoices billed from an order. Several invoices (deposit + balance) can share one order.</summary>
    public Guid? OrderId { get; set; }
    public string? Number { get; set; }
    public InvoiceKind Kind { get; set; } = InvoiceKind.Standard;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public string CurrencyCode { get; set; } = "";
    public DateOnly? IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Notes { get; set; }
    public string? Terms { get; set; }

    /// <summary>All three are computed on the server from the lines and never accepted from clients. Subtotal is net of line discounts, before tax.</summary>
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }

    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public uint Version { get; set; }
    public List<InvoiceLine> Lines { get; set; } = new();
}

public class InvoiceLine : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid InvoiceId { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    /// <summary>An amount (not a percentage) taken off this line before tax.</summary>
    public decimal Discount { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class Payment : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "";
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? Notes { get; set; }
    /// <summary>Why a payment was rejected or reversed.</summary>
    public string? StatusReason { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public string? IdempotencyKey { get; set; }
    public uint Version { get; set; }
}

/// <summary>Proof of a VERIFIED payment (never of invoice creation). Immutable; reversing the payment voids the receipt instead of deleting it.</summary>
public class Receipt : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid InvoiceId { get; set; }
    public string Number { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? VoidedAtUtc { get; set; }
}

/// <summary>Reduces what a customer owes on an issued invoice (goodwill, correction). Never deletes or edits the invoice.</summary>
public class CreditNote : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid InvoiceId { get; set; }
    public string Number { get; set; } = "";
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    public Guid? CreatedByUserId { get; set; }
}

public class ExpenseCategory : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Created for each business the first time it opens expenses. Fully editable afterwards.</summary>
    public static readonly IReadOnlyList<string> Defaults =
        ["Fabric & materials", "Labour & outsourcing", "Rent & utilities", "Equipment & maintenance", "Transport & delivery", "Marketing", "Other"];
}

/// <summary>Never deleted: a wrong expense is voided with a reason so the financial trail stays intact.</summary>
public class Expense : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid CategoryId { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Vendor { get; set; }
    public string? Reference { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public uint Version { get; set; }
}
