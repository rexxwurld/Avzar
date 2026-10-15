namespace Tailoring.Domain.Billing;

public readonly record struct LimitValue(bool IsUnlimited, long Max);

/// <summary>
/// Resolved entitlements for one tenant: tenant override (if unexpired) > plan value > restrictive default.
/// Pure and side-effect free so every rule can be unit-tested without a database.
/// </summary>
public sealed class EffectiveEntitlements
{
    private readonly Dictionary<string, string> _values = new();

    public string PlanCode { get; }
    public string PlanName { get; }
    public SubscriptionStatus Status { get; }

    public EffectiveEntitlements(
        string planCode, string planName, SubscriptionStatus status,
        IEnumerable<KeyValuePair<string, string>> planValues,
        IEnumerable<TenantEntitlementOverride> overrides, DateTime utcNow)
    {
        (PlanCode, PlanName, Status) = (planCode, planName, status);
        foreach (var (k, v) in planValues) _values[k] = v;
        foreach (var o in overrides.Where(o => o.ExpiresAtUtc is null || o.ExpiresAtUtc > utcNow))
            _values[o.Key] = o.Value;
    }

    public string Raw(string key)
    {
        var def = EntitlementCatalog.Require(key);
        return _values.TryGetValue(key, out var v) && EntitlementCatalog.Validate(key, v) is null
            ? v.Trim()
            : EntitlementCatalog.DefaultFor(def);
    }

    public bool Flag(string key)
    {
        EntitlementCatalog.Require(key, EntitlementKind.Flag);
        return Raw(key) == "true";
    }

    public LimitValue Limit(string key)
    {
        EntitlementCatalog.Require(key, EntitlementKind.Limit);
        var raw = Raw(key);
        return raw == EntitlementCatalog.Unlimited ? new LimitValue(true, long.MaxValue) : new LimitValue(false, long.Parse(raw));
    }

    public string Tier(string key)
    {
        EntitlementCatalog.Require(key, EntitlementKind.Tier);
        return Raw(key);
    }

    public bool TierAtLeast(string key, string minimum)
    {
        var order = EntitlementCatalog.Require(key, EntitlementKind.Tier).TierOrder!;
        var need = order.ToList().IndexOf(minimum);
        if (need < 0) throw new ArgumentException($"'{minimum}' is not a tier of '{key}'.", nameof(minimum));
        return order.ToList().IndexOf(Tier(key)) >= need;
    }
}
