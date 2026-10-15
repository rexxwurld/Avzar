using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Access;
using Tailoring.Domain.Tenancy;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<BusinessHoursDay> BusinessHours => Set<BusinessHoursDay>();
    public DbSet<BusinessHoliday> BusinessHolidays => Set<BusinessHoliday>();

    private static void ConfigureAccount(ModelBuilder b)
    {
        b.Entity<UserSession>(e =>
        {
            e.Property(x => x.DeviceName).HasMaxLength(80).IsRequired();
            e.Property(x => x.CurrentTokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.PreviousTokenHash).HasMaxLength(64);
            e.Property(x => x.RevokedReason).HasMaxLength(80);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<BusinessHoursDay>(e =>
        {
            e.ToTable("BusinessHours", t =>
            {
                t.HasCheckConstraint("CK_BusinessHours_Day", "\"DayOfWeek\" BETWEEN 0 AND 6");
                t.HasCheckConstraint("CK_BusinessHours_Times",
                    "(\"IsOpen\" = FALSE AND \"OpensAt\" IS NULL AND \"ClosesAt\" IS NULL) OR " +
                    "(\"IsOpen\" = TRUE AND \"OpensAt\" IS NOT NULL AND \"ClosesAt\" IS NOT NULL AND \"OpensAt\" < \"ClosesAt\")");
            });
            e.HasIndex(x => new { x.BusinessId, x.DayOfWeek }).IsUnique();
        });

        b.Entity<BusinessHoliday>(e =>
        {
            e.ToTable("BusinessHolidays");
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.BusinessId, x.Date }).IsUnique();
        });
    }
}
