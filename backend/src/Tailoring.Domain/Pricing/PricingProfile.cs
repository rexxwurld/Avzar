using Tailoring.Domain.Common;

namespace Tailoring.Domain.Pricing;

/// <summary>One per business. Absent until the owner saves it; until then <see cref="PriceCalculator.Defaults"/> apply and the API says so.</summary>
public class PricingProfile : Entity, ITenantScoped
{
    public Guid BusinessId { get; set; }
    public decimal DefaultHourlyRate { get; set; }
    public decimal OverheadPercent { get; set; }
    public PricingMethod Method { get; set; } = PricingMethod.Margin;
    public decimal TargetPercent { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal RoundingStep { get; set; }
    public uint Version { get; set; }
}
