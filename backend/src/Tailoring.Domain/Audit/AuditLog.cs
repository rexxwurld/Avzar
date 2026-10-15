using Tailoring.Domain.Common;

namespace Tailoring.Domain.Audit;

public class AuditLog : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string? DetailsJson { get; set; }
}
