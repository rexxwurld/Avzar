using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Application.ImportExport;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Access;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class CustomerImportService(AppDbContext db, ICustomerService customers, ITenantContext tenant, IAuditLogger audit) : ICustomerImportService
{
    public const int MaxRows = 2000;
    public const int MaxExportRows = 50_000;
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<string> ExportAsync(CancellationToken ct = default)
    {
        var rows = await db.Customers.AsNoTracking().Where(c => c.Status != CustomerStatus.Merged)
            .OrderBy(c => c.FullName).Take(MaxExportRows)
            .Select(c => new { c.FullName, c.Email, c.Phone, c.AddressLine, c.City, c.Notes, c.GarmentPreferences, c.Status, c.CreatedAtUtc })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.Append(CsvWriter.Row(CustomerCsv.ExportHeader)).Append("\r\n");
        foreach (var r in rows)
            sb.Append(CsvWriter.Row(r.FullName, r.Email, r.Phone, r.AddressLine, r.City, r.Notes, r.GarmentPreferences,
                r.Status.ToString(), r.CreatedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"))).Append("\r\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ analysis (shared by preview and commit)

    private sealed record Classified(ParsedCustomerRow Row, string Outcome, string? Message, Guid? ExistingId);
    private sealed record Analysis(string Hash, IReadOnlyList<Classified> Rows, IReadOnlyList<string> UnknownColumns);

    private async Task<Analysis> AnalyzeAsync(string csv, CancellationToken ct)
    {
        var parsed = CustomerCsv.Parse(csv);
        if (parsed.Rows.Count == 0) throw AppException.BadRequest("csv_no_rows", "The file has a header but no customers.");
        if (parsed.Rows.Count > MaxRows) throw AppException.BadRequest("too_many_rows", $"Import at most {MaxRows} customers at a time.");

        var emails = parsed.Rows.Select(r => r.NormalizedEmail).Where(e => e != null).Distinct().ToList();
        var phones = parsed.Rows.Select(r => r.NormalizedPhone).Where(p => p != null).Distinct().ToList();
        var existing = await db.Customers.AsNoTracking()
            .Where(c => c.Status != CustomerStatus.Merged
                && ((c.NormalizedEmail != null && emails.Contains(c.NormalizedEmail)) || (c.NormalizedPhone != null && phones.Contains(c.NormalizedPhone))))
            .Select(c => new { c.Id, c.NormalizedEmail, c.NormalizedPhone }).ToListAsync(ct);

        var seenEmail = new HashSet<string>();
        var seenPhone = new HashSet<string>();
        var result = new List<Classified>();
        foreach (var row in parsed.Rows)
        {
            if (row.Error is not null) { result.Add(new(row, "invalid", row.Error, null)); continue; }

            if ((row.NormalizedEmail is not null && seenEmail.Contains(row.NormalizedEmail))
                || (row.NormalizedPhone is not null && seenPhone.Contains(row.NormalizedPhone)))
            { result.Add(new(row, "duplicate_in_file", "Same email or phone as an earlier row in this file.", null)); continue; }
            if (row.NormalizedEmail is not null) seenEmail.Add(row.NormalizedEmail);
            if (row.NormalizedPhone is not null) seenPhone.Add(row.NormalizedPhone);

            var byEmail = row.NormalizedEmail is null ? null : existing.FirstOrDefault(e => e.NormalizedEmail == row.NormalizedEmail);
            var byPhone = row.NormalizedPhone is null ? null : existing.FirstOrDefault(e => e.NormalizedPhone == row.NormalizedPhone);
            if (byEmail is not null && byPhone is not null && byEmail.Id != byPhone.Id)
                result.Add(new(row, "invalid", "The email and the phone belong to two different existing customers. Merge them first.", null));
            else if ((byEmail ?? byPhone) is { } match)
                result.Add(new(row, "duplicate", "Matches an existing customer.", match.Id));
            else
                result.Add(new(row, "create", null, null));
        }
        return new Analysis(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv))).ToLowerInvariant(), result, parsed.UnknownColumns);
    }

    public async Task<CsvImportPreviewDto> PreviewAsync(string csvText, CancellationToken ct = default)
    {
        var a = await AnalyzeAsync(csvText, ct);
        var problems = a.Rows.Where(r => r.Outcome != "create").Take(300)
            .Select(r => new CsvRowResultDto(r.Row.RowNumber, r.Outcome, r.Message, r.Row.Request?.FullName, r.ExistingId)).ToList();
        return new CsvImportPreviewDto(a.Hash, a.Rows.Count,
            a.Rows.Count(r => r.Outcome == "create"), a.Rows.Count(r => r.Outcome == "duplicate"),
            a.Rows.Count(r => r.Outcome == "duplicate_in_file"), a.Rows.Count(r => r.Outcome == "invalid"),
            a.UnknownColumns, a.Rows.Where(r => r.Outcome == "create").Take(10).Select(r => r.Row.Request!.FullName).ToList(), problems);
    }

    public async Task<CsvImportResultDto> CommitAsync(string csvText, CsvImportCommitOptions opt, CancellationToken ct = default)
    {
        var update = opt.OnDuplicate?.ToLowerInvariant() switch
        {
            "skip" or null or "" => false,
            "update" => true,
            _ => throw AppException.BadRequest("invalid_on_duplicate", "OnDuplicate must be skip or update.")
        };

        var a = await AnalyzeAsync(csvText, ct);
        // The user confirmed THIS file. If it differs from what they previewed, nothing happens.
        if (!string.Equals(a.Hash, opt.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict("file_changed", "This is not the file you previewed. Preview it again before importing.");

        var invalid = a.Rows.Count(r => r.Outcome == "invalid");
        if (invalid > 0 && !opt.SkipInvalidRows)
            throw new AppException(400, "import_has_errors", $"{invalid} row(s) have errors. Fix the file, or choose to skip invalid rows.",
                new Dictionary<string, object?> { ["invalidRows"] = invalid });

        var matches = a.Rows.Count(r => r.Outcome == "duplicate");
        if (update && matches > 0 && opt.ConfirmUpdateCount != matches)
            throw AppException.Conflict("confirmation_required", $"This import would overwrite {matches} existing customer(s). Confirm that number to continue.",
                new Dictionary<string, object?> { ["updateCount"] = matches });

        int created = 0, updated = 0, skipped = 0, failed = 0;
        var stopped = false;
        var problems = new List<CsvRowResultDto>();
        foreach (var item in a.Rows)
        {
            if (item.Outcome is "invalid" or "duplicate_in_file") { skipped++; Add(item, item.Outcome, item.Message); continue; }
            if (stopped) { skipped++; Add(item, "not_imported", "Not imported because the import stopped earlier."); continue; }
            try
            {
                if (item.Outcome == "create")
                {
                    await customers.CreateAsync(item.Row.Request!, ct);
                    created++;
                }
                else if (update)
                {
                    var current = await customers.GetAsync(item.ExistingId!.Value, ct);
                    var req = item.Row.Request!;
                    await customers.UpdateAsync(current.Id, new UpsertCustomerRequest
                    {
                        FullName = req.FullName,
                        Email = req.Email ?? current.Email, Phone = req.Phone ?? current.Phone,
                        AddressLine = req.AddressLine ?? current.AddressLine, City = req.City ?? current.City,
                        Notes = req.Notes ?? current.Notes, GarmentPreferences = req.GarmentPreferences ?? current.GarmentPreferences,
                        Version = current.Version, AllowDuplicate = true,   // the match is the customer being updated
                    }, ct);
                    updated++;
                }
                else { skipped++; Add(item, "duplicate", "Skipped: already exists."); }
            }
            catch (AppException ex)
            {
                failed++;
                Add(item, "failed", ex.Message);
                if (ex.Code == "plan_limit_reached") stopped = true; // every further create would fail the same way
            }
        }

        audit.Record(BusinessId, tenant.UserId, "customer.csv_imported", nameof(Customer), null,
            new { created, updated, skipped, failed, rows = a.Rows.Count });
        await db.SaveChangesAsync(ct);
        return new CsvImportResultDto(created, updated, skipped, failed, stopped, problems.Take(300).ToList());

        void Add(Classified c, string outcome, string? message)
            => problems.Add(new CsvRowResultDto(c.Row.RowNumber, outcome, message, c.Row.Request?.FullName, c.ExistingId));
    }
}

public sealed class SearchService(AppDbContext db, ITenantContext tenant, IPermissionService permissions) : ISearchService
{
    private static readonly IReadOnlyList<SearchHitDto> None = [];

    public async Task<SearchResultDto> SearchAsync(string query, int perKindLimit = 5, CancellationToken ct = default)
    {
        var businessId = tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");
        var userId = tenant.UserId ?? throw AppException.Unauthorized("no_user", "Not signed in.");
        var q = (query ?? "").Trim();
        if (q.Length < 2) throw AppException.BadRequest("query_too_short", "Type at least 2 characters.");
        if (q.Length > 80) q = q[..80];
        var limit = Math.Clamp(perKindLimit, 1, 10);

        var access = await permissions.GetAccessAsync(userId, businessId, ct)
            ?? throw AppException.Unauthorized("membership_inactive", "Your membership in this business is no longer active.");
        bool Can(string p) => access.IsOwner || access.Permissions.Contains(p);

        var like = $"%{Parse.Like(q)}%";
        var digits = Tailoring.Domain.Common.Normalize.Phone(q);
        if (digits is { Length: < 3 }) digits = null;

        // The one DbContext is not thread-safe, so the kinds run one after another. Every query is already tenant-filtered.
        var customers = !Can(Permissions.CustomersView) ? None : (await db.Customers.AsNoTracking()
            .Where(c => c.Status != CustomerStatus.Merged
                && (EF.Functions.ILike(c.FullName, like) || (c.Email != null && EF.Functions.ILike(c.Email, like))
                    || (digits != null && c.NormalizedPhone != null && c.NormalizedPhone.Contains(digits))))
            .OrderBy(c => c.FullName).Take(limit).Select(c => new { c.Id, c.FullName, c.Email, c.Phone }).ToListAsync(ct))
            .Select(c => new SearchHitDto("customer", c.Id, c.FullName, c.Email ?? c.Phone)).ToList();

        var orders = !Can(Permissions.OrdersView) ? None : (await db.Orders.AsNoTracking()
            .Where(o => EF.Functions.ILike(o.OrderNumber, like)
                || db.Customers.Any(c => c.Id == o.CustomerId && EF.Functions.ILike(c.FullName, like)))
            .OrderByDescending(o => o.CreatedAtUtc).Take(limit)
            .Select(o => new { o.Id, o.OrderNumber, o.Status, Customer = db.Customers.Where(c => c.Id == o.CustomerId).Select(c => c.FullName).FirstOrDefault() })
            .ToListAsync(ct))
            .Select(o => new SearchHitDto("order", o.Id, o.OrderNumber, $"{o.Customer} · {o.Status}")).ToList();

        var invoices = !Can(Permissions.InvoicesView) ? None : (await db.Invoices.AsNoTracking()
            .Where(i => (i.Number != null && EF.Functions.ILike(i.Number, like))
                || db.Customers.Any(c => c.Id == i.CustomerId && EF.Functions.ILike(c.FullName, like)))
            .OrderByDescending(i => i.CreatedAtUtc).Take(limit)
            .Select(i => new { i.Id, i.Number, i.Status, Customer = db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.FullName).FirstOrDefault() })
            .ToListAsync(ct))
            .Select(i => new SearchHitDto("invoice", i.Id, i.Number ?? "Draft invoice", $"{i.Customer} · {i.Status}")).ToList();

        var designs = !Can(Permissions.DesignsView) ? None : (await db.Designs.AsNoTracking()
            .Where(d => EF.Functions.ILike(d.Name, like) || (d.GarmentType != null && EF.Functions.ILike(d.GarmentType, like)))
            .OrderBy(d => d.Name).Take(limit).Select(d => new { d.Id, d.Name, d.GarmentType }).ToListAsync(ct))
            .Select(d => new SearchHitDto("design", d.Id, d.Name, d.GarmentType)).ToList();

        var inquiries = !Can(Permissions.InquiriesView) ? None : (await db.Inquiries.AsNoTracking()
            .Where(i => EF.Functions.ILike(i.Name, like) || (i.Email != null && EF.Functions.ILike(i.Email, like))
                || (i.DesignNameSnapshot != null && EF.Functions.ILike(i.DesignNameSnapshot, like)))
            .OrderByDescending(i => i.CreatedAtUtc).Take(limit).Select(i => new { i.Id, i.Name, i.Status, i.DesignNameSnapshot }).ToListAsync(ct))
            .Select(i => new SearchHitDto("inquiry", i.Id, i.Name, $"{i.DesignNameSnapshot ?? "General"} · {i.Status}")).ToList();

        return new SearchResultDto(customers, orders, invoices, designs, inquiries);
    }
}
