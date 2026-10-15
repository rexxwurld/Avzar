using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Designs;
using Tailoring.Domain.Media;
using Tailoring.Domain.Storefront;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<Storefront> Storefronts => Set<Storefront>();
    public DbSet<StorefrontVersion> StorefrontVersions => Set<StorefrontVersion>();
    public DbSet<StorefrontVersionMedia> StorefrontVersionMedia => Set<StorefrontVersionMedia>();
    public DbSet<StorefrontDomain> StorefrontDomains => Set<StorefrontDomain>();
    public DbSet<SectionPreset> SectionPresets => Set<SectionPreset>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();

    private static void ConfigureStorefront(ModelBuilder b)
    {
        b.Entity<Storefront>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(40).IsRequired();
            e.Property(x => x.TemplateKey).HasMaxLength(40).IsRequired();
            e.Property(x => x.DraftJson).HasColumnType("text"); // text, not jsonb: the media-in-use check searches it
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ContactEmail).HasMaxLength(120);
            e.Property(x => x.ContactPhone).HasMaxLength(40);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.OpeningHours).HasMaxLength(300);
            e.Property(x => x.Version).IsRowVersion();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => x.Slug).IsUnique();   // global: one subdomain, one site
            e.HasIndex(x => x.BusinessId);
            e.ToTable(t => t.HasCheckConstraint("CK_Storefronts_PublishedNeedsVersion", "\"Status\" <> 'Published' OR \"PublishedVersionId\" IS NOT NULL"));
        });

        b.Entity<StorefrontVersion>(e =>
        {
            e.Property(x => x.TemplateKey).HasMaxLength(40).IsRequired();
            e.Property(x => x.DocumentJson).HasColumnType("jsonb");
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.StorefrontId, x.VersionNumber }).IsUnique();
            e.HasOne<Storefront>().WithMany().HasForeignKey(x => new { x.StorefrontId, x.BusinessId })
                .HasPrincipalKey(s => new { s.Id, s.BusinessId }).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StorefrontVersionMedia>(e =>
        {
            e.HasKey(x => new { x.VersionId, x.MediaAssetId });
            e.HasIndex(x => x.MediaAssetId);
            e.HasOne<StorefrontVersion>().WithMany().HasForeignKey(x => new { x.VersionId, x.BusinessId })
                .HasPrincipalKey(v => new { v.Id, v.BusinessId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<MediaAsset>().WithMany().HasForeignKey(x => new { x.MediaAssetId, x.BusinessId })
                .HasPrincipalKey(m => new { m.Id, m.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StorefrontDomain>(e =>
        {
            e.Property(x => x.Hostname).HasMaxLength(253).IsRequired();
            e.Property(x => x.VerificationToken).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(300);
            e.HasIndex(x => x.Hostname).IsUnique();
            e.HasOne<Storefront>().WithMany().HasForeignKey(x => new { x.StorefrontId, x.BusinessId })
                .HasPrincipalKey(s => new { s.Id, s.BusinessId }).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SectionPreset>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.SectionType).HasMaxLength(30).IsRequired();
            e.Property(x => x.Variant).HasMaxLength(30).IsRequired();
            e.Property(x => x.PropsJson).HasColumnType("jsonb");
            e.Property(x => x.StyleJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique();
        });

        b.Entity<Inquiry>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Email).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            e.Property(x => x.DesignNameSnapshot).HasMaxLength(120);
            e.Property(x => x.GarmentType).HasMaxLength(60);
            e.Property(x => x.Budget).HasMaxLength(60);
            e.Property(x => x.InternalNote).HasMaxLength(1000);
            e.Property(x => x.IpHash).HasMaxLength(64);
            e.HasIndex(x => new { x.BusinessId, x.Status, x.CreatedAtUtc });
            e.HasIndex(x => new { x.StorefrontId, x.IpHash, x.CreatedAtUtc });
            e.ToTable(t => t.HasCheckConstraint("CK_Inquiries_Contactable", "\"Email\" IS NOT NULL OR \"Phone\" IS NOT NULL"));
            e.HasOne<Storefront>().WithMany().HasForeignKey(x => new { x.StorefrontId, x.BusinessId })
                .HasPrincipalKey(s => new { s.Id, s.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Design>().WithMany().HasForeignKey(x => new { x.DesignId, x.BusinessId })
                .HasPrincipalKey(d => new { d.Id, d.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
