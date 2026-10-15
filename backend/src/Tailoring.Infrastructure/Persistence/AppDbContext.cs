using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Domain.Access;
using Tailoring.Domain.Audit;
using Tailoring.Domain.Common;
using Tailoring.Domain.Customers;
using Tailoring.Domain.Measurements;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;
using Tailoring.Domain.Tenancy;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options)
        => _tenant = tenant;

    /// <summary>Read by the global query filters at query time (EF parameterises it per context instance).</summary>
    public Guid? CurrentBusinessId => _tenant.BusinessId;

    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessLocation> Locations => Set<BusinessLocation>();
    public DbSet<BusinessRole> BusinessRoles => Set<BusinessRole>();
    public DbSet<BusinessRolePermission> RolePermissions => Set<BusinessRolePermission>();
    public DbSet<BusinessMembership> Memberships => Set<BusinessMembership>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<MembershipLocation> MembershipLocations => Set<MembershipLocation>();
    public DbSet<StaffInvitation> Invitations => Set<StaffInvitation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Phase 2
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerCommunication> CustomerCommunications => Set<CustomerCommunication>();
    public DbSet<FittingAppointment> Fittings => Set<FittingAppointment>();
    public DbSet<MeasurementTemplate> MeasurementTemplates => Set<MeasurementTemplate>();
    public DbSet<MeasurementTemplateField> MeasurementTemplateFields => Set<MeasurementTemplateField>();
    public DbSet<CustomerMeasurementSet> MeasurementSets => Set<CustomerMeasurementSet>();
    public DbSet<MeasurementRevision> MeasurementRevisions => Set<MeasurementRevision>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderGarment> OrderGarments => Set<OrderGarment>();
    public DbSet<OrderCounter> OrderCounters => Set<OrderCounter>();

    // Phase 3
    public DbSet<ProductionWorkflow> Workflows => Set<ProductionWorkflow>();
    public DbSet<ProductionStage> Stages => Set<ProductionStage>();
    public DbSet<StageDependency> StageDependencies => Set<StageDependency>();
    public DbSet<ProductionTask> ProductionTasks => Set<ProductionTask>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<TaskEvent> TaskEvents => Set<TaskEvent>();
    public DbSet<WorkerSkill> WorkerSkills => Set<WorkerSkill>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e => e.Property(u => u.FullName).HasMaxLength(120));

        b.Entity<Business>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.Version).IsRowVersion(); // xmin
            e.HasQueryFilter(x => x.Id == CurrentBusinessId);
        });

        // --- Every tenant-scoped table: FK to Business, no cascade, BusinessId index ---
        foreach (var et in b.Model.GetEntityTypes().Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType)).ToList())
        {
            b.Entity(et.ClrType)
                .HasOne(typeof(Business)).WithMany()
                .HasForeignKey(nameof(ITenantScoped.BusinessId))
                .OnDelete(DeleteBehavior.Restrict);
            b.Entity(et.ClrType).HasIndex(nameof(ITenantScoped.BusinessId));

            typeof(AppDbContext)
                .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(et.ClrType)
                .Invoke(this, [b]);
        }

        b.Entity<BusinessLocation>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Address).HasMaxLength(500);
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique();
        });

        b.Entity<BusinessRole>(e =>
        {
            e.ToTable("BusinessRoles");
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique();
        });

        b.Entity<BusinessRolePermission>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.Permission });
            e.Property(x => x.Permission).HasMaxLength(80);
            e.HasOne<BusinessRole>().WithMany(r => r.Permissions)
                .HasForeignKey(x => new { x.RoleId, x.BusinessId })
                .HasPrincipalKey(r => new { r.Id, r.BusinessId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BusinessMembership>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // Composite FKs (id, BusinessId) make cross-business links impossible at the DB level.
        b.Entity<MembershipRole>(e =>
        {
            e.HasKey(x => new { x.MembershipId, x.RoleId });
            e.HasOne<BusinessMembership>().WithMany(m => m.Roles)
                .HasForeignKey(x => new { x.MembershipId, x.BusinessId })
                .HasPrincipalKey(m => new { m.Id, m.BusinessId })
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BusinessRole>().WithMany()
                .HasForeignKey(x => new { x.RoleId, x.BusinessId })
                .HasPrincipalKey(r => new { r.Id, r.BusinessId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MembershipLocation>(e =>
        {
            e.HasKey(x => new { x.MembershipId, x.LocationId });
            e.HasOne<BusinessMembership>().WithMany(m => m.Locations)
                .HasForeignKey(x => new { x.MembershipId, x.BusinessId })
                .HasPrincipalKey(m => new { m.Id, m.BusinessId })
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BusinessLocation>().WithMany()
                .HasForeignKey(x => new { x.LocationId, x.BusinessId })
                .HasPrincipalKey(l => new { l.Id, l.BusinessId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StaffInvitation>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.Email });
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(80).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            e.Property(x => x.DetailsJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
        });

        ConfigurePhase2(b);
        ConfigurePhase3(b);
        ConfigureBilling(b);
        ConfigureDesigns(b);
        ConfigureMedia(b);
        ConfigureDocuments(b);
        ConfigureCollections(b);
        ConfigurePricing(b);
        ConfigureFinance(b);
        ConfigureStorefront(b);
        ConfigureEmail(b);
        ConfigureAccount(b);
    }

    private void ApplyTenantFilter<T>(ModelBuilder b) where T : class, ITenantScoped
        => b.Entity<T>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        EnforceTenantRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    /// <summary>
    /// Second line of defence behind the query filters. When a tenant is in scope, no write may touch
    /// another business's rows, and new rows are stamped with the current business. Writes with NO tenant
    /// in scope (registration, invitation acceptance, background jobs) must set BusinessId explicitly.
    /// </summary>
    private void EnforceTenantRules()
    {
        var tenantId = _tenant.BusinessId;

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.BusinessId == Guid.Empty)
                    {
                        entry.Entity.BusinessId = tenantId
                            ?? throw new InvalidOperationException("BusinessId must be set when no tenant is in scope.");
                    }
                    else if (tenantId is not null && entry.Entity.BusinessId != tenantId)
                    {
                        throw new InvalidOperationException("Cross-tenant write blocked.");
                    }
                    break;
                case EntityState.Modified or EntityState.Deleted:
                    if (tenantId is not null && entry.Entity.BusinessId != tenantId)
                        throw new InvalidOperationException("Cross-tenant write blocked.");
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Entity>().Where(e => e.State == EntityState.Modified))
            entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
    }
}
