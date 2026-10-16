using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Billing;
namespace Tailoring.Infrastructure.Persistence;
public partial class AppDbContext
{
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    private static void ConfigureSubscriptionPayments(ModelBuilder b)
    {
        b.Entity<SubscriptionPayment>(e =>
        {
            e.Property(x => x.BillingPeriod).HasMaxLength(20).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18,2);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            e.Property(x => x.ProviderReference).HasMaxLength(120).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.ProviderReference).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.Status, x.CreatedAtUtc });
            e.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
