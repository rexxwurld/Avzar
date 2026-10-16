using Microsoft.EntityFrameworkCore;
using Tailoring.Domain.Access;
using Tailoring.Domain.Orders;
using Tailoring.Domain.Production;

namespace Tailoring.Infrastructure.Persistence;

public partial class AppDbContext
{
    private static void ConfigurePhase3(ModelBuilder b)
    {
        // Tasks reference garments through (Id, BusinessId); add the alternate key Phase 2 did not need.
        b.Entity<OrderGarment>().HasAlternateKey(x => new { x.Id, x.BusinessId });

        // ---------- Worker availability, capacity and leave ----------
        b.Entity<WorkerAvailabilityDay>(e =>
        {
            e.ToTable("WorkerAvailability", t =>
            {
                t.HasCheckConstraint("CK_WorkerAvailability_Day", "\"DayOfWeek\" BETWEEN 0 AND 6");
                t.HasCheckConstraint("CK_WorkerAvailability_Window", "(\"IsAvailable\" = FALSE AND \"OpensAt\" IS NULL AND \"ClosesAt\" IS NULL AND \"CapacityMinutes\" = 0) OR (\"IsAvailable\" = TRUE AND \"OpensAt\" IS NOT NULL AND \"ClosesAt\" IS NOT NULL AND \"OpensAt\" < \"ClosesAt\" AND \"CapacityMinutes\" BETWEEN 1 AND 1440)");
            });
            e.HasIndex(x => new { x.BusinessId, x.MembershipId, x.DayOfWeek }).IsUnique();
            e.HasOne<BusinessMembership>().WithMany()
                .HasForeignKey(x => new { x.MembershipId, x.BusinessId })
                .HasPrincipalKey(x => new { x.Id, x.BusinessId }).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WorkerLeave>(e =>
        {
            e.ToTable("WorkerLeaves", t => t.HasCheckConstraint("CK_WorkerLeaves_Interval", "\"StartsAtUtc\" < \"EndsAtUtc\""));
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.Property(x => x.ReviewNote).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.BusinessId, x.MembershipId, x.StartsAtUtc, x.EndsAtUtc });
            e.HasOne<BusinessMembership>().WithMany()
                .HasForeignKey(x => new { x.MembershipId, x.BusinessId })
                .HasPrincipalKey(x => new { x.Id, x.BusinessId }).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Workflows ----------
        b.Entity<ProductionWorkflow>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique();
            // At most one default workflow per business, enforced by the database.
            e.HasIndex(x => x.BusinessId).IsUnique().HasFilter("\"IsDefault\"").HasDatabaseName("UX_Workflows_OneDefault");
        });

        b.Entity<ProductionStage>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Instructions).HasMaxLength(2000);
            e.Property(x => x.RequiredSkill).HasMaxLength(60);
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.WorkflowId, x.Key }).IsUnique();
            e.HasOne<ProductionWorkflow>().WithMany(w => w.Stages)
                .HasForeignKey(x => new { x.WorkflowId, x.BusinessId })
                .HasPrincipalKey(w => new { w.Id, w.BusinessId }).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StageDependency>(e =>
        {
            e.HasKey(x => new { x.StageId, x.DependsOnStageId });
            e.ToTable(t => t.HasCheckConstraint("CK_StageDependencies_NotSelf", "\"StageId\" <> \"DependsOnStageId\""));
            e.HasOne<ProductionStage>().WithMany()
                .HasForeignKey(x => new { x.StageId, x.BusinessId })
                .HasPrincipalKey(s => new { s.Id, s.BusinessId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ProductionStage>().WithMany()
                .HasForeignKey(x => new { x.DependsOnStageId, x.BusinessId })
                .HasPrincipalKey(s => new { s.Id, s.BusinessId }).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Tasks ----------
        b.Entity<ProductionTask>(e =>
        {
            e.Property(x => x.StageName).HasMaxLength(80).IsRequired();
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Instructions).HasMaxLength(4000);
            e.Property(x => x.RequiredSkill).HasMaxLength(60);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.StatusBeforeBlock).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.BlockedType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.BlockedReason).HasMaxLength(2000);
            e.Property(x => x.CompletionNotes).HasMaxLength(2000);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.Property(x => x.RequiredMaterialsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Version).IsRowVersion();
            e.HasAlternateKey(x => new { x.Id, x.BusinessId });
            e.HasIndex(x => new { x.BusinessId, x.Status, x.DeadlineUtc });
            e.HasIndex(x => new { x.BusinessId, x.AssignedMembershipId, x.Status });
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.OrderGarmentId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ProductionTasks_Qty", "\"Quantity\" > 0 AND (\"EstimatedMinutes\" IS NULL OR \"EstimatedMinutes\" > 0)");
                t.HasCheckConstraint("CK_ProductionTasks_Dates", "\"CompletedAtUtc\" IS NULL OR \"Status\" = 'Completed'");
            });
            e.HasOne<Order>().WithMany()
                .HasForeignKey(x => new { x.OrderId, x.BusinessId })
                .HasPrincipalKey(o => new { o.Id, o.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrderGarment>().WithMany()
                .HasForeignKey(x => new { x.OrderGarmentId, x.BusinessId })
                .HasPrincipalKey(g => new { g.Id, g.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            // Assignee: optional (mixed nullability => optional by convention; never call IsRequired(false) here).
            e.HasOne<BusinessMembership>().WithMany()
                .HasForeignKey(x => new { x.AssignedMembershipId, x.BusinessId })
                .HasPrincipalKey(m => new { m.Id, m.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TaskDependency>(e =>
        {
            e.HasKey(x => new { x.TaskId, x.DependsOnTaskId });
            e.HasIndex(x => x.DependsOnTaskId);
            e.ToTable(t => t.HasCheckConstraint("CK_TaskDependencies_NotSelf", "\"TaskId\" <> \"DependsOnTaskId\""));
            e.HasOne<ProductionTask>().WithMany()
                .HasForeignKey(x => new { x.TaskId, x.BusinessId })
                .HasPrincipalKey(t => new { t.Id, t.BusinessId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProductionTask>().WithMany()
                .HasForeignKey(x => new { x.DependsOnTaskId, x.BusinessId })
                .HasPrincipalKey(t => new { t.Id, t.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TaskEvent>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(40).IsRequired();
            e.Property(x => x.FromStatus).HasMaxLength(20);
            e.Property(x => x.ToStatus).HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TaskId, x.CreatedAtUtc });
            e.HasOne<ProductionTask>().WithMany()
                .HasForeignKey(x => new { x.TaskId, x.BusinessId })
                .HasPrincipalKey(t => new { t.Id, t.BusinessId }).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<WorkerSkill>(e =>
        {
            e.HasKey(x => new { x.MembershipId, x.Skill });
            e.Property(x => x.Skill).HasMaxLength(60);
            e.HasOne<BusinessMembership>().WithMany()
                .HasForeignKey(x => new { x.MembershipId, x.BusinessId })
                .HasPrincipalKey(m => new { m.Id, m.BusinessId }).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
