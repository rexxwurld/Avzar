using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Tailoring.Api.Security;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Api.Controllers;
public sealed record SubscriptionCheckoutRequest(string PlanCode, string BillingPeriod);
public sealed record SubscriptionCheckoutDto(string AuthorizationUrl, string Reference, decimal Amount, string CurrencyCode, DateTime ExpiresAtUtc);

[ApiController, Route("api/billing/subscription")]
public sealed class SubscriptionBillingController(AppDbContext db, ITenantContext tenant, IConfiguration config, IHttpClientFactory clients) : ControllerBase
{
    [HttpPost("checkout"), HasPermission(Permissions.BusinessSettings), EnableRateLimiting("auth")]
    public async Task<SubscriptionCheckoutDto> Checkout(SubscriptionCheckoutRequest request, CancellationToken ct)
    {
        var businessId = tenant.BusinessId ?? throw AppException.Unauthorized("business_required", "Select a business first.");
        var secret = config["Billing:Paystack:SecretKey"];
        if (string.IsNullOrWhiteSpace(secret)) throw new AppException(503, "billing_provider_not_configured", "Subscription payments are not configured on this deployment.");
        var period = request.BillingPeriod.Trim().ToLowerInvariant(); if (period is not ("monthly" or "annual")) throw AppException.BadRequest("invalid_billing_period", "Billing period must be monthly or annual.");
        var plan = await db.Plans.Include(x => x.Entitlements).SingleOrDefaultAsync(x => x.Code == request.PlanCode && x.IsActive, ct) ?? throw AppException.NotFound("plan_not_found", "This plan is unavailable.");
        if (plan.Code == DefaultPlans.FreeCode) throw AppException.BadRequest("free_plan_checkout", "The Free plan does not require checkout.");
        if (plan.CurrencyCode != "NGN") throw AppException.BadRequest("unsupported_billing_currency", "Paystack checkout currently supports NGN plans only.");
        var amount = period == "annual" ? plan.AnnualPrice : plan.MonthlyPrice;
        if (amount is null or <= 0 || amount * 100m > int.MaxValue) throw AppException.BadRequest("invalid_plan_price", "This plan has no valid price for the selected period.");
        var business = await db.Businesses.SingleAsync(ct);
        if (string.IsNullOrWhiteSpace(business.ContactEmail)) throw AppException.BadRequest("billing_email_required", "Add a business contact email before checkout.");
        var reference = "avz_" + Guid.NewGuid().ToString("N");
        var payment = new SubscriptionPayment { BusinessId = businessId, PlanId = plan.Id, BillingPeriod = period, Amount = amount.Value, CurrencyCode = "NGN", ProviderReference = reference };
        db.SubscriptionPayments.Add(payment); await db.SaveChangesAsync(ct);
        using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.paystack.co/transaction/initialize");
        msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        msg.Content = System.Net.Http.Json.JsonContent.Create(new { email = business.ContactEmail, amount = checked((int)(amount.Value * 100m)), currency = "NGN", reference, callback_url = config["Billing:Paystack:CallbackUrl"], metadata = new { businessId, planCode = plan.Code, billingPeriod = period, paymentId = payment.Id } });
        using var response = await clients.CreateClient("paystack").SendAsync(msg, ct); var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) { payment.Status = SubscriptionPaymentStatus.Failed; await db.SaveChangesAsync(ct); throw new AppException(502, "billing_provider_error", "The payment provider could not initialize checkout."); }
        using var json = JsonDocument.Parse(body); var data = json.RootElement.GetProperty("data");
        return new(data.GetProperty("authorization_url").GetString()!, reference, amount.Value, "NGN", DateTime.UtcNow.AddMinutes(30));
    }

    [HttpPost("webhook"), AllowAnonymous, IgnoreAntiforgeryToken, EnableRateLimiting("public")]
    public async Task<IActionResult> PaystackWebhook(CancellationToken ct)
    {
        var secret = config["Billing:Paystack:SecretKey"]; if (string.IsNullOrWhiteSpace(secret)) return StatusCode(503);
        using var reader = new StreamReader(Request.Body, Encoding.UTF8); var raw = await reader.ReadToEndAsync(ct);
        var supplied = Request.Headers["x-paystack-signature"].ToString(); var expected = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied.ToLowerInvariant()), Encoding.ASCII.GetBytes(expected))) return Unauthorized();
        using var json = JsonDocument.Parse(raw); var root = json.RootElement;
        if (!root.TryGetProperty("event", out var eventNode) || eventNode.GetString() != "charge.success") return Ok();
        var data = root.GetProperty("data"); var reference = data.GetProperty("reference").GetString(); if (string.IsNullOrWhiteSpace(reference)) return BadRequest();
        var payment = await db.SubscriptionPayments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProviderReference == reference, ct); if (payment is null) return NotFound(); if (payment.Status == SubscriptionPaymentStatus.Paid) return Ok();
        // Never grant a plan from the webhook payload alone: independently verify the reference with Paystack.
        using var verify = new HttpRequestMessage(HttpMethod.Get, "https://api.paystack.co/transaction/verify/" + Uri.EscapeDataString(reference));
        verify.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        using var providerResponse = await clients.CreateClient("paystack").SendAsync(verify, ct); if (!providerResponse.IsSuccessStatusCode) return StatusCode(502);
        using var verifiedJson = JsonDocument.Parse(await providerResponse.Content.ReadAsStringAsync(ct)); var verified = verifiedJson.RootElement.GetProperty("data");
        var expectedKobo = decimal.ToInt64(decimal.Round(payment.Amount * 100m, 0, MidpointRounding.AwayFromZero));
        if (verified.GetProperty("status").GetString() != "success" || verified.GetProperty("reference").GetString() != reference || verified.GetProperty("currency").GetString() != payment.CurrencyCode || verified.GetProperty("amount").GetInt64() != expectedKobo) { payment.Status = SubscriptionPaymentStatus.Failed; await db.SaveChangesAsync(ct); return BadRequest(); }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Lock the payment row and re-read its state so concurrent webhook retries cannot extend a subscription twice.
        await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"SubscriptionPayments\" WHERE \"Id\" = {payment.Id} FOR UPDATE").SingleAsync(ct);
        await db.Entry(payment).ReloadAsync(ct);
        if (payment.Status == SubscriptionPaymentStatus.Paid) { await tx.CommitAsync(ct); return Ok(); }
        if (payment.Status != SubscriptionPaymentStatus.Pending) { await tx.CommitAsync(ct); return Ok(); }
        var sub = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(x => x.BusinessId == payment.BusinessId, ct);
        var now = DateTime.UtcNow;
        var periodAnchor = sub.Status == SubscriptionStatus.Active && sub.CurrentPeriodEndUtc is { } currentEnd && currentEnd > now ? currentEnd : now;
        payment.Status = SubscriptionPaymentStatus.Paid; payment.PaidAtUtc = now; sub.PlanId = payment.PlanId; sub.Status = SubscriptionStatus.Active; sub.CurrentPeriodStartUtc = now; sub.CurrentPeriodEndUtc = periodEnd(payment.BillingPeriod, periodAnchor); sub.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok();
    }
    private static DateTime periodEnd(string period, DateTime now) => period == "annual" ? now.AddYears(1) : now.AddMonths(1);
}
