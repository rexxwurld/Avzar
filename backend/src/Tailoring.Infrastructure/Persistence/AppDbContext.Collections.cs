using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Designs;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<DesignCollection> DesignCollections => Set<DesignCollection>();
    public DbSet<DesignCollectionItem> DesignCollectionItems => Set<DesignCollectionItem>();

    private static void ConfigureCollections(ModelBuilder b)
    {
        b.Entity<DesignCollection>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Slug }).IsUnique();
        });

        b.Entity<DesignCollectionItem>(e =>
        {
            e.HasKey(x => new { x.CollectionId, x.DesignId });
            e.HasIndex(x => new { x.CollectionId, x.SortOrder });
            e.HasIndex(x => x.DesignId);
            e.HasOne<DesignCollection>().WithMany()
                .HasForeignKey(x => new { x.CollectionId, x.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Design>().WithMany()
                .HasForeignKey(x => new { x.DesignId, x.BusinessId })
                .HasPrincipalKey(d => new { d.Id, d.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
