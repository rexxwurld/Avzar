using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Inventory;
using Tailoring.Domain.Tenancy;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<MaterialStock> Materials => Set<MaterialStock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    private static void ConfigureInventory(ModelBuilder b)
    {
        b.Entity<MaterialStock>(e =>
        {
            e.ToTable("Materials", t => t.HasCheckConstraint("CK_Materials_Quantities", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\" AND \"ReorderLevel\" >= 0"));
            e.Property(x => x.Sku).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(24).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.OnHand).HasPrecision(18, 3);
            e.Property(x => x.Reserved).HasPrecision(18, 3);
            e.Property(x => x.ReorderLevel).HasPrecision(18, 3);
            e.Property(x => x.Version).IsRowVersion();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Sku }).IsUnique();
            e.HasOne<BusinessLocation>().WithMany().HasForeignKey(x => new { x.LocationId, x.BusinessId }).HasPrincipalKey(x => new { x.Id, x.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockMovement>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.Property(x => x.OnHandAfter).HasPrecision(18, 3);
            e.Property(x => x.ReservedAfter).HasPrecision(18, 3);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.BusinessId, x.MaterialId, x.CreatedAtUtc });
            e.HasOne<MaterialStock>().WithMany().HasForeignKey(x => new { x.MaterialId, x.BusinessId }).HasPrincipalKey(x => new { x.Id, x.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
