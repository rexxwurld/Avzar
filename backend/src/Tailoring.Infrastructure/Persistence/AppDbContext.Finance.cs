using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Finance;
using Tailoring.Domain.Orders;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();

    private static void ConfigureFinance(ModelBuilder b)
    {
        b.Entity<DocumentCounter>(e =>
        {
            e.HasKey(x => new { x.BusinessId, x.Kind, x.Year });
            e.Property(x => x.Kind).HasMaxLength(30);
        });

        b.Entity<Invoice>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Terms).HasMaxLength(2000);
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.TaxTotal).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.Version).IsRowVersion();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Number }).IsUnique().HasFilter("\"Number\" IS NOT NULL");
            e.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasIndex(x => new { x.BusinessId, x.Status, x.DueDate });
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.CustomerId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Invoices_Amounts", "\"Subtotal\" >= 0 AND \"TaxTotal\" >= 0 AND \"Total\" = \"Subtotal\" + \"TaxTotal\"");
                t.HasCheckConstraint("CK_Invoices_NumberWhenIssued", "\"Status\" = 'Draft' OR \"Number\" IS NOT NULL");
                t.HasCheckConstraint("CK_Invoices_VoidHasReason", "\"Status\" <> 'Void' OR \"VoidReason\" IS NOT NULL");
            });
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => new { l.InvoiceId, l.BusinessId })
                .HasPrincipalKey(i => new { i.Id, i.BusinessId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.CustomerId, x.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Order>().WithMany().HasForeignKey(x => new { x.OrderId, x.BusinessId })
                .HasPrincipalKey(o => new { o.Id, o.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<InvoiceLine>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(12, 2);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Discount).HasPrecision(18, 2);
            e.Property(x => x.TaxPercent).HasPrecision(5, 2);
            e.Property(x => x.NetAmount).HasPrecision(18, 2);
            e.Property(x => x.TaxAmount).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasIndex(x => new { x.InvoiceId, x.LineNumber }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_InvoiceLines_Amounts",
                "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"Discount\" >= 0 AND \"NetAmount\" >= 0 AND \"LineTotal\" = \"NetAmount\" + \"TaxAmount\""));
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.StatusReason).HasMaxLength(500);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.Version).IsRowVersion();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasIndex(x => new { x.InvoiceId, x.Status });
            e.HasIndex(x => x.CustomerId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" > 0");
                // A payment only counts once a named staff member verified it.
                t.HasCheckConstraint("CK_Payments_VerifiedHasVerifier", "\"Status\" <> 'Verified' OR (\"VerifiedAtUtc\" IS NOT NULL AND \"VerifiedByUserId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_Payments_ReasonWhenClosed", "\"Status\" NOT IN ('Rejected','Reversed') OR \"StatusReason\" IS NOT NULL");
            });
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => new { x.InvoiceId, x.BusinessId })
                .HasPrincipalKey(i => new { i.Id, i.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Receipt>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(30).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.BalanceAfter).HasPrecision(18, 2);
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => x.PaymentId).IsUnique(); // one receipt per payment
            e.HasIndex(x => new { x.BusinessId, x.Number }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Receipts_Amount", "\"Amount\" > 0"));
            e.HasOne<Payment>().WithMany().HasForeignKey(x => new { x.PaymentId, x.BusinessId })
                .HasPrincipalKey(p => new { p.Id, p.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CreditNote>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(30).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.BusinessId, x.Number }).IsUnique();
            e.HasIndex(x => x.InvoiceId);
            e.ToTable(t => t.HasCheckConstraint("CK_CreditNotes_Amount", "\"Amount\" > 0"));
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => new { x.InvoiceId, x.BusinessId })
                .HasPrincipalKey(i => new { i.Id, i.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ExpenseCategory>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique();
        });

        b.Entity<Expense>(e =>
        {
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Description).HasMaxLength(300).IsRequired();
            e.Property(x => x.Vendor).HasMaxLength(120);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.BusinessId, x.ExpenseDate });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Expenses_Amount", "\"Amount\" > 0");
                t.HasCheckConstraint("CK_Expenses_VoidHasReason", "\"VoidedAtUtc\" IS NULL OR \"VoidReason\" IS NOT NULL");
            });
            e.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => new { x.CategoryId, x.BusinessId })
                .HasPrincipalKey(c => new { c.Id, c.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
