using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Billing;
using Tailoring.Application.Common;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Customers;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>
/// Reads cross-tenant rows with IgnoreQueryFilters ON PURPOSE (the caller supplies the business id, and background
/// jobs have no tenant in scope). Every call site passes the tenant's own id; do not expose these methods with a client-supplied id.
/// </summary>
public sealed class EntitlementService(AppDbContext db) : IEntitlementService
{
    private readonly Dictionary<Guid, EffectiveEntitlements> _cache = new();

    public async Task<EffectiveEntitlements> GetAsync(Guid businessId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(businessId, out var hit)) return hit;

        var sub = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.BusinessId == businessId, ct);

        // Every business has a subscription from registration. No subscription, or an expired one, means the Free plan.
        var plan = sub is { Status: not SubscriptionStatus.Expired }
            ? await db.Plans.AsNoTracking().Include(p => p.Entitlements).FirstAsync(p => p.Id == sub.PlanId, ct)
            : await db.Plans.AsNoTracking().Include(p => p.Entitlements).FirstOrDefaultAsync(p => p.Code == DefaultPlans.FreeCode, ct)
              ?? throw new AppException(503, "plans_not_seeded", "Subscription plans are not configured on this server.");

        var overrides = await db.EntitlementOverrides.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.BusinessId == businessId).ToListAsync(ct);

        var resolved = new EffectiveEntitlements(plan.Code, plan.Name, sub?.Status ?? SubscriptionStatus.Active,
            plan.Entitlements.Select(e => KeyValuePair.Create(e.Key, e.Value)), overrides, DateTime.UtcNow);
        return _cache[businessId] = resolved;
    }

    public async Task RequireFlagAsync(Guid businessId, string key, CancellationToken ct = default)
    {
        var def = EntitlementCatalog.Require(key, EntitlementKind.Flag);
        if (!(await GetAsync(businessId, ct)).Flag(key)) throw NotInPlan(def, null);
    }

    public async Task RequireTierAsync(Guid businessId, string key, string minimumTier, CancellationToken ct = default)
    {
        var def = EntitlementCatalog.Require(key, EntitlementKind.Tier);
        if (!(await GetAsync(businessId, ct)).TierAtLeast(key, minimumTier)) throw NotInPlan(def, minimumTier);
    }

    public async Task EnsureBelowLimitAsync(Guid businessId, string key,
        Func<CancellationToken, Task<long>> currentCount, CancellationToken ct = default)
    {
        var def = EntitlementCatalog.Require(key, EntitlementKind.Limit);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("EnsureBelowLimitAsync must run inside a transaction (the advisory lock lives until commit).");

        var lockKey = $"{businessId:N}:{key}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);

        var limit = (await GetAsync(businessId, ct)).Limit(key);
        if (limit.IsUnlimited) return;
        var used = await currentCount(ct);
        if (used >= limit.Max) throw LimitReached(def, limit.Max, used);
    }

    public async Task EnsureWithinLimitAsync(Guid businessId, string key, Func<CancellationToken, Task<long>> currentUsed,
        long adding, long unitSize, CancellationToken ct = default)
    {
        var def = EntitlementCatalog.Require(key, EntitlementKind.Limit);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("EnsureWithinLimitAsync must run inside a transaction (the advisory lock lives until commit).");
        if (unitSize <= 0) throw new ArgumentOutOfRangeException(nameof(unitSize));

        var lockKey = $"{businessId:N}:{key}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);

        var limit = (await GetAsync(businessId, ct)).Limit(key);
        if (limit.IsUnlimited || limit.Max > long.MaxValue / unitSize) return;
        var used = await currentUsed(ct);
        if (used + adding > limit.Max * unitSize)
            throw LimitReached(def, limit.Max, (used + unitSize - 1) / unitSize);
    }

    public async Task ConsumeAsync(Guid businessId, string key, long amount = 1, CancellationToken ct = default)
    {
        var def = EntitlementCatalog.Require(key, EntitlementKind.Limit);
        if (!def.Periodic) throw new InvalidOperationException($"'{key}' is not a periodic limit; use EnsureBelowLimitAsync.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

        var limit = (await GetAsync(businessId, ct)).Limit(key);
        var max = limit.IsUnlimited ? long.MaxValue : limit.Max;
        var period = UsagePeriod.StartOf(DateTime.UtcNow);

        // Single statement: the insert/update only happens when the new total stays within the limit, so concurrent
        // callers serialise on the row lock and can never overshoot. No row returned = limit would be exceeded.
        var rows = await db.Database.SqlQuery<long>($"""
            INSERT INTO "UsageCounters" ("BusinessId", "Key", "PeriodStartUtc", "Count")
            SELECT {businessId}, {key}, {period}, {amount} WHERE {amount} <= {max}
            ON CONFLICT ("BusinessId", "Key", "PeriodStartUtc")
            DO UPDATE SET "Count" = "UsageCounters"."Count" + {amount}
            WHERE "UsageCounters"."Count" + {amount} <= {max}
            RETURNING "Count" AS "Value"
            """).ToListAsync(ct);

        if (rows.Count == 0) throw LimitReached(def, limit.Max, await UsedThisPeriodAsync(businessId, key, period, ct));
    }

    public async Task<TenantEntitlementsDto> DescribeAsync(Guid businessId, CancellationToken ct = default)
    {
        var e = await GetAsync(businessId, ct);
        var period = UsagePeriod.StartOf(DateTime.UtcNow);
        var items = new List<EntitlementItemDto>();
        foreach (var d in EntitlementCatalog.All)
        {
            long? used = d.Key switch
            {
                EntitlementKeys.UsersMax => await db.Memberships.IgnoreQueryFilters()
                    .LongCountAsync(m => m.BusinessId == businessId && m.Status == MembershipStatus.Active, ct)
                    + await db.Invitations.IgnoreQueryFilters().LongCountAsync(i => i.BusinessId == businessId
                        && i.AcceptedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > DateTime.UtcNow, ct),
                EntitlementKeys.CustomersActiveMax => await db.Customers.IgnoreQueryFilters()
                    .LongCountAsync(c => c.BusinessId == businessId && c.Status == CustomerStatus.Active, ct),
                _ when d.Periodic => await UsedThisPeriodAsync(businessId, d.Key, period, ct),
                _ => null,
            };
            items.Add(new EntitlementItemDto(d.Key, d.Kind.ToString(), d.Description, e.Raw(d.Key), used));
        }
        return new TenantEntitlementsDto(e.PlanCode, e.PlanName, e.Status.ToString(), items);
    }

    private Task<long> UsedThisPeriodAsync(Guid businessId, string key, DateTime period, CancellationToken ct)
        => db.UsageCounters.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.BusinessId == businessId && c.Key == key && c.PeriodStartUtc == period)
            .Select(c => c.Count).FirstOrDefaultAsync(ct);

    private static AppException NotInPlan(EntitlementDefinition d, string? requiredTier) => new(403, "feature_not_in_plan",
        $"Your current plan does not include {d.Noun}. Upgrade to unlock it.",
        new Dictionary<string, object?> { ["entitlement"] = d.Key, ["requiredTier"] = requiredTier });

    private static AppException LimitReached(EntitlementDefinition d, long limit, long used) => new(403, "plan_limit_reached",
        $"Your plan allows {limit} {d.Noun}, and you have used {used}. Upgrade your plan to add more. Nothing has been deleted.",
        new Dictionary<string, object?> { ["entitlement"] = d.Key, ["limit"] = limit, ["used"] = used });
}

