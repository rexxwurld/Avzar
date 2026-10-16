using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Finance;
using Tailoring.Application.Locations;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Finance;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class ExpenseService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, IEntitlementService entitlements,
    ILocationScope? locations = null) : IExpenseService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    // ------------------------------------------------------------------ categories

    public async Task<IReadOnlyList<ExpenseCategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        await GateAsync(ct);
        await EnsureDefaultCategoriesAsync(ct);
        return await db.ExpenseCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.IsActive, c.SortOrder)).ToListAsync(ct);
    }

    public async Task<ExpenseCategoryDto> CreateCategoryAsync(UpsertExpenseCategoryRequest req, CancellationToken ct = default)
    {
        await GateAsync(ct);
        var name = req.Name.Trim();
        await EnsureNameFreeAsync(name, null, ct);
        var cat = new ExpenseCategory { BusinessId = BusinessId, Name = name, IsActive = req.IsActive, SortOrder = req.SortOrder };
        db.ExpenseCategories.Add(cat);
        audit.Record(BusinessId, tenant.UserId, "expense_category.created", nameof(ExpenseCategory), cat.Id, new { name });
        await db.SaveChangesAsync(ct);
        return new ExpenseCategoryDto(cat.Id, cat.Name, cat.IsActive, cat.SortOrder);
    }

    public async Task<ExpenseCategoryDto> UpdateCategoryAsync(Guid id, UpsertExpenseCategoryRequest req, CancellationToken ct = default)
    {
        await GateAsync(ct);
        var cat = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw AppException.NotFound("category_not_found", "Category not found.");
        var name = req.Name.Trim();
        await EnsureNameFreeAsync(name, id, ct);
        cat.Name = name; cat.IsActive = req.IsActive; cat.SortOrder = req.SortOrder; // categories are deactivated, never deleted: old expenses keep pointing at them
        audit.Record(BusinessId, tenant.UserId, "expense_category.updated", nameof(ExpenseCategory), cat.Id);
        await db.SaveChangesAsync(ct);
        return new ExpenseCategoryDto(cat.Id, cat.Name, cat.IsActive, cat.SortOrder);
    }

    // ------------------------------------------------------------------ expenses

    public async Task<PagedResult<ExpenseDto>> ListAsync(ExpenseListQuery query, CancellationToken ct = default)
    {
        await GateAsync(ct);
        var q = await ScopedAsync(db.Expenses.AsNoTracking(), ct);
        if (query.LocationId is { } lid) q = q.Where(e => e.LocationId == lid);
        if (!query.IncludeVoid) q = q.Where(e => e.VoidedAtUtc == null);
        if (query.CategoryId is { } cid) q = q.Where(e => e.CategoryId == cid);
        if (query.From is { } from) q = q.Where(e => e.ExpenseDate >= from);
        if (query.To is { } to) q = q.Where(e => e.ExpenseDate <= to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{Parse.Like(query.Search.Trim())}%";
            q = q.Where(e => EF.Functions.ILike(e.Description, pattern)
                || (e.Vendor != null && EF.Functions.ILike(e.Vendor, pattern))
                || (e.Reference != null && EF.Functions.ILike(e.Reference, pattern)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(e => new { Expense = e, CategoryName = db.ExpenseCategories.Where(c => c.Id == e.CategoryId).Select(c => c.Name).First() })
            .ToListAsync(ct);
        return new PagedResult<ExpenseDto>(rows.Select(r => ToDto(r.Expense, r.CategoryName)).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<ExpenseDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        await GateAsync(ct);
        return await ToDtoAsync(await LoadAsync(id, track: false, ct), ct);
    }

    public async Task<ExpenseDto> CreateAsync(UpsertExpenseRequest req, CancellationToken ct = default)
    {
        await GateAsync(ct);
        var biz = await BusinessAsync(ct);
        await ValidateAsync(req, biz.Today, ct);
        var locationId = await ResolveLocationAsync(req.LocationId, ct);
        var expense = new Expense { BusinessId = BusinessId, CurrencyCode = biz.Currency, CreatedByUserId = tenant.UserId };
        Apply(expense, req);
        expense.LocationId = locationId;
        db.Expenses.Add(expense);
        audit.Record(BusinessId, tenant.UserId, "expense.created", nameof(Expense), expense.Id, new { expense.Amount, expense.Description, expense.LocationId });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(expense, ct);
    }

    public async Task<ExpenseDto> UpdateAsync(Guid id, UpsertExpenseRequest req, CancellationToken ct = default)
    {
        await GateAsync(ct);
        if (req.Version is null) throw AppException.BadRequest("version_required", "Include the expense's current version when updating.");
        var expense = await LoadAsync(id, track: true, ct);
        if (expense.VoidedAtUtc is not null) throw AppException.Conflict("expense_void", "A voided expense cannot be edited.");

        var biz = await BusinessAsync(ct);
        await ValidateAsync(req, biz.Today, ct);
        // Omitted = keep the current location. A changed location must be one the caller may use.
        var locationId = req.LocationId is null || req.LocationId == expense.LocationId ? expense.LocationId : await ResolveLocationAsync(req.LocationId, ct);
        Apply(expense, req);
        expense.LocationId = locationId;
        db.Entry(expense).Property(e => e.Version).OriginalValue = req.Version.Value;
        expense.UpdatedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, "expense.updated", nameof(Expense), expense.Id, new { expense.Amount, expense.LocationId });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("stale_version", "This expense was changed by someone else. Reload and try again."); }
        return await ToDtoAsync(expense, ct);
    }

    public async Task<ExpenseDto> VoidAsync(Guid id, VoidExpenseRequest req, CancellationToken ct = default)
    {
        await GateAsync(ct);
        var expense = await LoadAsync(id, track: true, ct);
        if (expense.VoidedAtUtc is not null) throw AppException.Conflict("expense_void", "This expense is already voided.");
        expense.VoidedAtUtc = DateTime.UtcNow;
        expense.VoidReason = req.Reason.Trim();
        audit.Record(BusinessId, tenant.UserId, "expense.voided", nameof(Expense), expense.Id, new { expense.Amount, reason = expense.VoidReason });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict("stale_version", "This expense was changed by someone else. Reload and try again."); }
        return await ToDtoAsync(expense, ct);
    }

    public async Task<ExpenseSummaryDto> SummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default, Guid? locationId = null)
    {
        await GateAsync(ct);
        var q = (await ScopedAsync(db.Expenses.AsNoTracking(), ct)).Where(e => e.VoidedAtUtc == null);
        if (locationId is { } lid) q = q.Where(e => e.LocationId == lid);
        if (from is { } f) q = q.Where(e => e.ExpenseDate >= f);
        if (to is { } t) q = q.Where(e => e.ExpenseDate <= t);

        var groups = await q.GroupBy(e => e.CategoryId).Select(g => new { CategoryId = g.Key, Total = g.Sum(e => e.Amount), Count = g.Count() }).ToListAsync(ct);
        var names = await db.ExpenseCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var rows = groups.Select(g => new ExpenseTotalDto(g.CategoryId, names.GetValueOrDefault(g.CategoryId, "?"), g.Total, g.Count))
            .OrderByDescending(r => r.Total).ToList();
        return new ExpenseSummaryDto((await BusinessAsync(ct)).Currency, from, to, rows.Sum(r => r.Total), rows);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Limits a query to expenses at the caller's locations. An expense with no location is business-wide and visible only to
    /// callers who see every location. A hidden expense looks exactly like a missing one.</summary>
    private async Task<IQueryable<Expense>> ScopedAsync(IQueryable<Expense> q, CancellationToken ct)
    {
        if (locations is null) return q; // no scope service registered (tests, system flows)
        var allowed = await locations.AllowedLocationIdsAsync(ct);
        return allowed is null ? q : q.Where(e => e.LocationId != null && allowed.Contains(e.LocationId.Value));
    }

    /// <summary>Checks a location chosen on a create or a move. Staff limited to some locations must pick one of theirs; the check runs
    /// before the existence check so a foreign location id is answered the same way as a real one.</summary>
    private async Task<Guid?> ResolveLocationAsync(Guid? locationId, CancellationToken ct)
    {
        if (locations is not null && await locations.AllowedLocationIdsAsync(ct) is not null)
        {
            if (locationId is null) throw AppException.Forbidden("location_forbidden", "Choose one of your locations for this expense.");
            await locations.EnsureAllowedAsync(locationId.Value, ct);
        }
        if (locationId is { } id && !await db.Locations.AnyAsync(l => l.Id == id && l.IsActive, ct))
            throw AppException.BadRequest("unknown_location", "Location not found or inactive.");
        return locationId;
    }

    private Task GateAsync(CancellationToken ct) => entitlements.RequireFlagAsync(BusinessId, EntitlementKeys.FinanceExpenses, ct);

    private async Task EnsureDefaultCategoriesAsync(CancellationToken ct)
    {
        if (await db.ExpenseCategories.AnyAsync(ct)) return;
        var order = 0;
        foreach (var name in ExpenseCategory.Defaults)
            db.ExpenseCategories.Add(new ExpenseCategory { BusinessId = BusinessId, Name = name, SortOrder = order++ });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); /* two first requests raced; the other one created them */ }
    }

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.ExpenseCategories.AnyAsync(c => c.Id != exceptId && c.Name.ToLower() == lower, ct))
            throw AppException.Conflict("category_exists", "A category with this name already exists.");
    }

    private async Task ValidateAsync(UpsertExpenseRequest req, DateOnly today, CancellationToken ct)
    {
        if (InvoiceMath.R(req.Amount) != req.Amount) throw AppException.BadRequest("invalid_amount", "Amounts can have at most 2 decimal places.");
        if (req.ExpenseDate > today) throw AppException.BadRequest("expense_in_future", "An expense cannot be dated in the future.");
        var category = await db.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CategoryId, ct)
            ?? throw AppException.BadRequest("unknown_category", "Category not found.");
        if (!category.IsActive) throw AppException.Conflict("category_inactive", $"'{category.Name}' is inactive. Pick another category or reactivate it.");
    }

    private static void Apply(Expense e, UpsertExpenseRequest req)
    {
        e.CategoryId = req.CategoryId; e.ExpenseDate = req.ExpenseDate; e.Amount = req.Amount;
        e.Description = req.Description.Trim(); e.Vendor = Parse.Clean(req.Vendor); e.Reference = Parse.Clean(req.Reference);
    }

    private async Task<Expense> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = await ScopedAsync(track ? db.Expenses.AsQueryable() : db.Expenses.AsNoTracking(), ct);
        return await q.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw AppException.NotFound("expense_not_found", "Expense not found.");
    }

    private async Task<(string Currency, DateOnly Today)> BusinessAsync(CancellationToken ct)
    {
        var b = await db.Businesses.AsNoTracking().Select(x => new { x.CurrencyCode, x.TimeZoneId }).FirstAsync(ct);
        return (b.CurrencyCode, BusinessTime.Today(b.TimeZoneId));
    }

    private async Task<ExpenseDto> ToDtoAsync(Expense e, CancellationToken ct)
        => ToDto(e, await db.ExpenseCategories.AsNoTracking().Where(c => c.Id == e.CategoryId).Select(c => c.Name).FirstAsync(ct));

    private static ExpenseDto ToDto(Expense e, string category) => new(e.Id, e.CategoryId, category, e.ExpenseDate, e.Amount, e.CurrencyCode,
        e.Description, e.Vendor, e.Reference, e.VoidedAtUtc is not null, e.VoidReason, e.Version, e.CreatedAtUtc, e.LocationId);
}
