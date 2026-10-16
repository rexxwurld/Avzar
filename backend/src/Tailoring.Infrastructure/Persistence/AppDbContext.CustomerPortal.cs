using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.CustomerPortal;
using Tailoring.Domain.Customers;
namespace Tailoring.Infrastructure.Persistence;
public partial class AppDbContext
{
    public DbSet<CustomerPortalGrant> CustomerPortalGrants => Set<CustomerPortalGrant>();
    private static void ConfigureCustomerPortal(ModelBuilder b)
    {
        b.Entity<CustomerPortalGrant>(e =>
        {
            e.ToTable("CustomerPortalGrants", t => t.HasCheckConstraint("CK_CustomerPortalGrants_UseCount", "\"UseCount\" >= 0"));
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.CustomerId, x.ExpiresAtUtc });
            e.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.CustomerId, x.BusinessId }).HasPrincipalKey(x => new { x.Id, x.BusinessId }).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
