using Tailoring.Domain.Common;

namespace Tailoring.Domain.Orders;

/// <summary>InProduction and Ready are driven by production tasks, never set by hand. Delivered is an explicit action on a Ready order.</summary>
public enum OrderStatus { Draft, Quoted, Confirmed, InProduction, Ready, Delivered, Cancelled }
public enum OrderPriority { Low, Normal, High, Urgent }
public enum DeliveryMethod { Collection, Delivery }

public class Order : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid LocationId { get; set; }
    public Guid CustomerId { get; set; }
    public string OrderNumber { get; set; } = "";
    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public OrderPriority Priority { get; set; } = OrderPriority.Normal;
    public DateOnly PromisedDate { get; set; }
    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Collection;
    public string? DeliveryAddress { get; set; }
    public string? DeliveryInstructions { get; set; }
    public string? Notes { get; set; }

    /// <summary>Fixed at creation from the business currency. Never mixed with other currencies.</summary>
    public string CurrencyCode { get; set; } = "";
    /// <summary>Computed on the server from garment lines. Never accepted from clients.</summary>
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }

    public string? IdempotencyKey { get; set; }
    public DateTime? QuotedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? ReadyAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public uint Version { get; set; }
    public List<OrderGarment> Garments { get; set; } = new();
}

public class OrderGarment : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid OrderId { get; set; }
    public int LineNumber { get; set; }
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? Fabric { get; set; }
    public string? Colour { get; set; }
    public string? StyleInstructions { get; set; }
    public string? AlterationNotes { get; set; }

    /// <summary>Optional link to the catalog design this line was made from. The link survives the design being renamed or archived.</summary>
    public Guid? DesignId { get; set; }
    /// <summary>Frozen at link time so order history never changes when the catalog does.</summary>
    public string? DesignNameSnapshot { get; set; }
    public decimal? DesignPriceSnapshot { get; set; }
    /// <summary>Server-computed calculator inputs, rules and result at the time the price was set. Contains costs and margins: never exposed through order DTOs.</summary>
    public string? PricingSnapshotJson { get; set; }
    /// <summary>Why the final unit price differs from the calculated one.</summary>
    public string? PriceOverrideReason { get; set; }

    public Guid? MeasurementSetId { get; set; }
    public int? MeasurementRevision { get; set; }
    /// <summary>Frozen copy of the measurements used for production. Later customer changes never alter it.</summary>
    public string? MeasurementSnapshotJson { get; set; }
}

/// <summary>Per-business, per-year counter used to mint order numbers atomically.</summary>
public class OrderCounter : ITenantScoped
{
    public Guid BusinessId { get; set; }
    public int Year { get; set; }
    public long LastValue { get; set; }
}
