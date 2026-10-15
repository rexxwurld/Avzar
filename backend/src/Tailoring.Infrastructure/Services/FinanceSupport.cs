using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Finance;
using Tailoring.Application.Locations;
using Tailoring.Domain.Finance;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

internal static class BusinessTime
{
    /// <summary>"Today" in the business's own time zone. Falls back to UTC if the zone id is unknown on this server.</summary>
    public static DateOnly Today(string timeZoneId)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); } catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
    }
}

internal static class Locks
{
    /// <summary>Transaction-scoped advisory lock: concurrent money operations on the same invoice/order queue up instead of racing.</summary>
    public static async Task AcquireAsync(AppDbContext db, string key, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Locks.AcquireAsync must run inside a transaction.");
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }
}

internal static class DocumentNumbers
{
    /// <summary>"INV-2026-00042". One atomic upsert; call inside the transaction that issues the document so a rollback releases nothing it should not.</summary>
    public static async Task<string> NextAsync(AppDbContext db, Guid businessId, string kind, string prefix, int year, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<long>($"""
            INSERT INTO "DocumentCounters" ("BusinessId", "Kind", "Year", "LastValue")
            VALUES ({businessId}, {kind}, {year}, 1)
            ON CONFLICT ("BusinessId", "Kind", "Year")
            DO UPDATE SET "LastValue" = "DocumentCounters"."LastValue" + 1
            RETURNING "LastValue" AS "Value"
            """).ToListAsync(ct);
        return $"{prefix}-{year}-{rows.Single():D5}";
    }
}

internal static class FinanceMapping
{
    public static PaymentDto ToDto(Payment p, string? invoiceNumber, Receipt? receipt) => new(
        p.Id, p.InvoiceId, invoiceNumber, p.CustomerId, p.Amount, p.CurrencyCode, p.Method.ToString(), p.Reference, p.ReceivedOn,
        p.Status.ToString(), p.Notes, p.StatusReason, p.RecordedByUserId, p.VerifiedByUserId, p.VerifiedAtUtc,
        receipt?.Id, receipt?.Number, p.Version, p.CreatedAtUtc);
}

/// <summary>
/// Location scope for finance records. An invoice has no location of its own: it belongs to the location of the order it was billed from.
/// A caller restricted to some locations therefore sees only invoices billed from orders at those locations (and the payments, receipts
/// and credit notes of those invoices). Invoices that are not tied to an order are visible to unrestricted callers only (fail closed).
/// A hidden record must look exactly like a missing one.
/// </summary>
internal sealed record FinanceScope(bool Restricted, Guid[] Allowed)
{
    public static readonly FinanceScope Unrestricted = new(false, Array.Empty<Guid>());

    public static async Task<FinanceScope> ForAsync(ILocationScope? locations, CancellationToken ct)
    {
        if (locations is null) return Unrestricted; // no scope service registered (tests, system flows)
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        return allowed is null ? Unrestricted : new FinanceScope(true, allowed);
    }
}

internal static class FinanceScopeExtensions
{
    public static IQueryable<Invoice> Visible(this IQueryable<Invoice> q, AppDbContext db, FinanceScope scope)
    {
        if (!scope.Restricted) return q;
        var allowed = scope.Allowed; // never null inside an EF expression
        return q.Where(i => i.OrderId != null && db.Orders.Any(o => o.Id == i.OrderId && allowed.Contains(o.LocationId)));
    }
}
