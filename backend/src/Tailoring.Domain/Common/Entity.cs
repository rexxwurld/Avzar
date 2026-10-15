namespace Tailoring.Domain.Common;

/// <summary>Marks an entity as owned by exactly one business (tenant).</summary>
public interface ITenantScoped
{
    Guid BusinessId { get; set; }
}

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
