using Tailoring.Domain.Common;

namespace Tailoring.Domain.Email;

/// <summary>
/// An address we must stop mailing because the provider reported a permanent bounce or a spam complaint.
/// Platform-level (not ITenantScoped): an address that bounces for one business bounces for all. Address is stored
/// trimmed and lower-case (see EmailAddressRules.SuppressionKey); one row per address.
/// </summary>
public class EmailSuppression : Entity
{
    public string Address { get; set; } = "";
    /// <summary>See <see cref="EmailSuppressionReason"/>.</summary>
    public string Reason { get; set; } = "";
}

public static class EmailSuppressionReason
{
    public const string Bounced = "Bounced";
    public const string Complained = "Complained";
}
