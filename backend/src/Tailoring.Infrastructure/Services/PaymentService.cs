using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Finance;
using Tailoring.Application.Locations;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Finance;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>
/// The rule this class exists to enforce: money only counts once a named staff member has verified it.
/// An invoice is never "marked paid"; its balance is always derived from Verified payments and credit notes.
/// </summary>
public sealed class PaymentService(AppDbContext db, ITenantContext tenant, IAuditLogger audit,
    IEntitlementService entitlements, IPermissionService permissions,
    ILocationScope? locations = null) : IPaymentService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    // ------------------------------------------------------------------ location scope (a payment follows its invoice, an invoice follows its order)

    private Task<FinanceScope> ScopeAsync(CancellationToken ct) => FinanceScope.ForAsync(locations, ct);

    private async Task<IQueryable<Payment>> VisiblePaymentsAsync(IQueryable<Payment> q, CancellationToken ct)
    {
        var scope = await ScopeAsync(ct);
        if (!scope.Restricted) return q;
        var ids = db.Invoices.AsNoTracking().Visible(db, scope).Select(i => i.Id);
        return q.Where(p => ids.Contains(p.InvoiceId));
    }

    private async Task<IQueryable<Receipt>> VisibleReceiptsAsync(IQueryable<Receipt> q, CancellationToken ct)
    {
        var scope = await ScopeAsync(ct);
        if (!scope.Restricted) return q;
        var ids = db.Invoices.AsNoTracking().Visible(db, scope).Select(i => i.Id);
        return q.Where(r => ids.Contains(r.InvoiceId));
    }

    /// <summary>A replayed idempotency key must not reveal a payment on a hidden invoice.</summary>
    private async Task EnsureVisibleAsync(Payment payment, CancellationToken ct)
    {
        var scope = await ScopeAsync(ct);
        if (!scope.Restricted) return;
        if (!await db.Invoices.AsNoTracking().Visible(db, scope).AnyAsync(i => i.Id == payment.InvoiceId, ct))
            throw AppException.NotFound("payment_not_found", "Payment not found.");
    }

    public async Task<(PaymentDto Payment, bool Created)> RecordAsync(Guid invoiceId, RecordPaymentRequest req, string? idempotencyKey, CancellationToken ct = default)
    {
        var key = Parse.Clean(idempotencyKey);
        if (key is { Length: > 100 }) throw AppException.BadRequest("invalid_idempotency_key", "Idempotency key is too long.");
        if (key is not null && await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct) is { } already)
        {
            await EnsureVisibleAsync(already, ct);
            if (already.InvoiceId != invoiceId || already.Amount != req.Amount)
                throw AppException.Conflict("idempotency_key_reused", "That idempotency key was already used for a different payment.");
            return (await ToDtoAsync(already, ct), false);
        }

        var method = Parse.Value<PaymentMethod>(req.Method, "method");
        if (InvoiceMath.R(req.Amount) != req.Amount) throw AppException.BadRequest("invalid_amount", "Amounts can have at most 2 decimal places.");
        var today = await TodayAsync(ct);
        var receivedOn = req.ReceivedOn ?? today;
        if (receivedOn > today) throw AppException.BadRequest("received_in_future", "A payment cannot be dated in the future.");
        var verifyNow = req.VerifyNow && await CanVerifyAsync(ct); // without the permission it simply stays Pending

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await Locks.AcquireAsync(db, $"invoice:{invoiceId:N}", ct);

            var invoice = await db.Invoices.AsNoTracking().Visible(db, await ScopeAsync(ct)).FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                ?? throw AppException.NotFound("invoice_not_found", "Invoice not found.");
            if (invoice.Status != InvoiceStatus.Issued)
                throw AppException.Conflict("invoice_not_payable", $"Payments can only be recorded against issued invoices (this one is {invoice.Status}).");

            var outstanding = await OutstandingAsync(invoice, ct);
            if (req.Amount > outstanding)
                throw AppException.Conflict("overpayment", $"The payment ({req.Amount}) is more than the amount still owed ({outstanding}).");
            if (req.Amount < outstanding)
                await entitlements.RequireFlagAsync(BusinessId, EntitlementKeys.FinancePartialPayments, ct); // Free plan: pay in full

            var payment = new Payment
            {
                BusinessId = BusinessId, InvoiceId = invoiceId, CustomerId = invoice.CustomerId, Amount = req.Amount,
                CurrencyCode = invoice.CurrencyCode, Method = method, Reference = Parse.Clean(req.Reference), ReceivedOn = receivedOn,
                Notes = Parse.Clean(req.Notes), Status = PaymentStatus.Pending, RecordedByUserId = tenant.UserId, IdempotencyKey = key,
            };
            db.Payments.Add(payment);
            audit.Record(BusinessId, tenant.UserId, "payment.recorded", nameof(Payment), payment.Id,
                new { invoice = invoice.Number, payment.Amount, method = method.ToString(), verifyNow });

            if (verifyNow) await MarkVerifiedAsync(payment, invoice, outstanding, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (await ToDtoAsync(payment, ct), true);
        }
        catch (DbUpdateException ex) when (key is not null && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var winner = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct);
            if (winner is null) throw;
            await EnsureVisibleAsync(winner, ct);
            return (await ToDtoAsync(winner, ct), false); // a retry raced the first request
        }
    }

    public async Task<PaymentDto> VerifyAsync(Guid id, CancellationToken ct = default)
    {
        var (payment, invoice) = await LockAndLoadAsync(id, ct);
        await using var tx = db.Database.CurrentTransaction!;
        if (payment.Status != PaymentStatus.Pending)
            throw AppException.Conflict("invalid_transition", $"Only pending payments can be verified (this one is {payment.Status}).");
        if (invoice.Status != InvoiceStatus.Issued)
            throw AppException.Conflict("invoice_not_payable", $"The invoice is {invoice.Status}, so this payment cannot be verified.");

        var outstanding = await OutstandingAsync(invoice, ct);
        if (payment.Amount > outstanding)
            throw AppException.Conflict("overpayment", $"Verifying this payment ({payment.Amount}) would exceed what is still owed ({outstanding}).");
        if (payment.Amount < outstanding)
            await entitlements.RequireFlagAsync(BusinessId, EntitlementKeys.FinancePartialPayments, ct);

        await MarkVerifiedAsync(payment, invoice, outstanding, ct);
        audit.Record(BusinessId, tenant.UserId, "payment.verified", nameof(Payment), payment.Id, new { invoice = invoice.Number, payment.Amount });
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDtoAsync(payment, ct);
    }

    public async Task<PaymentDto> RejectAsync(Guid id, PaymentReasonRequest req, CancellationToken ct = default)
    {
        var (payment, invoice) = await LockAndLoadAsync(id, ct);
        await using var tx = db.Database.CurrentTransaction!;
        if (payment.Status != PaymentStatus.Pending)
            throw AppException.Conflict("invalid_transition", $"Only pending payments can be rejected (this one is {payment.Status}).");

        payment.Status = PaymentStatus.Rejected;
        payment.StatusReason = req.Reason.Trim();
        audit.Record(BusinessId, tenant.UserId, "payment.rejected", nameof(Payment), payment.Id, new { invoice = invoice.Number, reason = payment.StatusReason });
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDtoAsync(payment, ct);
    }

    public async Task<PaymentDto> ReverseAsync(Guid id, PaymentReasonRequest req, CancellationToken ct = default)
    {
        var (payment, invoice) = await LockAndLoadAsync(id, ct);
        await using var tx = db.Database.CurrentTransaction!;
        if (payment.Status != PaymentStatus.Verified)
            throw AppException.Conflict("invalid_transition", $"Only verified payments can be reversed (this one is {payment.Status}).");

        payment.Status = PaymentStatus.Reversed;
        payment.StatusReason = req.Reason.Trim();
        var receipt = await db.Receipts.FirstOrDefaultAsync(r => r.PaymentId == payment.Id, ct);
        if (receipt is not null) receipt.VoidedAtUtc = DateTime.UtcNow; // voided, never deleted
        audit.Record(BusinessId, tenant.UserId, "payment.reversed", nameof(Payment), payment.Id,
            new { invoice = invoice.Number, payment.Amount, reason = payment.StatusReason });
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDtoAsync(payment, ct);
    }

    public async Task<PagedResult<PaymentDto>> ListAsync(PaymentListQuery query, CancellationToken ct = default)
    {
        var q = await VisiblePaymentsAsync(db.Payments.AsNoTracking().AsQueryable(), ct);
        if (query.InvoiceId is { } iid) q = q.Where(p => p.InvoiceId == iid);
        if (query.CustomerId is { } cid) q = q.Where(p => p.CustomerId == cid);
        if (query.From is { } from) q = q.Where(p => p.ReceivedOn >= from);
        if (query.To is { } to) q = q.Where(p => p.ReceivedOn <= to);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = Parse.Value<PaymentStatus>(query.Status, "status");
            q = q.Where(p => p.Status == status);
        }

        var total = await q.CountAsync(ct);
        var page = await q.OrderByDescending(p => p.ReceivedOn).ThenByDescending(p => p.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);

        var invoiceIds = page.Select(p => p.InvoiceId).Distinct().ToList();
        var numbers = await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        var paymentIds = page.Select(p => p.Id).ToList();
        var receipts = await db.Receipts.AsNoTracking().Where(r => paymentIds.Contains(r.PaymentId)).ToDictionaryAsync(r => r.PaymentId, ct);

        return new PagedResult<PaymentDto>(
            page.Select(p => FinanceMapping.ToDto(p, numbers.GetValueOrDefault(p.InvoiceId), receipts.GetValueOrDefault(p.Id))).ToList(),
            query.Page, query.PageSize, total);
    }

    public async Task<PaymentDto> GetAsync(Guid id, CancellationToken ct = default)
        => await ToDtoAsync(await (await VisiblePaymentsAsync(db.Payments.AsNoTracking(), ct)).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw AppException.NotFound("payment_not_found", "Payment not found."), ct);

    public async Task<ReceiptDto> GetReceiptAsync(Guid id, CancellationToken ct = default)
        => await ToReceiptDtoAsync(await (await VisibleReceiptsAsync(db.Receipts.AsNoTracking(), ct)).FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw AppException.NotFound("receipt_not_found", "Receipt not found."), ct);

    public async Task<ReceiptDto> GetReceiptForPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        if (!await (await VisiblePaymentsAsync(db.Payments.AsNoTracking(), ct)).AnyAsync(p => p.Id == paymentId, ct)) // tenant filter makes another business's id look the same
            throw AppException.NotFound("payment_not_found", "Payment not found.");
        return await ToReceiptDtoAsync(await db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.PaymentId == paymentId, ct)
            ?? throw AppException.NotFound("receipt_not_found", "This payment has no receipt. Only verified payments get one."), ct);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Starts a transaction, queues on the invoice's lock, then loads the payment and invoice as they are NOW.</summary>
    private async Task<(Payment Payment, Invoice Invoice)> LockAndLoadAsync(Guid paymentId, CancellationToken ct)
    {
        var invoiceId = await (await VisiblePaymentsAsync(db.Payments.AsNoTracking(), ct)).Where(p => p.Id == paymentId).Select(p => (Guid?)p.InvoiceId).FirstOrDefaultAsync(ct)
            ?? throw AppException.NotFound("payment_not_found", "Payment not found.");
        await db.Database.BeginTransactionAsync(ct);
        await Locks.AcquireAsync(db, $"invoice:{invoiceId:N}", ct);
        var payment = await db.Payments.FirstAsync(p => p.Id == paymentId, ct);
        var invoice = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == invoiceId, ct);
        return (payment, invoice);
    }

    private async Task MarkVerifiedAsync(Payment payment, Invoice invoice, decimal outstandingBefore, CancellationToken ct)
    {
        var verifier = tenant.UserId ?? throw AppException.Unauthorized("no_user", "A signed-in staff member must verify payments.");
        payment.Status = PaymentStatus.Verified;
        payment.VerifiedByUserId = verifier;
        payment.VerifiedAtUtc = DateTime.UtcNow;

        var today = await TodayAsync(ct);
        db.Receipts.Add(new Receipt
        {
            BusinessId = BusinessId, PaymentId = payment.Id, InvoiceId = invoice.Id, Amount = payment.Amount,
            BalanceAfter = outstandingBefore - payment.Amount,
            Number = await DocumentNumbers.NextAsync(db, BusinessId, DocumentKinds.Receipt, "RCT", today.Year, ct),
        });
    }

    private async Task<decimal> OutstandingAsync(Invoice invoice, CancellationToken ct)
    {
        var paid = await db.Payments.Where(p => p.InvoiceId == invoice.Id && p.Status == PaymentStatus.Verified).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var credited = await db.CreditNotes.Where(c => c.InvoiceId == invoice.Id).SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
        return InvoiceMath.Balance(invoice.Total, paid, credited);
    }

    private async Task<bool> CanVerifyAsync(CancellationToken ct)
    {
        var access = tenant.UserId is { } userId ? await permissions.GetAccessAsync(userId, BusinessId, ct) : null;
        return access is not null && (access.IsOwner || access.Permissions.Contains(Permissions.PaymentsVerify));
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
        => BusinessTime.Today(await db.Businesses.AsNoTracking().Select(b => b.TimeZoneId).FirstAsync(ct));

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("stale_version", "This payment was changed by someone else. Reload and try again."); }
    }

    private async Task<PaymentDto> ToDtoAsync(Payment p, CancellationToken ct)
    {
        var number = await db.Invoices.AsNoTracking().Where(i => i.Id == p.InvoiceId).Select(i => i.Number).FirstOrDefaultAsync(ct);
        var receipt = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.PaymentId == p.Id, ct);
        return FinanceMapping.ToDto(p, number, receipt);
    }

    private async Task<ReceiptDto> ToReceiptDtoAsync(Receipt r, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().FirstAsync(p => p.Id == r.PaymentId, ct);
        var invoice = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == r.InvoiceId, ct);
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == invoice.CustomerId).Select(c => c.FullName).FirstOrDefaultAsync(ct) ?? "";
        return new ReceiptDto(r.Id, r.Number, r.PaymentId, r.InvoiceId, invoice.Number, invoice.CustomerId, customer, invoice.CurrencyCode,
            r.Amount, payment.Method.ToString(), payment.Reference, r.BalanceAfter, r.IssuedAtUtc, r.VoidedAtUtc is not null);
    }
}
