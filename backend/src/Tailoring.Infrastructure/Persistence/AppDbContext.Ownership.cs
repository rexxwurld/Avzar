using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Access;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<OwnershipTransferChallenge> OwnershipTransferChallenges => Set<OwnershipTransferChallenge>();

    private static void ConfigureOwnership(ModelBuilder b)
    {
        b.Entity<OwnershipTransferChallenge>(e =>
        {
            e.ToTable("OwnershipTransferChallenges");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.RequestingUserId, x.ExpiresAtUtc });
            e.HasIndex(x => new { x.BusinessId, x.ConsumedAtUtc, x.CancelledAtUtc });
            e.HasOne<BusinessMembership>().WithMany()
                .HasForeignKey(x => new { x.NewOwnerMembershipId, x.BusinessId })
                .HasPrincipalKey(x => new { x.Id, x.BusinessId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
