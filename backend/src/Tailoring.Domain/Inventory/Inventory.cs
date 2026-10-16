using Tailoring.Domain.Common;

namespace Tailoring.Domain.Inventory;

public class MaterialStock : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid? LocationId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "m";
    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }
    public decimal ReorderLevel { get; set; }
    public string? Notes { get; set; }
    public uint Version { get; set; }
    public decimal Available => OnHand - Reserved;
}

public enum StockMovementKind { Receive, Adjust, Reserve, Release, Consume }
public class StockMovement : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid MaterialId { get; set; }
    public StockMovementKind Kind { get; set; }
    public decimal Quantity { get; set; }
    public decimal OnHandAfter { get; set; }
    public decimal ReservedAfter { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Note { get; set; }
}