public sealed class PlanCatalogService(AppDbContext db) : IPlanCatalogService
{
    public async Task<PlanCatalogDto> GetPublicAsync(CancellationToken ct = default)
    {
        var plans = await db.Plans.AsNoTracking().Include(p => p.Entitlements)
            .Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(ct);
        var defs = EntitlementCatalog.All.Select(d =>
            new EntitlementDefinitionDto(d.Key, d.Kind.ToString(), d.Description, d.TierOrder, d.Periodic)).ToList();
        var dtos = plans.Select(p => new PlanDto(p.Code, p.Name, p.Description, p.CurrencyCode, p.MonthlyPrice, p.AnnualPrice,
            p.Entitlements.ToDictionary(e => e.Key, e => e.Value))).ToList();
        return new PlanCatalogDto(defs, dtos);
    }
}

/// <summary>Inserts the initial plans once, then only ADDS keys that newer releases introduced. Never overwrites admin edits.</summary>
public static class PlanSeeder
{
    public static async Task SyncAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existing = await db.Plans.Include(p => p.Entitlements).ToListAsync(ct);
        foreach (var seed in DefaultPlans.All)
        {
            var plan = existing.FirstOrDefault(p => p.Code == seed.Code);
            if (plan is null)
            {
                plan = new Plan { Code = seed.Code, Name = seed.Name, Description = seed.Description, SortOrder = seed.SortOrder, MonthlyPrice = seed.MonthlyPrice };
                db.Plans.Add(plan);
            }
            foreach (var (key, value) in seed.Values.Where(kv => plan.Entitlements.All(e => e.Key != kv.Key)))
                plan.Entitlements.Add(new PlanEntitlement { PlanId = plan.Id, Key = key, Value = value });
        }
        await db.SaveChangesAsync(ct);
    }
}
