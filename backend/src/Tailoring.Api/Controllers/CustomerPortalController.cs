using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tailoring.Api.Security;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Domain.Access;
using Tailoring.Domain.CustomerPortal;
using Tailoring.Domain.Finance;
using Tailoring.Domain.Orders;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Api.Controllers;

public sealed record CustomerPortalLinkDto(DateTime ExpiresAtUtc, string MaskedEmail);
public sealed record CustomerPortalAccessRequest(string Token);
public sealed record CustomerPortalOrderDto(Guid Id, string OrderNumber, string Status, DateOnly PromisedDate, decimal Total, string CurrencyCode, IReadOnlyList<CustomerPortalTaskDto> Progress);
public sealed record CustomerPortalTaskDto(string Stage, string Status, DateTime? CompletedAtUtc);
public sealed record CustomerPortalPaymentDto(decimal Amount, string CurrencyCode, string Method, DateOnly ReceivedOn, string? Reference);
public sealed record CustomerPortalInvoiceDto(Guid Id, string? Number, string Status, DateOnly? IssueDate, DateOnly? DueDate, decimal Total, string CurrencyCode, decimal VerifiedPaid, decimal Balance, IReadOnlyList<CustomerPortalPaymentDto> Payments);
public sealed record CustomerPortalDto(string CustomerName, string BusinessName, string CurrencyCode, IReadOnlyList<CustomerPortalOrderDto> Orders, IReadOnlyList<CustomerPortalInvoiceDto> Invoices);

[ApiController, Route("api/customer-portal")]
public sealed class CustomerPortalController(AppDbContext db, ITenantContext tenant, IEmailSender email, IConfiguration config) : ControllerBase
{
    [HttpPost("customers/{customerId:guid}/link"), HasPermission(Permissions.CustomersManage), EnableRateLimiting("auth")]
    public async Task<CustomerPortalLinkDto> SendLink(Guid customerId, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == customerId, ct) ?? throw AppException.NotFound("customer_not_found", "Customer was not found.");
        if (string.IsNullOrWhiteSpace(customer.Email)) throw AppException.BadRequest("customer_email_required", "Add a valid customer email before sending a portal link.");
        var business = await db.Businesses.SingleAsync(ct);
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = DateTime.UtcNow.AddDays(7);
        var grant = new CustomerPortalGrant { BusinessId = customer.BusinessId, CustomerId = customer.Id, TokenHash = Hash(raw), ExpiresAtUtc = expires };
        db.CustomerPortalGrants.Add(grant);
        await db.SaveChangesAsync(ct);
        var baseUrl = (config["App:WebBaseUrl"] ?? "https://avzar.app").TrimEnd('/');
        var link = $"{baseUrl}/customer-portal#{Uri.EscapeDataString(raw)}";
        await email.SendAsync(customer.Email, $"Your {business.Name} customer portal", $"Hello {customer.FullName},\n\nView your order progress, invoices and payments securely here:\n{link}\n\nThis link expires in 7 days. If you did not request it, ignore this message.", ct);
        return new(expires, Mask(customer.Email));
    }

    [HttpDelete("customers/{customerId:guid}/links"), HasPermission(Permissions.CustomersManage), EnableRateLimiting("auth")]
    public async Task<IActionResult> RevokeLinks(Guid customerId, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == customerId, ct) ?? throw AppException.NotFound("customer_not_found", "Customer was not found.");
        var grants = await db.CustomerPortalGrants.Where(x => x.CustomerId == customer.Id && x.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var g in grants) g.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("access"), AllowAnonymous, IgnoreAntiforgeryToken, EnableRateLimiting("public")]
    public async Task<CustomerPortalDto> Access([FromBody] CustomerPortalAccessRequest request, CancellationToken ct)
    {
        var token = request?.Token ?? "";
        if (token.Length is < 32 or > 128) throw AppException.Unauthorized("portal_link_invalid", "This customer portal link is invalid or expired.");
        var hash = Hash(token);
        var grant = await db.CustomerPortalGrants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (grant is null || grant.RevokedAtUtc is not null || grant.ExpiresAtUtc <= DateTime.UtcNow) throw AppException.Unauthorized("portal_link_invalid", "This customer portal link is invalid or expired.");
        var customer = await db.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == grant.CustomerId && x.BusinessId == grant.BusinessId, ct)
            ?? throw AppException.Unauthorized("portal_link_invalid", "This customer portal link is invalid or expired.");
        var business = await db.Businesses.IgnoreQueryFilters().SingleAsync(x => x.Id == grant.BusinessId, ct);
        var orders = await db.Orders.IgnoreQueryFilters().Where(x => x.BusinessId == grant.BusinessId && x.CustomerId == grant.CustomerId && x.Status != OrderStatus.Cancelled)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var tasks = await db.ProductionTasks.IgnoreQueryFilters().Where(x => x.BusinessId == grant.BusinessId && orderIds.Contains(x.OrderId) && x.Status != Tailoring.Domain.Production.ProductionTaskStatus.Cancelled)
            .OrderBy(x => x.CreatedAtUtc).Select(x => new { x.OrderId, x.StageName, x.Status, x.CompletedAtUtc }).ToListAsync(ct);
        var invoices = await db.Invoices.IgnoreQueryFilters().Where(x => x.BusinessId == grant.BusinessId && x.CustomerId == grant.CustomerId && x.Status != InvoiceStatus.Void).OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct);
        var invoiceIds = invoices.Select(x => x.Id).ToArray();
        var paymentRows = await db.Payments.IgnoreQueryFilters().Where(x => x.BusinessId == grant.BusinessId && invoiceIds.Contains(x.InvoiceId) && x.Status == PaymentStatus.Verified)
            .OrderBy(x => x.ReceivedOn).Select(x => new { x.InvoiceId, x.Amount, x.CurrencyCode, Method = x.Method.ToString(), x.ReceivedOn, x.Reference }).ToListAsync(ct);
        var paid = paymentRows.GroupBy(x => x.InvoiceId).Select(g => new { InvoiceId = g.Key, Amount = g.Sum(x => x.Amount) }).ToList();
        grant.LastUsedAtUtc = DateTime.UtcNow; grant.UseCount++;
        await db.SaveChangesAsync(ct);
        return new(customer.FullName, business.Name, business.CurrencyCode,
            orders.Select(o => new CustomerPortalOrderDto(o.Id, o.OrderNumber, o.Status.ToString(), o.PromisedDate, o.Total, o.CurrencyCode,
                tasks.Where(t => t.OrderId == o.Id).Select(t => new CustomerPortalTaskDto(t.StageName, t.Status.ToString(), t.CompletedAtUtc)).ToList())).ToList(),
            invoices.Select(i => { var amount = paid.FirstOrDefault(x => x.InvoiceId == i.Id)?.Amount ?? 0m; var receipts = paymentRows.Where(x => x.InvoiceId == i.Id).Select(x => new CustomerPortalPaymentDto(x.Amount, x.CurrencyCode, x.Method, x.ReceivedOn, x.Reference)).ToList(); return new CustomerPortalInvoiceDto(i.Id, i.Number, i.Status.ToString(), i.IssueDate, i.DueDate, i.Total, i.CurrencyCode, amount, Math.Max(0, i.Total - amount), receipts); }).ToList());
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string Mask(string email) { var at = email.IndexOf('@'); return at <= 0 ? "***" : at == 1 ? "*" + email[at..] : email[..1] + new string('*', Math.Min(8, at - 1)) + email[at..]; }
}
