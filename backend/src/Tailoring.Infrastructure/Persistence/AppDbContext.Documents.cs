using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Documents;
using Tailoring.Domain.Orders;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<StoredDocument> Documents => Set<StoredDocument>();

    private static void ConfigureDocuments(ModelBuilder b)
    {
        b.Entity<StoredDocument>(e =>
        {
            e.ToTable("Documents");
            e.Property(x => x.FileName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ContentType).HasMaxLength(80).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.ScanStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.BusinessId, x.CustomerId });
            e.HasIndex(x => new { x.BusinessId, x.OrderId });
            e.HasOne<Customer>().WithMany()
                .HasForeignKey(x => new { x.CustomerId, x.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Order>().WithMany()
                .HasForeignKey(x => new { x.OrderId, x.BusinessId })
                .HasPrincipalKey(o => new { o.Id, o.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Documents_Size", "\"SizeBytes\" > 0"));
        });
    }
}
