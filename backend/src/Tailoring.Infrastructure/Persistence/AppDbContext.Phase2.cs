using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Measurements;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Tenancy;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    // NOTE: relationships whose FK mixes a nullable id with the non-nullable BusinessId (Customer.MergedInto,
    // OrderGarment.MeasurementSet, Fitting.Order) are optional by EF convention. Do not call IsRequired(false) on them.
    private static void ConfigurePhase2(ModelBuilder b)
    {
        // ---------- Customers ----------
        b.Entity<Customer>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.NormalizedEmail).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.NormalizedPhone).HasMaxLength(40);
            e.Property(x => x.AddressLine).HasMaxLength(300);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.NormalizedEmail });
            e.HasIndex(x => new { x.BusinessId, x.NormalizedPhone });
            e.HasIndex(x => new { x.BusinessId, x.FullName });
            e.HasOne<Customer>().WithMany()
                .HasForeignKey(x => new { x.MergedIntoCustomerId, x.BusinessId })
                .HasPrincipalKey(x => new { x.Id, x.BusinessId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CustomerCommunication>(e =>
        {
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });
            e.HasOne<Customer>().WithMany()
                .HasForeignKey(x => new { x.CustomerId, x.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- Measurements ----------
        b.Entity<MeasurementTemplate>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique();
        });

        b.Entity<MeasurementTemplateField>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(40).IsRequired();
            e.Property(x => x.Label).HasMaxLength(60).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(8).IsRequired();
            e.Property(x => x.MinValue).HasPrecision(10, 2);
            e.Property(x => x.MaxValue).HasPrecision(10, 2);
            e.HasIndex(x => new { x.TemplateId, x.Key }).IsUnique();
            e.HasOne<MeasurementTemplate>().WithMany(t => t.Fields)
                .HasForeignKey(x => new { x.TemplateId, x.BusinessId })
                .HasPrincipalKey(t => new { t.Id, t.BusinessId }).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CustomerMeasurementSet>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(80).IsRequired();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.CustomerId, x.Label }).IsUnique();
            e.HasOne<Customer>().WithMany()
                .HasForeignKey(x => new { x.CustomerId, x.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MeasurementTemplate>().WithMany()
                .HasForeignKey(x => new { x.TemplateId, x.BusinessId })
                .HasPrincipalKey(t => new { t.Id, t.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MeasurementRevision>(e =>
        {
            e.Property(x => x.ValuesJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
            // Two concurrent writers cannot both create revision N: the second fails and is reported as a conflict.
            e.HasIndex(x => new { x.SetId, x.RevisionNumber }).IsUnique();
            e.HasOne<CustomerMeasurementSet>().WithMany(s => s.Revisions)
                .HasForeignKey(x => new { x.SetId, x.BusinessId })
                .HasPrincipalKey(s => new { s.Id, s.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- Orders ----------
        b.Entity<Order>(e =>
        {
            e.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DeliveryMethod).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.Discount).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.DeliveryAddress).HasMaxLength(500);
            e.Property(x => x.DeliveryInstructions).HasMaxLength(1000);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.CancellationReason).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.OrderNumber }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasIndex(x => new { x.BusinessId, x.Status, x.PromisedDate });
            e.HasIndex(x => new { x.BusinessId, x.CustomerId });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Orders_Money", "\"Subtotal\" >= 0 AND \"Discount\" >= 0 AND \"Discount\" <= \"Subtotal\" AND \"Total\" = \"Subtotal\" - \"Discount\"");
            });
            e.HasOne<Customer>().WithMany()
                .HasForeignKey(x => new { x.CustomerId, x.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BusinessLocation>().WithMany()
                .HasForeignKey(x => new { x.LocationId, x.BusinessId })
                .HasPrincipalKey(l => new { l.Id, l.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OrderGarment>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Category).HasMaxLength(80);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.Property(x => x.Fabric).HasMaxLength(120);
            e.Property(x => x.Colour).HasMaxLength(60);
            e.Property(x => x.StyleInstructions).HasMaxLength(4000);
            e.Property(x => x.AlterationNotes).HasMaxLength(2000);
            e.Property(x => x.MeasurementSnapshotJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.OrderId, x.LineNumber });
            e.ToTable(t => t.HasCheckConstraint("CK_OrderGarments_Qty", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0"));
            e.HasOne<Order>().WithMany(o => o.Garments)
                .HasForeignKey(x => new { x.OrderId, x.BusinessId })
                .HasPrincipalKey(o => new { o.Id, o.BusinessId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CustomerMeasurementSet>().WithMany()
                .HasForeignKey(x => new { x.MeasurementSetId, x.BusinessId })
                .HasPrincipalKey(s => new { s.Id, s.BusinessId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OrderCounter>(e =>
        {
            e.ToTable("OrderCounters");
            e.HasKey(x => new { x.BusinessId, x.Year });
        });

        b.Entity<FittingAppointment>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => new { x.BusinessId, x.ScheduledAtUtc });
            e.HasOne<Customer>().WithMany()
                .HasForeignKey(x => new { x.CustomerId, x.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BusinessLocation>().WithMany()
                .HasForeignKey(x => new { x.LocationId, x.BusinessId })
                .HasPrincipalKey(l => new { l.Id, l.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Order>().WithMany()
                .HasForeignKey(x => new { x.OrderId, x.BusinessId })
                .HasPrincipalKey(o => new { o.Id, o.BusinessId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
