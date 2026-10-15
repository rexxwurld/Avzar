using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Media;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<DesignImage> DesignImages => Set<DesignImage>();

    private static void ConfigureMedia(ModelBuilder b)
    {
        b.Entity<MediaAsset>(e =>
        {
            e.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.OriginalFileName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(60).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_MediaAssets_Size", "\"SizeBytes\" > 0");
                t.HasCheckConstraint("CK_MediaAssets_Dimensions", "\"Width\" > 0 AND \"Height\" > 0");
            });
        });

        b.Entity<DesignImage>(e =>
        {
            e.HasKey(x => new { x.DesignId, x.MediaAssetId });
            e.Property(x => x.AltText).HasMaxLength(200);
            e.HasIndex(x => new { x.DesignId, x.SortOrder });
            e.HasIndex(x => x.MediaAssetId);
            e.HasOne<Design>().WithMany()
                .HasForeignKey(x => new { x.DesignId, x.BusinessId })
                .HasPrincipalKey(d => new { d.Id, d.BusinessId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MediaAsset>().WithMany()
                .HasForeignKey(x => new { x.MediaAssetId, x.BusinessId })
                .HasPrincipalKey(m => new { m.Id, m.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
