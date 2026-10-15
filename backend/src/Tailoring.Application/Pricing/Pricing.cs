using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Pricing;

public sealed record MaterialLineInput
{
    [Required, StringLength(80, MinimumLength = 1)] public string Name { get; init; } = "";
    [Range(0, 1_000_000)] public decimal Quantity { get; init; }
    [Range(0, 1_000_000_000)] public decimal UnitCost { get; init; }
}

public sealed record LabourLineInput
{
    [Required, StringLength(80, MinimumLength = 1)] public string Task { get; init; } = "";
    [Range(0, 1000)] public decimal Hours { get; init; }
    /// <summary>Omit to use the profile's default hourly rate.</summary>
    [Range(0, 1_000_000)] public decimal? HourlyRate { get; init; }
}

public sealed record OtherCostInput
{
    [Required, StringLength(80, MinimumLength = 1)] public string Name { get; init; } = "";
    [Range(0, 1_000_000_000)] public decimal Amount { get; init; }
}

/// <summary>Costs for ONE unit. Quantity is applied by the order line.</summary>
public sealed record PricingInputDto
{
    [MaxLength(50)] public List<MaterialLineInput> Materials { get; init; } = new();
    [MaxLength(30)] public List<LabourLineInput> Labour { get; init; } = new();
    [MaxLength(30)] public List<OtherCostInput> Other { get; init; } = new();
    /// <summary>Scales labour only (an intricate design takes longer). 0.5 to 5.</summary>
    [Range(0.5, 5)] public decimal ComplexityMultiplier { get; init; } = 1m;
    /// <summary>Per-calculation override of the profile's method: Markup | Margin.</summary>
    public string? Method { get; init; }
    /// <summary>Per-calculation override of the profile's target percent (needs a Method or uses the profile's).</summary>
    [Range(0, 1000)] public decimal? TargetPercent { get; init; }
}

public sealed record PricingRulesDto(decimal DefaultHourlyRate, decimal OverheadPercent, string Method, decimal TargetPercent,
    decimal TaxPercent, decimal RoundingStep);

public sealed record PricingResultDto(
    decimal MaterialCost, decimal LabourCost, decimal OtherCost, decimal OverheadCost, decimal TotalCost,
    decimal SuggestedPriceBeforeRounding, decimal UnitPrice, decimal Profit, decimal MarginPercent, decimal MarkupPercent,
    decimal TaxAmount, decimal PriceIncludingTax);

/// <param name="ProfileConfigured">False when the business has not saved a pricing profile and starting defaults were used.</param>
public sealed record PricingCalculationDto(string CurrencyCode, bool ProfileConfigured, PricingRulesDto Rules, PricingResultDto Result);

public sealed record PricingProfileDto(bool IsConfigured, string CurrencyCode, decimal DefaultHourlyRate, decimal OverheadPercent,
    string Method, decimal TargetPercent, decimal TaxPercent, decimal RoundingStep, uint? Version);

public sealed record UpsertPricingProfileRequest
{
    [Range(0, 1_000_000)] public decimal DefaultHourlyRate { get; init; }
    [Range(0, 200)] public decimal OverheadPercent { get; init; }
    /// <summary>Markup | Margin</summary>
    [Required] public string Method { get; init; } = "Margin";
    [Range(0, 1000)] public decimal TargetPercent { get; init; }
    [Range(0, 100)] public decimal TaxPercent { get; init; }
    [Range(0, 1_000_000)] public decimal RoundingStep { get; init; }
    /// <summary>Required when a profile already exists.</summary>
    public uint? Version { get; init; }
}

public interface IPricingService
{
    Task<PricingProfileDto> GetProfileAsync(CancellationToken ct = default);
    Task<PricingProfileDto> UpsertProfileAsync(UpsertPricingProfileRequest request, CancellationToken ct = default);
    /// <summary>Stateless: saves nothing. The order service calls this again on save and stores its own result, so a client can never submit a made-up calculation.</summary>
    Task<PricingCalculationDto> CalculateAsync(PricingInputDto input, CancellationToken ct = default);
}
