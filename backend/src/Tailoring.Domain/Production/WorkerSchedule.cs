using Tailoring.Domain.Common;

namespace Tailoring.Domain.Production;

/// <summary>One worker's recurring local-time availability for a weekday (0 Sunday through 6 Saturday).</summary>
public sealed class WorkerAvailabilityDay : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid MembershipId { get; set; }
    public int DayOfWeek { get; set; }
    public bool IsAvailable { get; set; }
    public TimeOnly? OpensAt { get; set; }
    public TimeOnly? ClosesAt { get; set; }
    /// <summary>Maximum minutes of assigned production work per local calendar day.</summary>
    public int CapacityMinutes { get; set; }
}

public enum WorkerLeaveStatus { Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3 }

/// <summary>Approved absence interval in UTC. Leave is a scheduling constraint, not a production task.</summary>
public sealed class WorkerLeave : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid MembershipId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string Reason { get; set; } = "";
    public WorkerLeaveStatus Status { get; set; } = WorkerLeaveStatus.Approved;
    public Guid? CreatedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
}
