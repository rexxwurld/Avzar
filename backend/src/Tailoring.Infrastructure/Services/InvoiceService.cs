using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Finance;
using Tailoring.Application.Locations;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Finance;
using Tailoring.Domain.Orders;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class InvoiceService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, IEntitlementService entitlements,
    ILocationScope? locations = null) : IInvoiceService
{
    /// <summary>Applied at issue when no due date was chosen.</summary>
    public const int DefaultDueDays = 14;

    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    // ------------------------------------------------------------------ location scope

    private Task<FinanceScope> ScopeAsync(CancellationToken ct) => FinanceScope.ForAsync(locations, ct);

    /// <summary>A replayed idempotency key must not reveal an invoice at a hidden location.</summary>
    private async Task EnsureVisibleAsync(Invoice invoice, CancellationToken ct)
    {
        var scope = await ScopeAsync(ct);
        if (!scope.Restricted) return;
        if (!await db.Invoices.AsNoTracking().Visible(db, scope).AnyAsync(i => i.Id == invoice.Id, ct))
            throw AppException.NotFound("invoice_not_found", "Invoice not found.");
    }

    // ------------------------------------------------------------------ read

    public async Task<PagedResult<InvoiceSummaryDto>> ListAsync(InvoiceListQuery query, CancellationToken ct = default)
    {
        var (_, today) = await BusinessAsync(ct);
        var rows = db.Invoices.AsNoTracking().Visible(db, await ScopeAsync(ct)).Select(i => new
        {
            Invoice = i,
            CustomerName = db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.FullName).FirstOrDefault() ?? "",
            Paid = db.Payments.Where(p => p.InvoiceId == i.Id && p.Status == Domain.Finance.PaymentStatus.Verified).Sum(p => (decimal?)p.Amount) ?? 0m,
            Credited = db.CreditNotes.Where(c => c.InvoiceId == i.Id).Sum(c => (decimal?)c.Amount) ?? 0m,
        });

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = Parse.Value<InvoiceStatus>(query.Status, "status");
            rows = rows.Where(r => r.Invoice.Status == status);
        }
        if (query.CustomerId is { } cid) rows = rows.Where(r => r.Invoice.CustomerId == cid);
        if (query.OrderId is { } oid) rows = rows.Where(r => r.Invoice.OrderId == oid);
        if (query.From is { } from) rows = rows.Where(r => r.Invoice.IssueDate >= from);
        if (query.To is { } to) rows = rows.Where(r => r.Invoice.IssueDate <= to);
        if (query.Outstanding == true)
            rows = rows.Where(r => r.Invoice.Status == InvoiceStatus.Issued && r.Invoice.Total - r.Paid - r.Credited > 0);
        if (query.Overdue == true)
            rows = rows.Where(r => r.Invoice.Status == InvoiceStatus.Issued && r.Invoice.Total - r.Paid - r.Credited > 0 && r.Invoice.DueDate < today);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{Parse.Like(query.Search.Trim())}%";
            rows = rows.Where(r => (r.Invoice.Number != null && EF.Functions.ILike(r.Invoice.Number, pattern)) || EF.Functions.ILike(r.CustomerName, pattern));
        }

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(r => r.Invoice.IssuedAtUtc ?? r.Invoice.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);

        var items = page.Select(r =>
        {
            var i = r.Invoice;
            var (balance, status, overdue) = Derive(i, r.Paid, r.Credited, today);
            return new InvoiceSummaryDto(i.Id, i.Number, i.Kind.ToString(), i.Status.ToString(), i.CustomerId, r.CustomerName, i.OrderId,
                i.CurrencyCode, i.Total, r.Paid, r.Credited, balance, status, overdue, i.IssueDate, i.DueDate);
        }).ToList();
        return new PagedResult<InvoiceSummaryDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default)
        => await ToDtoAsync(await LoadAsync(id, track: false, ct), ct);

    // ------------------------------------------------------------------ create

    public async Task<(InvoiceDto Invoice, bool Created)> CreateAsync(CreateInvoiceRequest req, string? idempotencyKey, CancellationToken ct = default)
    {
        var key = NormalizeKey(idempotencyKey);
        if (key is not null && await FindByKeyAsync(key, ct) is { } already)
        {
            await EnsureVisibleAsync(already, ct);
            return (await ToDtoAsync(already, ct), false);
        }

        if ((await ScopeAsync(ct)).Restricted)
            throw AppException.Forbidden("location_forbidden",
                "An invoice that is not billed from an order has no location, so only staff with access to every location can create it.");
        var (currency, _) = await BusinessAsync(ct);
        if (!await db.Customers.AnyAsync(c => c.Id == req.CustomerId, ct))
            throw AppException.BadRequest("unknown_customer", "Customer not found.");

        var invoice = new Invoice
        {
            BusinessId = BusinessId, CustomerId = req.CustomerId, Kind = InvoiceKind.Standard, CurrencyCode = currency,
            DueDate = req.DueDate, Notes = Parse.Clean(req.Notes), Terms = Parse.Clean(req.Terms),
            IdempotencyKey = key, CreatedByUserId = tenant.UserId,
        };
        SetLines(invoice, req.Lines, req.TaxPercent ?? await ProfileTaxAsync(ct));
        return await SaveNewAsync(invoice, key, ct);
    }

    public async Task<(InvoiceDto Invoice, bool Created)> CreateFromOrderAsync(Guid orderId, CreateInvoiceFromOrderRequest req, string? idempotencyKey, CancellationToken ct = default)
    {
        var key = NormalizeKey(idempotencyKey);
        if (key is not null && await FindByKeyAsync(key, ct) is { } already)
        {
            await EnsureVisibleAsync(already, ct);
            return (await ToDtoAsync(already, ct), false);
        }

        var mode = req.Mode?.Trim().ToLowerInvariant() switch
        {
            "full" => InvoiceKind.Standard,
            "deposit" => InvoiceKind.Deposit,
            "balance" => InvoiceKind.Balance,
            _ => throw AppException.BadRequest("invalid_mode", "Mode must be Full, Deposit or Balance."),
        };
        if (mode != InvoiceKind.Standard) await entitlements.RequireFlagAsync(BusinessId, EntitlementKeys.FinancePartialPayments, ct);
        if (mode == InvoiceKind.Deposit && (req.DepositPercent is null) == (req.DepositAmount is null))
            throw AppException.BadRequest("deposit_amount_required", "Give either DepositPercent or DepositAmount for a deposit invoice.");

        var taxPercent = req.TaxPercent ?? await ProfileTaxAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await Locks.AcquireAsync(db, $"order-invoicing:{orderId:N}", ct); // two clicks cannot both bill the same money

            var order = await db.Orders.AsNoTracking().Include(o => o.Garments).FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw AppException.NotFound("order_not_found", "Order not found.");
            var orderScope = await ScopeAsync(ct);
            if (orderScope.Restricted && !orderScope.Allowed.Contains(order.LocationId))
                throw AppException.NotFound("order_not_found", "Order not found."); // a hidden order looks like a missing one
            if (order.Status is not (OrderStatus.Confirmed or OrderStatus.InProduction or OrderStatus.Ready or OrderStatus.Delivered))
                throw AppException.Conflict("order_not_billable",
                    $"Only confirmed orders can be invoiced (this one is {order.Status}). Quotes can still change, so they are not billed.");

            // Drafts count too, so two drafts can never both claim the same part of the order. Void invoices free it again.
            var billed = await db.Invoices.Where(i => i.OrderId == orderId && i.Status != InvoiceStatus.Void).SumAsync(i => (decimal?)i.Subtotal, ct) ?? 0m;
            var remaining = order.Total - billed;

            var invoice = new Invoice
            {
                BusinessId = BusinessId, CustomerId = order.CustomerId, OrderId = order.Id, Kind = mode, CurrencyCode = order.CurrencyCode,
                DueDate = req.DueDate, Notes = Parse.Clean(req.Notes), Terms = Parse.Clean(req.Terms),
                IdempotencyKey = key, CreatedByUserId = tenant.UserId,
            };

            List<InvoiceLineInput> lines;
            switch (mode)
            {
                case InvoiceKind.Standard:
                    if (billed > 0)
                        throw AppException.Conflict("already_invoiced", "This order already has invoices. Use Mode=Balance to bill what is left.");
                    if (order.Total <= 0) throw AppException.Conflict("nothing_to_invoice", "This order has nothing to bill.");
                    lines = OrderLines(order);
                    break;

                case InvoiceKind.Deposit:
                    if (remaining <= 0) throw AppException.Conflict("nothing_to_invoice", "The whole order has already been invoiced.");
                    var amount = req.DepositAmount ?? InvoiceMath.R(order.Total * req.DepositPercent!.Value / 100m);
                    if (amount <= 0 || amount > remaining)
                        throw AppException.BadRequest("deposit_out_of_range", $"The deposit must be above zero and at most the part not yet billed ({remaining}).");
                    lines = [new InvoiceLineInput { Description = $"Deposit for order {order.OrderNumber}", Quantity = 1, UnitPrice = amount }];
                    break;

                default: // Balance
                    if (billed <= 0) throw AppException.Conflict("nothing_billed_yet", "Nothing has been invoiced for this order yet. Use Mode=Full or Deposit first.");
                    if (remaining <= 0) throw AppException.Conflict("nothing_to_invoice", "The whole order has already been invoiced.");
                    lines = [new InvoiceLineInput { Description = $"Balance for order {order.OrderNumber}", Quantity = 1, UnitPrice = remaining }];
                    break;
            }

            SetLines(invoice, lines, taxPercent);
            if (invoice.Subtotal != (mode == InvoiceKind.Standard ? order.Total : lines.Sum(l => l.UnitPrice)))
                throw AppException.Conflict("order_total_mismatch", "The order's line totals do not add up to its total. Open and re-save the order, then try again.");

            db.Invoices.Add(invoice);
            audit.Record(BusinessId, tenant.UserId, "invoice.created", nameof(Invoice), invoice.Id, new { order = order.OrderNumber, kind = mode.ToString(), invoice.Subtotal });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (await ToDtoAsync(invoice, ct), true);
        }
        catch (DbUpdateException ex) when (key is not null && IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            var winner = await FindByKeyAsync(key, ct);
            if (winner is null) throw; // the unique violation was something else
            await EnsureVisibleAsync(winner, ct);
            return (await ToDtoAsync(winner, ct), false); // a retry raced the first request
        }
    }

    // ------------------------------------------------------------------ draft lifecycle

    public async Task<InvoiceDto> UpdateDraftAsync(Guid id, UpdateInvoiceRequest req, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Locks.AcquireAsync(db, $"invoice:{id:N}", ct);
        var invoice = await LoadAsync(id, track: true, ct);
        RequireDraft(invoice);
        if (invoice.Version != req.Version)
            throw AppException.Conflict("stale_version", "This invoice was changed by someone else. Reload and try again.");
        if (req.Lines is not null && invoice.OrderId is not null)
            throw AppException.BadRequest("order_invoice_lines_locked",
                "Lines of an invoice billed from an order cannot be edited. Delete the draft and create it again.");

        invoice.DueDate = req.DueDate;
        invoice.Notes = Parse.Clean(req.Notes);
        invoice.Terms = Parse.Clean(req.Terms);

        if (req.Lines is not null)
        {
            db.InvoiceLines.RemoveRange(invoice.Lines.ToList());
            await db.SaveChangesAsync(ct); // old lines out first: line numbers are unique per invoice
            invoice.Lines.Clear();
            SetLines(invoice, req.Lines, req.TaxPercent ?? await ProfileTaxAsync(ct));
            // Entity ids are assigned client-side, so a line discovered through the collection would be tracked as an
            // existing row (UPDATE, 0 rows, concurrency error). Mark the new lines as Added explicitly.
            foreach (var line in invoice.Lines) db.Entry(line).State = EntityState.Added;
        }

        invoice.UpdatedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, "invoice.updated", nameof(Invoice), invoice.Id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("stale_version", "This invoice was changed by someone else. Reload and try again."); }
        await tx.CommitAsync(ct);
        return await ToDtoAsync(invoice, ct);
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadAsync(id, track: true, ct);
        RequireDraft(invoice);
        db.Invoices.Remove(invoice);
        audit.Record(BusinessId, tenant.UserId, "invoice.draft_deleted", nameof(Invoice), id, new { invoice.Total });
        await db.SaveChangesAsync(ct);
    }

    public async Task<InvoiceDto> IssueAsync(Guid id, CancellationToken ct = default)
    {
        var (_, today) = await BusinessAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Locks.AcquireAsync(db, $"invoice:{id:N}", ct);
        var invoice = await LoadAsync(id, track: true, ct);
        RequireDraft(invoice);
        if (invoice.Total <= 0) throw AppException.BadRequest("nothing_to_issue", "An invoice needs a total above zero.");

        if (invoice.OrderId is { } orderId)
        {
            var status = await db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Status).FirstAsync(ct);
            if (status is OrderStatus.Cancelled or OrderStatus.Draft or OrderStatus.Quoted)
                throw AppException.Conflict("order_not_billable", $"The order is {status}, so this invoice cannot be issued.");
        }

        invoice.IssueDate = today;
        invoice.DueDate ??= today.AddDays(DefaultDueDays);
        if (invoice.DueDate < invoice.IssueDate) throw AppException.BadRequest("due_date_before_issue", "The due date is before today.");

        invoice.Number = await DocumentNumbers.NextAsync(db, BusinessId, DocumentKinds.Invoice, "INV", today.Year, ct);
        invoice.Status = InvoiceStatus.Issued;
        invoice.IssuedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, "invoice.issued", nameof(Invoice), invoice.Id, new { invoice.Number, invoice.Total });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("stale_version", "This invoice was changed by someone else. Reload and try again."); }
        await tx.CommitAsync(ct);
        return await ToDtoAsync(invoice, ct);
    }

    public async Task<InvoiceDto> VoidAsync(Guid id, VoidInvoiceRequest req, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Locks.AcquireAsync(db, $"invoice:{id:N}", ct);
        var invoice = await LoadAsync(id, track: true, ct);
        if (invoice.Status == InvoiceStatus.Draft)
            throw AppException.Conflict("invoice_is_draft", "A draft was never issued. Delete it instead.");
        if (invoice.Status == InvoiceStatus.Void)
            throw AppException.Conflict("invoice_already_void", "This invoice is already void.");

        if (await db.Payments.AnyAsync(p => p.InvoiceId == id && (p.Status == Domain.Finance.PaymentStatus.Pending || p.Status == Domain.Finance.PaymentStatus.Verified), ct))
            throw AppException.Conflict("invoice_has_payments", "This invoice has payments. Reject the pending ones and reverse the verified ones first.");
        if (await db.CreditNotes.AnyAsync(c => c.InvoiceId == id, ct))
            throw AppException.Conflict("invoice_has_credit_notes", "This invoice has credit notes, so it cannot be voided.");

        invoice.Status = InvoiceStatus.Void;
        invoice.VoidedAtUtc = DateTime.UtcNow;
        invoice.VoidReason = req.Reason.Trim();
        audit.Record(BusinessId, tenant.UserId, "invoice.voided", nameof(Invoice), invoice.Id, new { invoice.Number, reason = invoice.VoidReason });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("stale_version", "This invoice was changed by someone else. Reload and try again."); }
        await tx.CommitAsync(ct);
        return await ToDtoAsync(invoice, ct);
    }

    public async Task<InvoiceDto> AddCreditNoteAsync(Guid id, CreditNoteRequest req, CancellationToken ct = default)
    {
        await entitlements.RequireFlagAsync(BusinessId, EntitlementKeys.FinancePartialPayments, ct);
        var (_, today) = await BusinessAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Locks.AcquireAsync(db, $"invoice:{id:N}", ct);
        var invoice = await LoadAsync(id, track: false, ct);
        if (invoice.Status != InvoiceStatus.Issued)
            throw AppException.Conflict("invoice_not_issued", "Credit notes apply to issued invoices only.");

        var paid = await db.Payments.Where(p => p.InvoiceId == id && p.Status == Domain.Finance.PaymentStatus.Verified).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var credited = await db.CreditNotes.Where(c => c.InvoiceId == id).SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
        var balance = InvoiceMath.Balance(invoice.Total, paid, credited);
        if (InvoiceMath.R(req.Amount) != req.Amount) throw AppException.BadRequest("invalid_amount", "Amounts can have at most 2 decimal places.");
        if (req.Amount > balance)
            throw AppException.Conflict("credit_exceeds_balance",
                $"The credit ({req.Amount}) is more than the unpaid balance ({balance}). If the customer already paid, reverse that payment (a refund) first.");

        var note = new CreditNote
        {
            BusinessId = BusinessId, InvoiceId = id, Amount = req.Amount, Reason = req.Reason.Trim(), CreatedByUserId = tenant.UserId,
            Number = await DocumentNumbers.NextAsync(db, BusinessId, DocumentKinds.CreditNote, "CN", today.Year, ct),
        };
        db.CreditNotes.Add(note);
        audit.Record(BusinessId, tenant.UserId, "invoice.credit_note", nameof(Invoice), id, new { note.Number, note.Amount, note.Reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDtoAsync(await LoadAsync(id, track: false, ct), ct);
    }

    // ------------------------------------------------------------------ helpers

    private static (decimal Balance, string? Status, bool Overdue) Derive(Invoice i, decimal paid, decimal credited, DateOnly today)
    {
        if (i.Status == InvoiceStatus.Void) return (0m, null, false);
        if (i.Status == InvoiceStatus.Draft) return (i.Total, null, false);
        var balance = InvoiceMath.Balance(i.Total, paid, credited);
        return (balance, InvoiceMath.PaymentStatus(i.Total, paid, credited), balance > 0 && i.DueDate is { } due && due < today);
    }

    private static void RequireDraft(Invoice i)
    {
        if (i.Status != InvoiceStatus.Draft)
            throw AppException.Conflict("invoice_not_draft", "Issued invoices cannot be edited. Void it and create a new one, or add a credit note.");
    }

    /// <summary>Garment lines with the order-level discount spread proportionally, to the cent.</summary>
    private static List<InvoiceLineInput> OrderLines(Order order)
    {
        var garments = order.Garments.OrderBy(g => g.LineNumber).ToList();
        var grosses = garments.Select(g => InvoiceMath.R(g.Quantity * g.UnitPrice)).ToList();
        var shares = InvoiceMath.AllocateDiscount(grosses, order.Discount);
        return garments.Select((g, i) => new InvoiceLineInput
        { Description = g.Name, Quantity = g.Quantity, UnitPrice = g.UnitPrice, Discount = shares[i] }).ToList();
    }

    /// <summary>Validates every line, computes every amount on the server and sets the invoice totals.</summary>
    private void SetLines(Invoice invoice, IEnumerable<InvoiceLineInput> inputs, decimal defaultTax)
    {
        var number = 0;
        foreach (var input in inputs)
        {
            var description = input.Description.Trim();
            var tax = input.TaxPercent ?? defaultTax;
            if (InvoiceMath.ValidateLine(description, input.Quantity, input.UnitPrice, input.Discount, tax) is { } error)
                throw AppException.BadRequest("invalid_invoice_line", error);
            var a = InvoiceMath.Line(input.Quantity, input.UnitPrice, input.Discount, tax);
            invoice.Lines.Add(new InvoiceLine
            {
                BusinessId = BusinessId, InvoiceId = invoice.Id, LineNumber = ++number, Description = description, Quantity = input.Quantity,
                UnitPrice = input.UnitPrice, Discount = input.Discount, TaxPercent = tax, NetAmount = a.Net, TaxAmount = a.Tax, LineTotal = a.Total,
            });
        }
        invoice.Subtotal = invoice.Lines.Sum(l => l.NetAmount);
        invoice.TaxTotal = invoice.Lines.Sum(l => l.TaxAmount);
        invoice.Total = invoice.Subtotal + invoice.TaxTotal;
    }

    private async Task<(InvoiceDto, bool)> SaveNewAsync(Invoice invoice, string? key, CancellationToken ct)
    {
        db.Invoices.Add(invoice);
        audit.Record(BusinessId, tenant.UserId, "invoice.created", nameof(Invoice), invoice.Id, new { invoice.Subtotal });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (key is not null && IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            var winner = (await FindByKeyAsync(key, ct))!;
            await EnsureVisibleAsync(winner, ct);
            return (await ToDtoAsync(winner, ct), false);
        }
        return (await ToDtoAsync(invoice, ct), true);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string? NormalizeKey(string? key)
    {
        var k = Parse.Clean(key);
        if (k is { Length: > 100 }) throw AppException.BadRequest("invalid_idempotency_key", "Idempotency key is too long.");
        return k;
    }

    private Task<Invoice?> FindByKeyAsync(string key, CancellationToken ct)
        => db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.IdempotencyKey == key, ct);

    private async Task<decimal> ProfileTaxAsync(CancellationToken ct)
        => await db.PricingProfiles.AsNoTracking().Select(p => (decimal?)p.TaxPercent).FirstOrDefaultAsync(ct) ?? 0m;

    private async Task<(string Currency, DateOnly Today)> BusinessAsync(CancellationToken ct)
    {
        var b = await db.Businesses.AsNoTracking().Select(x => new { x.CurrencyCode, x.TimeZoneId }).FirstAsync(ct);
        return (b.CurrencyCode, BusinessTime.Today(b.TimeZoneId));
    }

    private async Task<Invoice> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = db.Invoices.Include(i => i.Lines).Visible(db, await ScopeAsync(ct));
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw AppException.NotFound("invoice_not_found", "Invoice not found.");
    }

    private async Task<InvoiceDto> ToDtoAsync(Invoice i, CancellationToken ct)
    {
        var (_, today) = await BusinessAsync(ct);
        var customerName = await db.Customers.AsNoTracking().Where(c => c.Id == i.CustomerId).Select(c => c.FullName).FirstOrDefaultAsync(ct) ?? "";
        var orderNumber = i.OrderId is { } oid
            ? await db.Orders.AsNoTracking().Where(o => o.Id == oid).Select(o => o.OrderNumber).FirstOrDefaultAsync(ct) : null;

        var payments = await db.Payments.AsNoTracking().Where(p => p.InvoiceId == i.Id).OrderBy(p => p.CreatedAtUtc).ToListAsync(ct);
        var paymentIds = payments.Select(p => p.Id).ToList();
        var receipts = await db.Receipts.AsNoTracking().Where(r => paymentIds.Contains(r.PaymentId)).ToDictionaryAsync(r => r.PaymentId, ct);
        var credits = await db.CreditNotes.AsNoTracking().Where(c => c.InvoiceId == i.Id).OrderBy(c => c.CreatedAtUtc).ToListAsync(ct);

        var paid = payments.Where(p => p.Status == Domain.Finance.PaymentStatus.Verified).Sum(p => p.Amount);
        var pending = payments.Where(p => p.Status == Domain.Finance.PaymentStatus.Pending).Sum(p => p.Amount);
        var credited = credits.Sum(c => c.Amount);
        var (balance, status, overdue) = Derive(i, paid, credited, today);

        return new InvoiceDto(i.Id, i.Number, i.Kind.ToString(), i.Status.ToString(), i.CustomerId, customerName, i.OrderId, orderNumber,
            i.CurrencyCode, i.IssueDate, i.DueDate, i.Notes, i.Terms, i.Subtotal, i.TaxTotal, i.Total, paid, pending, credited, balance,
            status, overdue, i.VoidReason, i.Version, i.CreatedAtUtc, i.IssuedAtUtc,
            i.Lines.OrderBy(l => l.LineNumber).Select(l => new InvoiceLineDto(l.Id, l.LineNumber, l.Description, l.Quantity, l.UnitPrice,
                l.Discount, l.TaxPercent, l.NetAmount, l.TaxAmount, l.LineTotal)).ToList(),
            payments.Select(p => FinanceMapping.ToDto(p, i.Number, receipts.GetValueOrDefault(p.Id))).ToList(),
            credits.Select(c => new CreditNoteDto(c.Id, c.Number, c.Amount, c.Reason, c.CreatedAtUtc)).ToList());
    }
}
