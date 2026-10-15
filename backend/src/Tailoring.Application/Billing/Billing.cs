using Tailoring.Domain.Billing;

namespace Tailoring.Application.Billing;

public sealed record EntitlementDefinitionDto(string Key, string Kind, string Description, IReadOnlyList<string>? Tiers, bool Periodic);
public sealed record PlanDto(string Code, string Name, string? Description, string CurrencyCode, decimal MonthlyPrice,
    decimal? AnnualPrice, IReadOnlyDictionary<string, string> Entitlements);
public sealed record PlanCatalogDto(IReadOnlyList<EntitlementDefinitionDto> Entitlements, IReadOnlyList<PlanDto> Plans);

/// <param name="Used">Current usage where the platform can measure it today; null otherwise.</param>
public sealed record EntitlementItemDto(string Key, string Kind, string Description, string Value, long? Used);
public sealed record TenantEntitlementsDto(string PlanCode, string PlanName, string Status, IReadOnlyList<EntitlementItemDto> Items);

/// <summary>
/// The one place plan rules are checked. Callers never compare plan names. Every method throws a 403
/// <c>AppException</c> with a stable code (<c>feature_not_in_plan</c> / <c>plan_limit_reached</c>) so clients can offer an upgrade.
/// </summary>
public interface IEntitlementService
{
    Task<EffectiveEntitlements> GetAsync(Guid businessId, CancellationToken ct = default);
    Task RequireFlagAsync(Guid businessId, string key, CancellationToken ct = default);
    Task RequireTierAsync(Guid businessId, string key, string minimumTier, CancellationToken ct = default);

    /// <summary>
    /// For stock limits (staff, active customers, designs). MUST run inside a database transaction: it takes a
    /// per-tenant, per-key advisory lock held until commit, so two concurrent creators cannot both slip under the limit.
    /// </summary>
    Task EnsureBelowLimitAsync(Guid businessId, string key, Func<CancellationToken, Task<long>> currentCount, CancellationToken ct = default);

    /// <summary>
    /// For size-based limits (storage). Like <see cref="EnsureBelowLimitAsync"/> it must run inside a transaction.
    /// The limit is expressed in units (e.g. GB); <paramref name="unitSize"/> converts it to the unit of <paramref name="currentUsed"/> and <paramref name="adding"/> (bytes).
    /// </summary>
    Task EnsureWithinLimitAsync(Guid businessId, string key, Func<CancellationToken, Task<long>> currentUsed, long adding, long unitSize, CancellationToken ct = default);

    /// <summary>For periodic limits (orders/month, appointments/month). One atomic, limit-checked upsert; join the caller's transaction so a failed create does not burn quota.</summary>
    Task ConsumeAsync(Guid businessId, string key, long amount = 1, CancellationToken ct = default);

    Task<TenantEntitlementsDto> DescribeAsync(Guid businessId, CancellationToken ct = default);
}

public interface IPlanCatalogService
{
    Task<PlanCatalogDto> GetPublicAsync(CancellationToken ct = default);
}
