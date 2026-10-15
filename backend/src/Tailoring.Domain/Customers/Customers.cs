using Tailoring.Domain.Common;

namespace Tailoring.Domain.Customers;

public enum CustomerStatus { Active, Inactive, Merged }

public class Customer : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public string? Phone { get; set; }
    public string? NormalizedPhone { get; set; }
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
    public string? GarmentPreferences { get; set; }
    public CustomerStatus Status { get; set; } = CustomerStatus.Active;
    /// <summary>Set when this profile was merged into another. Merged profiles are kept for traceability.</summary>
    public Guid? MergedIntoCustomerId { get; set; }
    public uint Version { get; set; }
}

public enum CommunicationChannel { Phone, Email, WhatsApp, InPerson, Other }
public enum CommunicationDirection { Inbound, Outbound }

public class CustomerCommunication : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid CustomerId { get; set; }
    public CommunicationChannel Channel { get; set; }
    public CommunicationDirection Direction { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = "";
    public Guid? RecordedByUserId { get; set; }
}

public enum FittingStatus { Scheduled, Completed, Cancelled, NoShow }

public class FittingAppointment : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid LocationId { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public FittingStatus Status { get; set; } = FittingStatus.Scheduled;
    public string? Notes { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
