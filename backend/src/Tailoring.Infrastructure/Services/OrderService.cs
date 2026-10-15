using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Application.Orders;
using Tailoring.Application.Pricing;
using Tailoring.Application.Production;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class OrderService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, IProductionPlanner planner, IEntitlementService entitlements,
    IPricingService pricing, IPermissionService permissions) : IOrderService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    // ------------------------------------------------------------------ queries

    public async Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListQuery query, CancellationToken ct = default)
    {
        var q = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = Parse.Value<OrderStatus>(query.Status, "status");
            q = q.Where(o => o.Status == status);
        }
        if (query.CustomerId is { } cid) q = q.Where(o => o.CustomerId == cid);
        if (query.LocationId is { } lid) q = q.Where(o => o.LocationId == lid);
        if (query.DueFrom is { } dueFrom) q = q.Where(o => o.PromisedDate >= dueFrom);
        if (query.DueTo is { } dueTo) q = q.Where(o => o.PromisedDate <= dueTo);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{Parse.Like(query.Search.Trim())}%";
            q = q.Where(o => EF.Functions.ILike(o.OrderNumber, pattern)
                || db.Customers.Any(c => c.Id == o.CustomerId && EF.Functions.ILike(c.FullName, pattern)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(o => o.PromisedDate).ThenByDescending(o => o.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.Status, o.CustomerId, o.PromisedDate, o.Priority, o.Total, o.CurrencyCode,
                CustomerName = db.Customers.Where(c => c.Id == o.CustomerId).Select(c => c.FullName).FirstOrDefault(),
                GarmentCount = o.Garments.Count
            }).ToListAsync(ct);

        var items = rows.Select(r => new OrderSummaryDto(r.Id, r.OrderNumber, r.Status.ToString(), r.CustomerId,
            r.CustomerName ?? "", r.PromisedDate, r.Priority.ToString(), r.Total, r.CurrencyCode, r.GarmentCount)).ToList();
        return new PagedResult<OrderSummaryDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<OrderDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Garments).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw AppException.NotFound("order_not_found", "Order not found.");
        return await ToDtoAsync(order, ct);
    }

    public async Task<OrderPricingDto> GetPricingAsync(Guid id, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Garments).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw AppException.NotFound("order_not_found", "Order not found.");
        var lines = order.Garments.OrderBy(g => g.LineNumber)
            .Select(g => new OrderLinePricingDto(g.Id, g.LineNumber, g.Name, g.Quantity, g.UnitPrice, g.DesignPriceSnapshot,
                g.PriceOverrideReason, ParseSnapshot(g.PricingSnapshotJson))).ToList();
        return new OrderPricingDto(order.Id, order.OrderNumber, order.CurrencyCode, lines);
    }

    // ------------------------------------------------------------------ create

    public async Task<(OrderDto Order, bool Created)> CreateAsync(CreateOrderRequest req, string? idempotencyKey, CancellationToken ct = default)
    {
        var key = Parse.Clean(idempotencyKey);
        if (key is { Length: > 100 }) throw AppException.BadRequest("invalid_idempotency_key", "Idempotency key is too long.");

        if (key is not null && await FindByKeyAsync(key, ct) is { } already)
            return (await ToDtoAsync(already, ct), false);

        var (currency, today) = await GetBusinessContextAsync(ct);
        var priority = Parse.Value<OrderPriority>(req.Priority, "priority");
        var delivery = Parse.Value<DeliveryMethod>(req.DeliveryMethod, "delivery_method");
        ValidateHeader(req.PromisedDate, today, delivery, req.DeliveryAddress, checkDate: true);

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CustomerId, ct)
            ?? throw AppException.BadRequest("unknown_customer", "Customer not found.");
        if (customer.Status != CustomerStatus.Active)
            throw AppException.Conflict("customer_not_active", "Orders can only be created for active customers.");
        await EnsureLocationAsync(req.LocationId, ct);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Inside the order transaction: a failed or rolled-back order never burns monthly quota.
            await entitlements.ConsumeAsync(BusinessId, EntitlementKeys.OrdersMonthlyMax, 1, ct);

            var order = new Order
            {
                BusinessId = BusinessId, LocationId = req.LocationId, CustomerId = customer.Id, CurrencyCode = currency,
                IdempotencyKey = key, CreatedByUserId = tenant.UserId
            };
            ApplyHeader(order, req, priority, delivery);

            for (var i = 0; i < req.Garments.Count; i++)
                order.Garments.Add(await BuildGarmentAsync(order, req.Garments[i], i + 1, customer.Id, existing: null, ct));
            ApplyTotals(order, req.Discount);

            order.OrderNumber = await NextOrderNumberAsync(ct);
            db.Orders.Add(order);
            audit.Record(BusinessId, tenant.UserId, "order.created", nameof(Order), order.Id,
                new { order.OrderNumber, order.Total, order.CurrencyCode });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return (await ToDtoAsync(order, ct), true);
        }
        catch (DbUpdateException) when (key is not null)
        {
            // A concurrent retry with the same key won the unique index; return its order instead of failing.
            db.ChangeTracker.Clear();
            if (await FindByKeyAsync(key, ct) is { } winner) return (await ToDtoAsync(winner, ct), false);
            throw;
        }
    }

    // ------------------------------------------------------------------ update

    public async Task<OrderDto> UpdateAsync(Guid id, UpdateOrderRequest req, CancellationToken ct = default)
    {
        var order = await db.Orders.Include(o => o.Garments).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw AppException.NotFound("order_not_found", "Order not found.");
        if (order.Status is not (OrderStatus.Draft or OrderStatus.Quoted))
            throw AppException.Conflict("order_locked", "Only draft or quoted orders can be edited here. Confirmed orders need an amendment (not built yet).");

        var (_, today) = await GetBusinessContextAsync(ct);
        var priority = Parse.Value<OrderPriority>(req.Priority, "priority");
        var delivery = Parse.Value<DeliveryMethod>(req.DeliveryMethod, "delivery_method");
        ValidateHeader(req.PromisedDate, today, delivery, req.DeliveryAddress, checkDate: req.PromisedDate != order.PromisedDate);
        if (req.LocationId != order.LocationId) await EnsureLocationAsync(req.LocationId, ct);

        db.Entry(order).Property(o => o.Version).OriginalValue = req.Version;

        var existingById = order.Garments.ToDictionary(g => g.Id);
        var kept = new HashSet<Guid>();
        for (var i = 0; i < req.Garments.Count; i++)
        {
            var input = req.Garments[i];
            if (input.Id is { } gid)
            {
                if (!existingById.TryGetValue(gid, out var existing))
                    throw AppException.BadRequest("unknown_line", "A garment line in the request does not belong to this order.");
                if (!kept.Add(gid))
                    throw AppException.BadRequest("duplicate_line", "The same garment line appears twice.");
                await BuildGarmentAsync(order, input, i + 1, order.CustomerId, existing, ct);
            }
            else
            {
                order.Garments.Add(await BuildGarmentAsync(order, input, i + 1, order.CustomerId, existing: null, ct));
            }
        }
        foreach (var removed in order.Garments.Where(g => existingById.ContainsKey(g.Id) && !kept.Contains(g.Id)).ToList())
        {
            order.Garments.Remove(removed);   // so totals below only see surviving lines
            db.OrderGarments.Remove(removed);
        }

        ApplyHeader(order, req, priority, delivery);
        ApplyTotals(order, req.Discount);

        // Editing a quoted order invalidates the quote: it must be re-quoted explicitly.
        var wasQuoted = order.Status == OrderStatus.Quoted;
        if (wasQuoted) { order.Status = OrderStatus.Draft; order.QuotedAtUtc = null; }

        order.UpdatedAtUtc = DateTime.UtcNow; // guarantees an UPDATE, so a stale Version is always detected
        audit.Record(BusinessId, tenant.UserId, "order.updated", nameof(Order), order.Id, new { order.Total, requoteRequired = wasQuoted });
        await SaveWithConcurrencyAsync(ct);
        return await ToDtoAsync(order, ct);
    }

    // ------------------------------------------------------------------ status transitions

    public async Task<OrderDto> QuoteAsync(Guid id, CancellationToken ct = default)
    {
        var order = await LoadForTransitionAsync(id, ct);
        if (order.Status != OrderStatus.Draft)
            throw AppException.Conflict("invalid_transition", $"Only draft orders can be quoted (this one is {order.Status}).");
        if (order.Garments.Count == 0 || order.Total <= 0)
            throw AppException.BadRequest("nothing_to_quote", "Add priced garments before issuing a quotation.");

        order.Status = OrderStatus.Quoted;
        order.QuotedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, "order.quoted", nameof(Order), order.Id, new { order.Total });
        await SaveWithConcurrencyAsync(ct);
        return await ToDtoAsync(order, ct);
    }

    public async Task<OrderDto> ConfirmAsync(Guid id, CancellationToken ct = default)
    {
        var order = await LoadForTransitionAsync(id, ct);
        if (order.Status != OrderStatus.Quoted)
            throw AppException.Conflict("invalid_transition", $"Only quoted orders can be confirmed (this one is {order.Status}).");

        var (_, today) = await GetBusinessContextAsync(ct);
        if (order.PromisedDate < today)
            throw AppException.BadRequest("promised_date_passed", "The promised date has passed. Update the order before confirming.");

        var customerActive = await db.Customers.AnyAsync(c => c.Id == order.CustomerId && c.Status == CustomerStatus.Active, ct);
        if (!customerActive)
            throw AppException.Conflict("customer_not_active", "The customer profile is not active.");

        // Phase 6 will add the deposit/payment policy here.
        order.Status = OrderStatus.Confirmed;
        order.ConfirmedAtUtc = DateTime.UtcNow;

        // Tasks are generated in the same unit of work: the order is never confirmed without its tasks, or vice versa.
        var plan = await planner.GenerateAsync(order.Id, null, ct);
        audit.Record(BusinessId, tenant.UserId, "order.confirmed", nameof(Order), order.Id,
            new { order.OrderNumber, order.Total, plan.TasksCreated, plan.GarmentsWithoutWorkflow });
        await SaveWithConcurrencyAsync(ct);
        return await ToDtoAsync(order, ct);
    }

    public async Task<OrderDto> CancelAsync(Guid id, CancelOrderRequest req, CancellationToken ct = default)
    {
        var order = await LoadForTransitionAsync(id, ct);
        if (order.Status == OrderStatus.Cancelled)
            throw AppException.Conflict("invalid_transition", "This order is already cancelled.");

        if (order.Status == OrderStatus.Delivered)
            throw AppException.Conflict("invalid_transition", "A delivered order cannot be cancelled. Record a refund instead (Phase 6).");

        // Money must be settled first: a cancelled order cannot keep live invoices or payments.
        if (await db.Invoices.AnyAsync(i => i.OrderId == order.Id && i.Status != Tailoring.Domain.Finance.InvoiceStatus.Void, ct))
            throw AppException.Conflict("order_has_invoices",
                "This order has invoices. Void them first (reverse any payments before that), then cancel the order.");

        var previous = order.Status;
        await planner.CancelOpenTasksAsync(order.Id, $"Order cancelled: {req.Reason.Trim()}", ct);
        order.Status = OrderStatus.Cancelled;
        order.CancelledAtUtc = DateTime.UtcNow;
        order.CancellationReason = req.Reason.Trim();
        audit.Record(BusinessId, tenant.UserId, "order.cancelled", nameof(Order), order.Id,
            new { from = previous.ToString(), reason = order.CancellationReason });
        await SaveWithConcurrencyAsync(ct);
        return await ToDtoAsync(order, ct);
    }

    public async Task<OrderDto> DeliverAsync(Guid id, CancellationToken ct = default)
    {
        var order = await LoadForTransitionAsync(id, ct);
        if (order.Status != OrderStatus.Ready)
            throw AppException.Conflict("invalid_transition",
                $"Only orders that are Ready can be delivered (this one is {order.Status}). Finish the production tasks first.");

        // Defence in depth: Ready is derived from tasks, but re-verify before handing the garment over.
        var open = await db.ProductionTasks.CountAsync(t => t.OrderId == id
            && t.Status != ProductionTaskStatus.Completed && t.Status != ProductionTaskStatus.Cancelled, ct);
        if (open > 0)
            throw AppException.Conflict("tasks_incomplete", $"{open} production task(s) are still open.");

        order.Status = OrderStatus.Delivered;
        order.DeliveredAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, "order.delivered", nameof(Order), order.Id, new { order.OrderNumber });
        await SaveWithConcurrencyAsync(ct);
        return await ToDtoAsync(order, ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Order> LoadForTransitionAsync(Guid id, CancellationToken ct)
        => await db.Orders.Include(o => o.Garments).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw AppException.NotFound("order_not_found", "Order not found.");

    private async Task SaveWithConcurrencyAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict("stale_version", "This order was changed by someone else. Reload and try again.");
        }
    }

    private async Task<Order?> FindByKeyAsync(string key, CancellationToken ct)
        => await db.Orders.AsNoTracking().Include(o => o.Garments).FirstOrDefaultAsync(o => o.IdempotencyKey == key, ct);

    private async Task<(string Currency, DateOnly Today)> GetBusinessContextAsync(CancellationToken ct)
    {
        var b = await db.Businesses.AsNoTracking().Select(x => new { x.CurrencyCode, x.TimeZoneId }).FirstAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(b.TimeZoneId);
        return (b.CurrencyCode, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone)));
    }

    private async Task EnsureLocationAsync(Guid locationId, CancellationToken ct)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == locationId && l.IsActive, ct))
            throw AppException.BadRequest("unknown_location", "Location not found or inactive.");
    }

    private static void ValidateHeader(DateOnly promised, DateOnly today, DeliveryMethod method, string? address, bool checkDate)
    {
        if (checkDate && promised < today)
            throw AppException.BadRequest("promised_date_in_past", "The promised date cannot be in the past.");
        if (method == DeliveryMethod.Delivery && string.IsNullOrWhiteSpace(address))
            throw AppException.BadRequest("delivery_address_required", "A delivery address is required for delivery orders.");
    }

    private static void ApplyHeader(Order o, OrderInputBase r, OrderPriority priority, DeliveryMethod delivery)
    {
        o.LocationId = r.LocationId;
        o.PromisedDate = r.PromisedDate;
        o.Priority = priority;
        o.DeliveryMethod = delivery;
        o.DeliveryAddress = Parse.Clean(r.DeliveryAddress);
        o.DeliveryInstructions = Parse.Clean(r.DeliveryInstructions);
        o.Notes = Parse.Clean(r.Notes);
    }

    private static decimal Money(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>The only place totals are computed. Client-supplied totals do not exist in the contract.</summary>
    private static void ApplyTotals(Order order, decimal requestedDiscount)
    {
        if (Money(requestedDiscount) != requestedDiscount)
            throw AppException.BadRequest("invalid_discount", "Discount can have at most 2 decimal places.");
        var subtotal = order.Garments.Sum(g => g.LineTotal);
        if (requestedDiscount > subtotal)
            throw AppException.BadRequest("discount_exceeds_total", "The discount cannot exceed the order subtotal.");
        order.Subtotal = subtotal;
        order.Discount = requestedDiscount;
        order.Total = subtotal - requestedDiscount;
    }

    private async Task<OrderGarment> BuildGarmentAsync(Order order, OrderGarmentInput input, int line, Guid customerId,
        OrderGarment? existing, CancellationToken ct)
    {
        if (Money(input.UnitPrice) != input.UnitPrice)
            throw AppException.BadRequest("invalid_price", $"'{input.Name}': price can have at most 2 decimal places.");

        var g = existing ?? new OrderGarment { BusinessId = BusinessId, OrderId = order.Id };
        g.LineNumber = line;
        g.Name = input.Name.Trim();
        g.Category = Parse.Clean(input.Category);
        g.Quantity = input.Quantity;
        g.UnitPrice = input.UnitPrice;
        g.LineTotal = Money(input.Quantity * input.UnitPrice);
        g.Fabric = Parse.Clean(input.Fabric);
        g.Colour = Parse.Clean(input.Colour);
        g.StyleInstructions = Parse.Clean(input.StyleInstructions);
        g.AlterationNotes = Parse.Clean(input.AlterationNotes);

        if (input.MeasurementSetId is null)
        {
            g.MeasurementSetId = null; g.MeasurementRevision = null; g.MeasurementSnapshotJson = null;
        }
        else
        {
            // An existing line keeps its frozen snapshot unless the caller explicitly picks a different profile/revision.
            var unchanged = existing is not null && existing.MeasurementSetId == input.MeasurementSetId
                && (input.MeasurementRevision is null || input.MeasurementRevision == existing.MeasurementRevision)
                && existing.MeasurementSnapshotJson is not null;
            if (!unchanged)
            {
                var templateName = await SnapshotAsync(g, input.MeasurementSetId.Value, input.MeasurementRevision, customerId, ct);
                g.Category ??= templateName;
            }
        }

        await ApplyDesignAsync(g, input, existing, ct);
        await ApplyPricingAsync(g, input, existing, ct);
        return g;
    }

    private async Task ApplyDesignAsync(OrderGarment g, OrderGarmentInput input, OrderGarment? existing, CancellationToken ct)
    {
        if (input.DesignId is null) { g.DesignId = null; g.DesignNameSnapshot = null; g.DesignPriceSnapshot = null; return; }
        if (existing?.DesignId == input.DesignId) return; // unchanged: keep the snapshot taken when it was linked

        // Tenant filter: another business's design is simply "not found".
        var design = await db.Designs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == input.DesignId, ct)
            ?? throw AppException.BadRequest("unknown_design", "That design was not found.");
        if (design.Status != DesignStatus.Active)
            throw AppException.Conflict("design_archived", $"'{design.Name}' is archived. Restore it before using it on an order.");

        g.DesignId = design.Id;
        g.DesignNameSnapshot = design.Name;
        g.DesignPriceSnapshot = design.BasePrice;
    }

    private async Task ApplyPricingAsync(OrderGarment g, OrderGarmentInput input, OrderGarment? existing, CancellationToken ct)
    {
        if (input.Pricing is null)
        {
            if (!string.IsNullOrWhiteSpace(input.PriceOverrideReason))
                throw AppException.BadRequest("override_without_pricing", "A price override reason only applies when the price comes from the calculator.");
            // Manual price. A snapshot that no longer explains the price would mislead, so drop it when the price changed.
            if (existing?.PricingSnapshotJson is not null && existing.UnitPrice != input.UnitPrice)
            { g.PricingSnapshotJson = null; g.PriceOverrideReason = null; }
            return;
        }

        await RequirePricingAccessAsync(ct);
        var calc = await pricing.CalculateAsync(input.Pricing, ct); // recomputed here; the client never supplies results
        var suggested = calc.Result.UnitPrice;

        var reason = Parse.Clean(input.PriceOverrideReason);
        if (input.UnitPrice != suggested)
        {
            if (reason is null || reason.Length < 3)
                throw AppException.BadRequest("override_reason_required",
                    $"'{input.Name}': the price {input.UnitPrice} differs from the calculated {suggested}. Give a reason for the change.");
        }
        else reason = null;

        g.PriceOverrideReason = reason;
        g.PricingSnapshotJson = JsonSerializer.Serialize(new
        {
            calculatedAtUtc = DateTime.UtcNow, input = input.Pricing, rules = calc.Rules, result = calc.Result,
            suggestedUnitPrice = suggested, finalUnitPrice = input.UnitPrice,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private async Task RequirePricingAccessAsync(CancellationToken ct)
    {
        var access = tenant.UserId is { } userId ? await permissions.GetAccessAsync(userId, BusinessId, ct) : null;
        if (access is null || !(access.IsOwner || access.Permissions.Contains(Permissions.PricingView)))
            throw AppException.Forbidden("permission_denied", "You need the pricing permission to use the calculator on an order.");
    }

    private async Task<string> SnapshotAsync(OrderGarment g, Guid setId, int? revision, Guid customerId, CancellationToken ct)
    {
        var set = await db.MeasurementSets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == setId && s.CustomerId == customerId, ct)
            ?? throw AppException.BadRequest("unknown_measurement_set", "That measurement profile does not belong to this customer.");
        var number = revision ?? set.CurrentRevision;
        var rev = await db.MeasurementRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.SetId == setId && r.RevisionNumber == number, ct)
            ?? throw AppException.BadRequest("unknown_revision", $"Revision {number} does not exist for that measurement profile.");
        var templateName = await db.MeasurementTemplates.AsNoTracking().Where(t => t.Id == set.TemplateId).Select(t => t.Name).FirstAsync(ct);

        g.MeasurementSetId = set.Id;
        g.MeasurementRevision = rev.RevisionNumber;
        g.MeasurementSnapshotJson = JsonSerializer.Serialize(new
        {
            templateName,
            profile = set.Label,
            revision = rev.RevisionNumber,
            recordedAtUtc = rev.CreatedAtUtc,
            notes = rev.Notes,
            values = MeasurementJson.Deserialize(rev.ValuesJson)
        }, MeasurementJson.Options);
        return templateName;
    }

    /// <summary>Atomic per-business, per-year counter. Runs inside the order transaction, so a failed order leaves no gap.</summary>
    private async Task<string> NextOrderNumberAsync(CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var rows = await db.Database.SqlQuery<long>($"""
            INSERT INTO "OrderCounters" ("BusinessId", "Year", "LastValue") VALUES ({BusinessId}, {year}, 1)
            ON CONFLICT ("BusinessId", "Year") DO UPDATE SET "LastValue" = "OrderCounters"."LastValue" + 1
            RETURNING "LastValue" AS "Value"
            """).ToListAsync(ct);
        return $"ORD-{year}-{rows.Single():D5}";
    }

    private async Task<OrderDto> ToDtoAsync(Order o, CancellationToken ct)
    {
        var customerName = await db.Customers.AsNoTracking().Where(c => c.Id == o.CustomerId).Select(c => c.FullName).FirstAsync(ct);
        var garments = o.Garments.OrderBy(g => g.LineNumber).Select(g => new OrderGarmentDto(
            g.Id, g.LineNumber, g.Name, g.Category, g.Quantity, g.UnitPrice, g.LineTotal, g.Fabric, g.Colour,
            g.StyleInstructions, g.AlterationNotes, g.MeasurementSetId, g.MeasurementRevision, ParseSnapshot(g.MeasurementSnapshotJson),
            g.DesignId, g.DesignNameSnapshot)).ToList();

        return new OrderDto(o.Id, o.OrderNumber, o.Status.ToString(), o.CustomerId, customerName, o.LocationId,
            o.PromisedDate, o.Priority.ToString(), o.DeliveryMethod.ToString(), o.DeliveryAddress, o.DeliveryInstructions,
            o.Notes, o.CurrencyCode, o.Subtotal, o.Discount, o.Total, o.Version, o.CreatedAtUtc, o.QuotedAtUtc,
            o.ConfirmedAtUtc, o.CancelledAtUtc, o.CancellationReason, garments,
            o.ReadyAtUtc, o.DeliveredAtUtc, await TaskSupport.ProgressAsync(db, o.Id, ct));
    }

    private static JsonElement? ParseSnapshot(string? json)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
