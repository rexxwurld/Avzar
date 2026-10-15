using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Locations;
using Tailoring.Application.Customers;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Common;
using Tailoring.Domain.Customers;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class CustomerService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, IEntitlementService entitlements) : ICustomerService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");
    private Guid? ActorId => tenant.UserId;

    public async Task<PagedResult<CustomerSummaryDto>> ListAsync(CustomerListQuery query, CancellationToken ct = default)
    {
        var q = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = Parse.Value<CustomerStatus>(query.Status, "status");
            q = q.Where(c => c.Status == status);
        }
        else q = q.Where(c => c.Status != CustomerStatus.Merged);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{Parse.Like(query.Search.Trim())}%";
            var digits = Normalize.Phone(query.Search);
            q = q.Where(c => EF.Functions.ILike(c.FullName, pattern)
                          || (c.Email != null && EF.Functions.ILike(c.Email, pattern))
                          || (digits != null && c.NormalizedPhone != null && c.NormalizedPhone.Contains(digits)));
        }

        var total = await q.CountAsync(ct);

        q = (query.Sort?.ToLowerInvariant(), query.Descending) switch
        {
            ("created", false) => q.OrderBy(c => c.CreatedAtUtc),
            ("created", true) => q.OrderByDescending(c => c.CreatedAtUtc),
            (_, true) => q.OrderByDescending(c => c.FullName),
            _ => q.OrderBy(c => c.FullName),
        };

        var rows = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(c => new
            {
                c.Id, c.FullName, c.Email, c.Phone, c.Status, c.CreatedAtUtc,
                OrderCount = db.Orders.Count(o => o.CustomerId == c.Id)
            }).ToListAsync(ct);

        var items = rows.Select(r => new CustomerSummaryDto(r.Id, r.FullName, r.Email, r.Phone,
            r.Status.ToString(), r.OrderCount, r.CreatedAtUtc)).ToList();
        return new PagedResult<CustomerSummaryDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken ct = default)
        => ToDto(await LoadAsync(id, ct, track: false));

    public async Task<CustomerDto> CreateAsync(UpsertCustomerRequest req, CancellationToken ct = default)
    {
        var customer = new Customer { BusinessId = BusinessId };
        Apply(customer, req);

        if (!req.AllowDuplicate)
            await EnsureNoDuplicatesAsync(customer.NormalizedEmail, customer.NormalizedPhone, null, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await entitlements.EnsureBelowLimitAsync(BusinessId, EntitlementKeys.CustomersActiveMax,
            c => db.Customers.LongCountAsync(x => x.Status == CustomerStatus.Active, c), ct);

        db.Customers.Add(customer);
        audit.Record(BusinessId, ActorId, "customer.created", nameof(Customer), customer.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(customer);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, UpsertCustomerRequest req, CancellationToken ct = default)
    {
        if (req.Version is null)
            throw AppException.BadRequest("version_required", "Include the customer's current version when updating.");

        var customer = await LoadAsync(id, ct, track: true);
        if (customer.Status == CustomerStatus.Merged)
            throw AppException.Conflict("customer_merged", "This profile was merged into another customer and can no longer be edited.");

        var oldEmail = customer.NormalizedEmail;
        var oldPhone = customer.NormalizedPhone;
        Apply(customer, req);

        var contactChanged = customer.NormalizedEmail != oldEmail || customer.NormalizedPhone != oldPhone;
        if (contactChanged && !req.AllowDuplicate)
            await EnsureNoDuplicatesAsync(customer.NormalizedEmail, customer.NormalizedPhone, customer.Id, ct);

        db.Entry(customer).Property(c => c.Version).OriginalValue = req.Version.Value;
        customer.UpdatedAtUtc = DateTime.UtcNow; // guarantees an UPDATE, so a stale Version is always detected
        audit.Record(BusinessId, ActorId, "customer.updated", nameof(Customer), customer.Id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict("stale_version", "This customer was changed by someone else. Reload and try again.");
        }
        return ToDto(customer);
    }

    public async Task<CustomerDto> SetStatusAsync(Guid id, SetCustomerStatusRequest req, CancellationToken ct = default)
    {
        var status = Parse.Value<CustomerStatus>(req.Status, "status");
        if (status == CustomerStatus.Merged)
            throw AppException.BadRequest("invalid_status", "Use the merge action to merge customers.");

        var customer = await LoadAsync(id, ct, track: true);
        if (customer.Status == CustomerStatus.Merged)
            throw AppException.Conflict("customer_merged", "Merged profiles cannot change status.");

        // Reactivating counts against the active-customer limit; deactivating never does.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (status == CustomerStatus.Active && customer.Status != CustomerStatus.Active)
            await entitlements.EnsureBelowLimitAsync(BusinessId, EntitlementKeys.CustomersActiveMax,
                c => db.Customers.LongCountAsync(x => x.Status == CustomerStatus.Active, c), ct);

        customer.Status = status;
        audit.Record(BusinessId, ActorId, "customer.status_changed", nameof(Customer), customer.Id, new { status = status.ToString() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(customer);
    }

    public async Task<CustomerDto> MergeAsync(Guid sourceId, Guid targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId)
            throw AppException.BadRequest("same_customer", "Choose two different customers to merge.");

        var source = await LoadAsync(sourceId, ct, track: true);
        var target = await LoadAsync(targetId, ct, track: true);
        if (source.Status == CustomerStatus.Merged || target.Status == CustomerStatus.Merged)
            throw AppException.Conflict("already_merged", "One of these profiles has already been merged.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Measurement profiles: avoid unique-label clashes on the target.
        var taken = await db.MeasurementSets.Where(s => s.CustomerId == targetId).Select(s => s.Label).ToListAsync(ct);
        var moving = await db.MeasurementSets.Where(s => s.CustomerId == sourceId).ToListAsync(ct);
        foreach (var set in moving)
        {
            var label = set.Label;
            for (var n = 1; taken.Contains(label, StringComparer.Ordinal); n++)
                label = Parse.Truncate($"{set.Label} (from {source.FullName}){(n > 1 ? $" {n}" : "")}", 80);
            taken.Add(label);
            set.Label = label;
            set.CustomerId = targetId;
        }

        // Fill gaps on the surviving profile; never overwrite its existing data.
        target.Email ??= source.Email; target.NormalizedEmail ??= source.NormalizedEmail;
        target.Phone ??= source.Phone; target.NormalizedPhone ??= source.NormalizedPhone;
        target.AddressLine ??= source.AddressLine; target.City ??= source.City;

        source.Status = CustomerStatus.Merged;
        source.MergedIntoCustomerId = targetId;

        audit.Record(BusinessId, ActorId, "customer.merged", nameof(Customer), targetId, new { sourceId, targetId });
        await db.SaveChangesAsync(ct);

        await db.Orders.Where(o => o.CustomerId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(o => o.CustomerId, targetId), ct);
        await db.CustomerCommunications.Where(c => c.CustomerId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CustomerId, targetId), ct);
        await db.Fittings.Where(f => f.CustomerId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(f => f.CustomerId, targetId), ct);
        await db.Documents.Where(d => d.CustomerId == sourceId).ExecuteUpdateAsync(s => s.SetProperty(d => d.CustomerId, targetId), ct);

        await tx.CommitAsync(ct);
        return ToDto(target);
    }

    public async Task<IReadOnlyList<CommunicationDto>> ListCommunicationsAsync(Guid customerId, CancellationToken ct = default)
    {
        await LoadAsync(customerId, ct, track: false);
        var rows = await db.CustomerCommunications.AsNoTracking()
            .Where(c => c.CustomerId == customerId).OrderByDescending(c => c.CreatedAtUtc).Take(200).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<CommunicationDto> AddCommunicationAsync(Guid customerId, AddCommunicationRequest req, CancellationToken ct = default)
    {
        await LoadAsync(customerId, ct, track: false);
        var entry = new CustomerCommunication
        {
            BusinessId = BusinessId, CustomerId = customerId,
            Channel = Parse.Value<CommunicationChannel>(req.Channel, "channel"),
            Direction = Parse.Value<CommunicationDirection>(req.Direction, "direction"),
            Subject = Parse.Clean(req.Subject), Body = req.Body.Trim(), RecordedByUserId = ActorId
        };
        db.CustomerCommunications.Add(entry);
        await db.SaveChangesAsync(ct);
        return ToDto(entry);
    }

    // ---------------- helpers ----------------

    private async Task<Customer> LoadAsync(Guid id, CancellationToken ct, bool track)
    {
        var q = track ? db.Customers.AsQueryable() : db.Customers.AsNoTracking();
        // Tenant query filter makes another business's customer indistinguishable from "does not exist".
        return await q.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw AppException.NotFound("customer_not_found", "Customer not found.");
    }

    private static void Apply(Customer c, UpsertCustomerRequest r)
    {
        c.FullName = r.FullName.Trim();
        c.Email = Parse.Clean(r.Email);
        c.NormalizedEmail = Normalize.Email(r.Email);
        c.Phone = Parse.Clean(r.Phone);
        c.NormalizedPhone = Normalize.Phone(r.Phone);
        c.AddressLine = Parse.Clean(r.AddressLine);
        c.City = Parse.Clean(r.City);
        c.Notes = Parse.Clean(r.Notes);
        c.GarmentPreferences = Parse.Clean(r.GarmentPreferences);
    }

    private async Task EnsureNoDuplicatesAsync(string? email, string? phone, Guid? excludeId, CancellationToken ct)
    {
        if (email is null && phone is null) return;

        var q = db.Customers.AsNoTracking().Where(c => c.Status != CustomerStatus.Merged
            && ((email != null && c.NormalizedEmail == email) || (phone != null && c.NormalizedPhone == phone)));
        if (excludeId is { } ex) q = q.Where(c => c.Id != ex);

        var rows = await q.Take(5).ToListAsync(ct);
        if (rows.Count == 0) return;

        var candidates = rows.Select(c => new DuplicateCandidateDto(c.Id, c.FullName, c.Email, c.Phone,
            email != null && c.NormalizedEmail == email ? "email" : "phone")).ToList();
        throw AppException.Conflict("possible_duplicate",
            "A customer with the same email or phone already exists. Open the existing profile, or resubmit with allowDuplicate=true.",
            new Dictionary<string, object?> { ["candidates"] = candidates });
    }

    private static CustomerDto ToDto(Customer c) => new(c.Id, c.FullName, c.Email, c.Phone, c.AddressLine, c.City,
        c.Notes, c.GarmentPreferences, c.Status.ToString(), c.MergedIntoCustomerId, c.Version, c.CreatedAtUtc);

    private static CommunicationDto ToDto(CustomerCommunication c) => new(c.Id, c.Channel.ToString(),
        c.Direction.ToString(), c.Subject, c.Body, c.RecordedByUserId, c.CreatedAtUtc);
}

public sealed class FittingService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, IEntitlementService entitlements,
    ILocationScope? locations = null) : IFittingService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<PagedResult<FittingDto>> ListAsync(FittingListQuery query, CancellationToken ct = default)
    {
        var q = db.Fittings.AsNoTracking().AsQueryable();
        if (locations is not null && await locations.AllowedLocationIdsAsync(ct) is { } allowed)
            q = q.Where(f => allowed.Contains(f.LocationId));
        if (query.CustomerId is { } cid) q = q.Where(f => f.CustomerId == cid);
        if (query.OrderId is { } oid) q = q.Where(f => f.OrderId == oid);
        if (query.From is { } fromValue) { var f0 = fromValue.UtcDateTime; q = q.Where(f => f.ScheduledAtUtc >= f0); }
        if (query.To is { } toValue) { var t0 = toValue.UtcDateTime; q = q.Where(f => f.ScheduledAtUtc < t0); }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(f => f.ScheduledAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<FittingDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<FittingDto> CreateAsync(CreateFittingRequest req, CancellationToken ct = default)
    {
        var when = req.ScheduledAt.UtcDateTime;
        if (when <= DateTime.UtcNow)
            throw AppException.BadRequest("fitting_in_past", "A fitting must be scheduled in the future.");

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CustomerId, ct)
            ?? throw AppException.NotFound("customer_not_found", "Customer not found.");
        if (customer.Status != CustomerStatus.Active)
            throw AppException.Conflict("customer_not_active", "Fittings can only be booked for active customers.");

        if (!await db.Locations.AnyAsync(l => l.Id == req.LocationId && l.IsActive, ct))
            throw AppException.BadRequest("unknown_location", "Location not found or inactive.");
        if (locations is not null) await locations.EnsureAllowedAsync(req.LocationId, ct);

        var allowedOrNull = locations is null ? null : await locations.AllowedLocationIdsAsync(ct);
        var restricted = allowedOrNull is not null;
        var allowedIds = allowedOrNull ?? Array.Empty<Guid>(); // never a null collection inside an EF expression
        if (req.OrderId is { } orderId &&
            !await db.Orders.AnyAsync(o => o.Id == orderId && o.CustomerId == req.CustomerId
                && (!restricted || allowedIds.Contains(o.LocationId)), ct))
            throw AppException.BadRequest("unknown_order", "That order does not exist for this customer.");

        var fitting = new FittingAppointment
        {
            BusinessId = BusinessId, CustomerId = req.CustomerId, OrderId = req.OrderId, LocationId = req.LocationId,
            ScheduledAtUtc = when, DurationMinutes = req.DurationMinutes, Notes = Parse.Clean(req.Notes),
            CreatedByUserId = tenant.UserId
        };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await entitlements.ConsumeAsync(BusinessId, EntitlementKeys.AppointmentsMonthlyMax, 1, ct);
        db.Fittings.Add(fitting);
        audit.Record(BusinessId, tenant.UserId, "fitting.created", nameof(FittingAppointment), fitting.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(fitting);
    }

    public async Task<FittingDto> SetStatusAsync(Guid id, SetFittingStatusRequest req, CancellationToken ct = default)
    {
        var status = Parse.Value<FittingStatus>(req.Status, "status");
        var fittings = db.Fittings.AsQueryable();
        if (locations is not null && await locations.AllowedLocationIdsAsync(ct) is { } allowed)
            fittings = fittings.Where(f => allowed.Contains(f.LocationId));
        var fitting = await fittings.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw AppException.NotFound("fitting_not_found", "Fitting not found.");
        if (fitting.Status is FittingStatus.Completed && status != FittingStatus.Completed)
            throw AppException.Conflict("fitting_closed", "A completed fitting cannot be reopened.");

        fitting.Status = status;
        if (Parse.Clean(req.Notes) is { } notes) fitting.Notes = notes;
        audit.Record(BusinessId, tenant.UserId, "fitting.status_changed", nameof(FittingAppointment), fitting.Id, new { status = status.ToString() });
        await db.SaveChangesAsync(ct);
        return ToDto(fitting);
    }

    private static FittingDto ToDto(FittingAppointment f) => new(f.Id, f.CustomerId, f.OrderId, f.LocationId,
        DateTime.SpecifyKind(f.ScheduledAtUtc, DateTimeKind.Utc), f.DurationMinutes, f.Status.ToString(), f.Notes);
}
