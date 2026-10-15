using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Pricing;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<PricingProfile> PricingProfiles => Set<PricingProfile>();

    private static void ConfigurePricing(ModelBuilder b)
    {
        b.Entity<PricingProfile>(e =>
        {
            e.Property(x => x.DefaultHourlyRate).HasPrecision(18, 2);
            e.Property(x => x.OverheadPercent).HasPrecision(7, 2);
            e.Property(x => x.TargetPercent).HasPrecision(7, 2);
            e.Property(x => x.TaxPercent).HasPrecision(7, 2);
            e.Property(x => x.RoundingStep).HasPrecision(18, 2);
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.BusinessId).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PricingProfiles_Ranges",
                    "\"DefaultHourlyRate\" >= 0 AND \"OverheadPercent\" BETWEEN 0 AND 200 AND \"TaxPercent\" BETWEEN 0 AND 100 AND \"RoundingStep\" >= 0");
                t.HasCheckConstraint("CK_PricingProfiles_Target",
                    "(\"Method\" = 'Margin' AND \"TargetPercent\" BETWEEN 0 AND 95) OR (\"Method\" = 'Markup' AND \"TargetPercent\" BETWEEN 0 AND 1000)");
            });
        });

        // Order lines: design link + frozen pricing snapshot (added in step 3)
        b.Entity<OrderGarment>(e =>
        {
            e.Property(x => x.DesignNameSnapshot).HasMaxLength(120);
            e.Property(x => x.DesignPriceSnapshot).HasPrecision(18, 2);
            e.Property(x => x.PriceOverrideReason).HasMaxLength(500);
            e.Property(x => x.PricingSnapshotJson).HasColumnType("jsonb");
            e.HasIndex(x => x.DesignId);
            e.HasOne<Design>().WithMany()
                .HasForeignKey(x => new { x.DesignId, x.BusinessId })
                .HasPrincipalKey(d => new { d.Id, d.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
