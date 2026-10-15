using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Customers;
using Tailoring.Application.Orders;
using Tailoring.Application.Storefront;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Storefront;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>
/// Public requests land here whatever the site layout looks like. A request is NEVER an order: it only becomes a (draft)
/// order when a staff member converts it.
/// </summary>
public sealed class InquiryService(AppDbContext db, ITenantContext tenant, IAuditLogger audit, ICaptchaVerifier captcha,
    ICustomerService customers, IOrderService orders) : IInquiryService
{
    public const int MaxPerIpPerHour = 10;
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    // ------------------------------------------------------------------ public

    public async Task<InquiryAcceptedDto> SubmitAsync(string slug, SubmitInquiryRequest req, string? clientIp, CancellationToken ct = default)
    {
        var accepted = new InquiryAcceptedDto(true, "Thank you. We have received your message and will get back to you.");
        if (!string.IsNullOrWhiteSpace(req.Website)) return accepted; // honeypot: a bot filled the hidden field. Look successful, store nothing.

        var kind = Parse.Value<InquiryKind>(req.Kind, "kind");
        var s = await db.Storefronts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.Status == StorefrontStatus.Published, ct)
            ?? throw AppException.NotFound("site_not_found", "This site does not exist.");

        var workflow = kind switch
        {
            InquiryKind.Inquiry or InquiryKind.Quotation => "inquiry",
            InquiryKind.OrderRequest => "customOrder",
            InquiryKind.Appointment => "booking",
            _ => "contact",
        };
        if (!s.EnabledWorkflows().Contains(workflow))
            throw AppException.Conflict("workflow_disabled", "This business is not taking that kind of request through the site.");

        if (captcha.IsConfigured && !await captcha.VerifyAsync(req.CaptchaToken, clientIp, ct))
            throw AppException.BadRequest("captcha_failed", "Please complete the check and try again.");

        var email = Parse.Clean(req.Email); var phone = Parse.Clean(req.Phone);
        if (email is null && phone is null) throw AppException.BadRequest("contact_required", "Please give an email address or a phone number so we can reply.");
        foreach (var text in new[] { req.Name, req.Message, req.GarmentType, req.Budget, phone }.OfType<string>())
            if (text.Contains('<') || text.Contains('>')) throw AppException.BadRequest("invalid_text", "The characters < and > are not allowed.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (kind == InquiryKind.Appointment && req.PreferredDate is null) throw AppException.BadRequest("date_required", "Please choose the day you would like to come in.");
        if (req.PreferredDate is { } pd && (pd < today.AddDays(-1) || pd > today.AddYears(2))) throw AppException.BadRequest("invalid_date", "That date is not valid.");
        if (req.EventDate is { } ed && (ed < today.AddDays(-1) || ed > today.AddYears(5))) throw AppException.BadRequest("invalid_date", "That date is not valid.");

        string? designName = null;
        if (req.DesignId is { } designId)
        {
            designName = await db.Designs.IgnoreQueryFilters().AsNoTracking()
                .Where(d => d.Id == designId && d.BusinessId == s.BusinessId && d.Status == DesignStatus.Active && d.Visibility == DesignVisibility.Public)
                .Select(d => d.Name).FirstOrDefaultAsync(ct)
                ?? throw AppException.BadRequest("unknown_design", "That design is not available.");
        }

        var ipHash = HashIp(clientIp, s.BusinessId);
        if (ipHash is not null)
        {
            var since = DateTime.UtcNow.AddHours(-1);
            if (await db.Inquiries.IgnoreQueryFilters().CountAsync(i => i.StorefrontId == s.Id && i.IpHash == ipHash && i.CreatedAtUtc > since, ct) >= MaxPerIpPerHour)
                throw new AppException(429, "too_many_requests", "You have sent several messages recently. Please wait a while before sending another.");
        }

        db.Inquiries.Add(new Inquiry
        {
            BusinessId = s.BusinessId, StorefrontId = s.Id, Kind = kind, Name = req.Name.Trim(), Email = email, Phone = phone, Message = req.Message.Trim(),
            DesignId = req.DesignId, DesignNameSnapshot = designName, GarmentType = Parse.Clean(req.GarmentType), Budget = Parse.Clean(req.Budget),
            PreferredDate = req.PreferredDate, EventDate = req.EventDate, IpHash = ipHash,
        });
        await db.SaveChangesAsync(ct);
        return accepted;
    }

    // ------------------------------------------------------------------ staff

    public async Task<PagedResult<InquiryDto>> ListAsync(InquiryListQuery query, CancellationToken ct = default)
    {
        var q = db.Inquiries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Status)) { var st = Parse.Value<InquiryStatus>(query.Status, "status"); q = q.Where(i => i.Status == st); }
        if (!string.IsNullOrWhiteSpace(query.Kind)) { var k = Parse.Value<InquiryKind>(query.Kind, "kind"); q = q.Where(i => i.Kind == k); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{Parse.Like(query.Search.Trim())}%";
            q = q.Where(i => EF.Functions.ILike(i.Name, pattern) || (i.Email != null && EF.Functions.ILike(i.Email, pattern)) || EF.Functions.ILike(i.Message, pattern));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(i => i.CreatedAtUtc).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<InquiryDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<InquiryDto> GetAsync(Guid id, CancellationToken ct = default) => ToDto(await LoadAsync(id, track: false, ct));

    public async Task<InquiryDto> UpdateAsync(Guid id, UpdateInquiryRequest req, CancellationToken ct = default)
    {
        var status = Parse.Value<InquiryStatus>(req.Status, "status");
        if (status == InquiryStatus.Converted) throw AppException.BadRequest("use_convert", "Convert the request to an order to mark it converted.");
        var i = await LoadAsync(id, track: true, ct);
        if (i.Status == InquiryStatus.Converted) throw AppException.Conflict("already_converted", "This request already became an order.");
        if (req.InternalNote is { } n && (n.Contains('<') || n.Contains('>'))) throw AppException.BadRequest("invalid_text", "The characters < and > are not allowed.");

        i.Status = status; i.InternalNote = Parse.Clean(req.InternalNote);
        i.HandledByUserId = tenant.UserId; i.HandledAtUtc = DateTime.UtcNow; i.UpdatedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, "inquiry.updated", nameof(Inquiry), i.Id, new { status = status.ToString() });
        await db.SaveChangesAsync(ct);
        return ToDto(i);
    }

    public async Task<InquiryDto> ConvertToOrderAsync(Guid id, ConvertInquiryRequest req, CancellationToken ct = default)
    {
        var i = await LoadAsync(id, track: true, ct);
        if (i.Status is InquiryStatus.Converted or InquiryStatus.Rejected)
            throw AppException.Conflict("not_convertible", $"A {i.Status} request cannot be converted.");

        var email = Normalize.Email(i.Email); var phone = Normalize.Phone(i.Phone);
        var customerId = await db.Customers.AsNoTracking()
            .Where(c => (email != null && c.NormalizedEmail == email) || (phone != null && c.NormalizedPhone == phone))
            .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        customerId ??= (await customers.CreateAsync(new UpsertCustomerRequest { FullName = i.Name, Email = i.Email, Phone = i.Phone, AllowDuplicate = true }, ct)).Id;

        var designStillAvailable = i.DesignId is { } d && await db.Designs.AnyAsync(x => x.Id == d && x.Status == DesignStatus.Active, ct);
        var note = $"Website request: {i.Message}";
        var (order, _) = await orders.CreateAsync(new CreateOrderRequest
        {
            CustomerId = customerId.Value, LocationId = req.LocationId, PromisedDate = req.PromisedDate,
            Garments = [new OrderGarmentInput
            {
                Name = Parse.Clean(req.GarmentName) ?? i.DesignNameSnapshot ?? i.GarmentType ?? "Custom order", UnitPrice = req.UnitPrice,
                DesignId = designStillAvailable ? i.DesignId : null, StyleInstructions = note.Length > 1000 ? note[..1000] : note,
            }],
        }, $"inquiry-{i.Id:N}", ct); // the key means a double click cannot create two orders

        i.Status = InquiryStatus.Converted; i.ConvertedOrderId = order.Id;
        i.HandledByUserId = tenant.UserId; i.HandledAtUtc = DateTime.UtcNow; i.UpdatedAtUtc = DateTime.UtcNow;
        audit.Record(BusinessId, tenant.UserId, "inquiry.converted", nameof(Inquiry), i.Id, new { order = order.OrderNumber });
        await db.SaveChangesAsync(ct);
        return ToDto(i);
    }

    public async Task<InquirySummaryDto> SummaryAsync(CancellationToken ct = default)
    {
        var counts = await db.Inquiries.AsNoTracking().GroupBy(i => i.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        int N(InquiryStatus s) => counts.FirstOrDefault(c => c.Key == s)?.N ?? 0;
        return new InquirySummaryDto(N(InquiryStatus.New), N(InquiryStatus.Reviewed), N(InquiryStatus.Converted), N(InquiryStatus.Rejected));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Inquiry> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = track ? db.Inquiries.AsQueryable() : db.Inquiries.AsNoTracking();
        return await q.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw AppException.NotFound("inquiry_not_found", "Request not found.");
    }

    private static string? HashIp(string? ip, Guid businessId)
        => string.IsNullOrWhiteSpace(ip) ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{businessId:N}|{ip.Trim()}"))).ToLowerInvariant();

    private static InquiryDto ToDto(Inquiry i) => new(i.Id, i.Kind.ToString(), i.Status.ToString(), i.Name, i.Email, i.Phone, i.Message, i.DesignId,
        i.DesignNameSnapshot, i.GarmentType, i.Budget, i.PreferredDate, i.EventDate, i.ConvertedOrderId, i.InternalNote, i.CreatedAtUtc, i.HandledAtUtc);
}
