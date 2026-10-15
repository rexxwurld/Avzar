using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Tailoring.Application.Common;
using Tailoring.Application.Pricing;

namespace Tailoring.Application.Orders;

public sealed record OrderGarmentDto(
    Guid Id, int LineNumber, string Name, string? Category, int Quantity, decimal UnitPrice, decimal LineTotal,
    string? Fabric, string? Colour, string? StyleInstructions, string? AlterationNotes,
    Guid? MeasurementSetId, int? MeasurementRevision, JsonElement? MeasurementSnapshot,
    Guid? DesignId = null, string? DesignName = null);

/// <summary>Costs and margins. Only returned by the pricing endpoint (pricing.view), never inside <see cref="OrderDto"/>.</summary>
public sealed record OrderLinePricingDto(Guid LineId, int LineNumber, string Name, int Quantity, decimal UnitPrice,
    decimal? DesignPriceSnapshot, string? PriceOverrideReason, JsonElement? Calculation);

public sealed record OrderPricingDto(Guid OrderId, string OrderNumber, string CurrencyCode, IReadOnlyList<OrderLinePricingDto> Lines);

public sealed record OrderDto(
    Guid Id, string OrderNumber, string Status, Guid CustomerId, string CustomerName, Guid LocationId,
    DateOnly PromisedDate, string Priority, string DeliveryMethod, string? DeliveryAddress, string? DeliveryInstructions,
    string? Notes, string CurrencyCode, decimal Subtotal, decimal Discount, decimal Total,
    uint Version, DateTime CreatedAtUtc, DateTime? QuotedAtUtc, DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc, string? CancellationReason, IReadOnlyList<OrderGarmentDto> Garments,
    DateTime? ReadyAtUtc = null, DateTime? DeliveredAtUtc = null, ProductionProgressDto? Progress = null);

/// <summary>Derived from the order's tasks on every read. Cancelled (not-required) tasks are excluded.</summary>
public sealed record ProductionProgressDto(int TotalTasks, int Completed, int InProgress, int Blocked, int Overdue, int PercentComplete);

public sealed record OrderSummaryDto(
    Guid Id, string OrderNumber, string Status, Guid CustomerId, string CustomerName, DateOnly PromisedDate,
    string Priority, decimal Total, string CurrencyCode, int GarmentCount);

public sealed record OrderListQuery : PagedQuery
{
    public string? Search { get; init; }          // order number or customer name
    public string? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? LocationId { get; init; }
    public DateOnly? DueFrom { get; init; }
    public DateOnly? DueTo { get; init; }
}

public sealed record OrderGarmentInput
{
    /// <summary>Existing line id when editing; omit for a new line.</summary>
    public Guid? Id { get; init; }
    [Required, StringLength(120, MinimumLength = 1)] public string Name { get; init; } = "";
    [StringLength(80)] public string? Category { get; init; }
    [Range(1, 10000)] public int Quantity { get; init; } = 1;
    [Range(0, 100000000)] public decimal UnitPrice { get; init; }
    [StringLength(120)] public string? Fabric { get; init; }
    [StringLength(60)] public string? Colour { get; init; }
    [StringLength(4000)] public string? StyleInstructions { get; init; }
    [StringLength(2000)] public string? AlterationNotes { get; init; }
    /// <summary>Customer measurement profile to snapshot for this garment.</summary>
    public Guid? MeasurementSetId { get; init; }
    /// <summary>Specific revision to snapshot; omit for the latest at the time of snapshotting.</summary>
    public int? MeasurementRevision { get; init; }
    /// <summary>Catalog design this line is made from. Active designs only. Name and price are snapshotted.</summary>
    public Guid? DesignId { get; init; }
    /// <summary>Costs for ONE unit. Needs pricing.view. The server recomputes it; the client's numbers are never trusted.</summary>
    public PricingInputDto? Pricing { get; init; }
    /// <summary>Required when UnitPrice differs from the calculated price.</summary>
    [StringLength(500)] public string? PriceOverrideReason { get; init; }
}

public abstract record OrderInputBase
{
    [Required] public Guid LocationId { get; init; }
    [Required] public DateOnly PromisedDate { get; init; }
    public string Priority { get; init; } = "Normal";
    public string DeliveryMethod { get; init; } = "Collection";
    [StringLength(500)] public string? DeliveryAddress { get; init; }
    [StringLength(1000)] public string? DeliveryInstructions { get; init; }
    [StringLength(4000)] public string? Notes { get; init; }
    [Range(0, 100000000)] public decimal Discount { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<OrderGarmentInput> Garments { get; init; } = new();
}

public sealed record CreateOrderRequest : OrderInputBase
{
    [Required] public Guid CustomerId { get; init; }
}

public sealed record UpdateOrderRequest : OrderInputBase
{
    [Required] public uint Version { get; init; }
}

public sealed record CancelOrderRequest
{
    [Required, StringLength(500, MinimumLength = 3)] public string Reason { get; init; } = "";
}

public interface IOrderService
{
    Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListQuery query, CancellationToken ct = default);
    Task<OrderDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Calculator snapshots per line. Callers must hold pricing.view (enforced at the API).</summary>
    Task<OrderPricingDto> GetPricingAsync(Guid id, CancellationToken ct = default);
    /// <summary>With an idempotency key, a retry returns the order created by the first call instead of a duplicate.</summary>
    Task<(OrderDto Order, bool Created)> CreateAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<OrderDto> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken ct = default);
    Task<OrderDto> QuoteAsync(Guid id, CancellationToken ct = default);
    Task<OrderDto> ConfirmAsync(Guid id, CancellationToken ct = default);
    Task<OrderDto> CancelAsync(Guid id, CancelOrderRequest request, CancellationToken ct = default);
    /// <summary>Ready -> Delivered. Refuses if any production task is still open.</summary>
    Task<OrderDto> DeliverAsync(Guid id, CancellationToken ct = default);
}
