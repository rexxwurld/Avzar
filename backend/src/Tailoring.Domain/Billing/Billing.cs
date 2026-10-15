using Tailoring.Domain.Common;

namespace Tailoring.Domain.Billing;

public enum SubscriptionStatus { Active = 0, Trialing = 1, PastDue = 2, Canceled = 3, Expired = 4 }

/// <summary>Global (not tenant-scoped) plan definition. Prices and limits are data, edited by platform admins.</summary>
public class Plan : Entity
{
    /// <summary>Stable machine code ("free", "starter", ...). Display names can change; codes do not.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string CurrencyCode { get; set; } = "NGN";
    public decimal MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }
    public List<PlanEntitlement> Entitlements { get; set; } = new();
}

/// <summary>One catalogue key with a raw string value. Parsed and validated through <see cref="EntitlementCatalog"/>.</summary>
public class PlanEntitlement
{
    public Guid PlanId { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class TenantSubscription : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public DateTime CurrentPeriodStartUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CurrentPeriodEndUtc { get; set; }
}

/// <summary>Per-tenant exception to the plan (enterprise limits, goodwill upgrades). Wins over the plan value.</summary>
public class TenantEntitlementOverride : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime? ExpiresAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>Usage for a periodic limit. Incremented only through one atomic, limit-checked SQL statement.</summary>
public class UsageCounter : ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string Key { get; set; } = "";
    public DateTime PeriodStartUtc { get; set; }
    public long Count { get; set; }
}

/// <summary>Quota periods. Calendar months in UTC for now; Phase 9 swaps in billing-period anchoring behind this seam.</summary>
public static class UsagePeriod
{
    public static DateTime StartOf(DateTime utcNow) => new(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
}
