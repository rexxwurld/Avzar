using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Email;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();

    private static void ConfigureEmail(ModelBuilder b)
    {
        b.Entity<EmailOutboxMessage>(e =>
        {
            e.ToTable("EmailOutbox", t => t.HasCheckConstraint("CK_EmailOutbox_Attempts", "\"Attempts\" >= 0"));
            e.Property(x => x.ToAddress).HasMaxLength(254).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            e.Property(x => x.TemplateKey).HasMaxLength(60);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.LastError).HasMaxLength(400);
            e.Property(x => x.ProviderMessageId).HasMaxLength(200);
            // The dispatcher only ever scans Pending rows that are due.
            e.HasIndex(x => x.NextAttemptUtc).HasFilter("\"Status\" = 'Pending'");
            e.HasIndex(x => x.BusinessId);
        });
    }
}
