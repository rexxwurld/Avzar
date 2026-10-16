using Tailoring.Domain.Common;
namespace Tailoring.Domain.Billing;
public enum SubscriptionPaymentStatus { Pending, Paid, Failed, Expired }
/// <summary>One provider checkout attempt. Provider reference is unique and webhook processing is idempotent.</summary>
public sealed class SubscriptionPayment : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid PlanId { get; set; }
    public string BillingPeriod { get; set; } = "monthly";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "NGN";
    public string Provider { get; set; } = "paystack";
    public string ProviderReference { get; set; } = "";
    public SubscriptionPaymentStatus Status { get; set; } = SubscriptionPaymentStatus.Pending;
    public DateTime? PaidAtUtc { get; set; }
}
