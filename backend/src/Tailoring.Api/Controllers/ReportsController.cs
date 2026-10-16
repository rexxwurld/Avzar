using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using Tailoring.Api.Security;
using Tailoring.Application.Common;
using Tailoring.Domain.Access;
using Tailoring.Application.Locations;
using Tailoring.Domain.Finance;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Api.Controllers;

public sealed record ReportTotalsDto(DateOnly From, DateOnly To, string CurrencyCode, int OrdersCreated, int OrdersDelivered, decimal OrderValue, decimal VerifiedRevenue, decimal Expenses, decimal GrossAfterExpenses, int OpenTasks, int CompletedTasks, int OverdueTasks, IReadOnlyList<ReportSeriesPointDto> Daily);
public sealed record ReportSeriesPointDto(DateOnly Date, decimal VerifiedRevenue, decimal Expenses, int Orders);

[ApiController, Route("api/reports")]
public sealed class ReportsController(AppDbContext db, Tailoring.Application.Abstractions.ITenantContext tenant, Tailoring.Application.Billing.IEntitlementService entitlements, IDistributedCache cache, ILocationScope locations) : ControllerBase
{
    [HttpGet("summary"), HasPermission(Permissions.ReportsView)]
    public async Task<ReportTotalsDto> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var businessId = tenant.BusinessId ?? throw AppException.Unauthorized("business_required", "Select a business first.");
        await entitlements.RequireTierAsync(businessId, Tailoring.Domain.Billing.EntitlementKeys.ReportsLevel, "basic", ct);
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-29));
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (end < start || end.DayNumber - start.DayNumber > 366) throw AppException.BadRequest("invalid_report_range", "Choose a date range of at most 367 days, with the end date on or after the start date.");
        var startUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var allowedLocations = await locations.AllowedLocationIdsAsync(ct);
        var scopeKey = allowedLocations is null ? "all" : string.Join('-', allowedLocations.OrderBy(x => x).Select(x => x.ToString("N")));
        var cacheKey = $"report-summary:{businessId:N}:{scopeKey}:{start:yyyyMMdd}:{end:yyyyMMdd}";
        var cached = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null) return JsonSerializer.Deserialize<ReportTotalsDto>(cached)!;
        var orders = db.Orders.Where(x => x.CreatedAtUtc >= startUtc && x.CreatedAtUtc < endUtc);
        var deliveredOrders = db.Orders.Where(x => x.Status == OrderStatus.Delivered && x.DeliveredAtUtc >= startUtc && x.DeliveredAtUtc < endUtc);
        var taskRows = db.ProductionTasks.AsQueryable();
        var payments = db.Payments.Where(x => x.Status == PaymentStatus.Verified && x.VerifiedAtUtc >= startUtc && x.VerifiedAtUtc < endUtc);
        var expenses = db.Expenses.Where(x => x.ExpenseDate >= start && x.ExpenseDate <= end && x.VoidedAtUtc == null);
        if (allowedLocations is not null)
        {
            orders = orders.Where(x => allowedLocations.Contains(x.LocationId));
            deliveredOrders = deliveredOrders.Where(x => allowedLocations.Contains(x.LocationId));
            // A restricted member sees only receipts attached to an order at one of their assigned locations.
            payments = payments.Where(p => db.Invoices.Any(i => i.Id == p.InvoiceId && i.OrderId.HasValue && db.Orders.Any(o => o.Id == i.OrderId.Value && allowedLocations.Contains(o.LocationId))));
            expenses = expenses.Where(x => x.LocationId.HasValue && allowedLocations.Contains(x.LocationId.Value));
            taskRows = taskRows.Where(t => db.Orders.Any(o => o.Id == t.OrderId && allowedLocations.Contains(o.LocationId)));
        }
        var currency = await db.Businesses.Select(x => x.CurrencyCode).SingleAsync(ct);
        var ordersCreated = await orders.CountAsync(ct);
        var ordersDelivered = await deliveredOrders.CountAsync(ct);
        var orderValue = await orders.Where(x => x.Status != OrderStatus.Draft && x.Status != OrderStatus.Quoted && x.Status != OrderStatus.Cancelled).SumAsync(x => (decimal?)x.Total, ct) ?? 0m;
        var revenue = await payments.SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var expenseTotal = await expenses.SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var open = await taskRows.CountAsync(x => x.Status != ProductionTaskStatus.Completed && x.Status != ProductionTaskStatus.Cancelled, ct);
        var completed = await taskRows.CountAsync(x => x.Status == ProductionTaskStatus.Completed && x.CompletedAtUtc >= startUtc && x.CompletedAtUtc < endUtc, ct);
        var overdue = await taskRows.CountAsync(x => x.Status != ProductionTaskStatus.Completed && x.Status != ProductionTaskStatus.Cancelled && x.DeadlineUtc < DateTime.UtcNow, ct);
        var revenueRows = await payments.Select(x => new { x.VerifiedAtUtc, x.Amount }).ToListAsync(ct);
        var dailyRevenue = revenueRows.GroupBy(x => DateOnly.FromDateTime(x.VerifiedAtUtc!.Value)).Select(g => new { Date = g.Key, Amount = g.Sum(x => x.Amount) }).ToList();
        var dailyExpenses = await expenses.GroupBy(x => x.ExpenseDate).Select(g => new { Date = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var orderRows = await orders.Select(x => x.CreatedAtUtc).ToListAsync(ct);
        var dailyOrders = orderRows.GroupBy(x => DateOnly.FromDateTime(x)).Select(g => new { Date = g.Key, Count = g.Count() }).ToList();
        var revenueByDay = dailyRevenue.ToDictionary(x => x.Date, x => x.Amount);
        var expenseByDay = dailyExpenses.ToDictionary(x => x.Date, x => x.Amount);
        var ordersByDay = dailyOrders.ToDictionary(x => x.Date, x => x.Count);
        var daily = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(i => start.AddDays(i)).Select(d => new ReportSeriesPointDto(d, revenueByDay.GetValueOrDefault(d), expenseByDay.GetValueOrDefault(d), ordersByDay.GetValueOrDefault(d))).ToList();
        var result = new ReportTotalsDto(start, end, currency, ordersCreated, ordersDelivered, orderValue, revenue, expenseTotal, revenue - expenseTotal, open, completed, overdue, daily);
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, ct);
        return result;
    }
}
