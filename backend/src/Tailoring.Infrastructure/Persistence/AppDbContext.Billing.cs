using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Billing;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();
    public DbSet<TenantSubscription> Subscriptions => Set<TenantSubscription>();
    public DbSet<TenantEntitlementOverride> EntitlementOverrides => Set<TenantEntitlementOverride>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();

    private static void ConfigureBilling(ModelBuilder b)
    {
        b.Entity<Plan>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.MonthlyPrice).HasPrecision(18, 2);
            e.Property(x => x.AnnualPrice).HasPrecision(18, 2);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<PlanEntitlement>(e =>
        {
            e.HasKey(x => new { x.PlanId, x.Key });
            e.Property(x => x.Key).HasMaxLength(80);
            e.Property(x => x.Value).HasMaxLength(60).IsRequired();
            e.HasOne<Plan>().WithMany(p => p.Entitlements).HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TenantSubscription>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.BusinessId).IsUnique(); // exactly one subscription per business
            e.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TenantEntitlementOverride>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(80).IsRequired();
            e.Property(x => x.Value).HasMaxLength(60).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.BusinessId, x.Key }).IsUnique();
        });

        b.Entity<UsageCounter>(e =>
        {
            e.HasKey(x => new { x.BusinessId, x.Key, x.PeriodStartUtc });
            e.Property(x => x.Key).HasMaxLength(80);
            e.ToTable(t => t.HasCheckConstraint("CK_UsageCounters_NonNegative", "\"Count\" >= 0"));
        });
    }
}
