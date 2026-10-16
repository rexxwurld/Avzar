using Tailoring.Domain.Common;

namespace Tailoring.Domain.Email;

public enum EmailStatus { Pending = 0, Sent = 1, Failed = 2 }

/// <summary>
/// One email to deliver. Platform-level (not ITenantScoped): background delivery runs with no tenant in scope and
/// BusinessId is attribution only. Bodies can contain one-time links, so they are purged once the message is terminal.
/// </summary>
public class EmailOutboxMessage : Entity
{
    public Guid? BusinessId { get; set; }
    public string ToAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public string TextBody { get; set; } = "";
    public string? HtmlBody { get; set; }
    public string? TemplateKey { get; set; }
    /// <summary>Optional stable caller key. A filtered unique index makes retries/replays safe without constraining legacy rows.</summary>
    public string? DeduplicationKey { get; set; }

    public EmailStatus Status { get; set; } = EmailStatus.Pending;
    /// <summary>Delivery attempts started (incremented when a dispatcher claims the row, so a crash mid-send still counts).</summary>
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    /// <summary>While in the future, another dispatcher owns the row.</summary>
    public DateTime? LeaseUntilUtc { get; set; }
    public string? LastError { get; set; }
    /// <summary>The provider ACCEPTED the message. This is not proof of inbox delivery; bounces need provider webhooks.</summary>
    public string? ProviderMessageId { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? BodyPurgedAtUtc { get; set; }

    /// <summary>What the provider reported AFTER accepting the message (webhook): see <see cref="EmailDeliveryStatus"/>. Null = no report yet.</summary>
    public string? DeliveryStatus { get; set; }
    public DateTime? DeliveryUpdatedAtUtc { get; set; }
    /// <summary>Short provider explanation (for example a bounce message). Never contains the recipient address.</summary>
    public string? DeliveryDetail { get; set; }
}

/// <summary>Provider-reported outcomes. Higher-severity reports replace lower ones, never the other way round (see EmailDeliveryRules).</summary>
public static class EmailDeliveryStatus
{
    public const string Delayed = "Delayed";
    public const string Delivered = "Delivered";
    public const string Bounced = "Bounced";
    public const string Complained = "Complained";
}
