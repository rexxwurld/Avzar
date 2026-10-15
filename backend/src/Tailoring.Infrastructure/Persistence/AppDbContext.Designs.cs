using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Designs;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<DesignCategory> DesignCategories => Set<DesignCategory>();
    public DbSet<Design> Designs => Set<Design>();

    private static void ConfigureDesigns(ModelBuilder b)
    {
        b.Entity<DesignCategory>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique();
        });

        b.Entity<Design>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.GarmentType).HasMaxLength(60);
            e.Property(x => x.BasePrice).HasPrecision(18, 2);
            e.Property(x => x.CurrencyCode).HasMaxLength(3);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.Status, x.Visibility });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Designs_PriceNonNegative", "\"BasePrice\" IS NULL OR \"BasePrice\" >= 0");
                t.HasCheckConstraint("CK_Designs_PriceHasCurrency", "\"BasePrice\" IS NULL OR \"CurrencyCode\" IS NOT NULL");
                // An archived design can never be public.
                t.HasCheckConstraint("CK_Designs_ArchivedIsPrivate", "\"Status\" <> 'Archived' OR \"Visibility\" = 'Private'");
            });
            // Composite FKs make cross-business links impossible at the database level.
            e.HasOne<DesignCategory>().WithMany()
                .HasForeignKey(x => new { x.CategoryId, x.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Design>().WithMany()
                .HasForeignKey(x => new { x.ParentDesignId, x.BusinessId })
                .HasPrincipalKey(d => new { d.Id, d.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
