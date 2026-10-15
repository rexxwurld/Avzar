namespace Tailoring.Domain.Pricing;

/// <summary>
/// Markup is a percentage ON COST (cost 100 + markup 50% = price 150, which is a 33.3% margin).
/// Margin is a percentage OF THE SELLING PRICE (cost 100 + margin 50% = price 200, which is a 100% markup).
/// They are different numbers; the calculator takes exactly one as the target and always reports both.
/// </summary>
public enum PricingMethod { Markup, Margin }

public sealed record PricingRules(decimal DefaultHourlyRate, decimal OverheadPercent, PricingMethod Method,
    decimal TargetPercent, decimal TaxPercent, decimal RoundingStep);

public sealed record MaterialLine(string Name, decimal Quantity, decimal UnitCost);
public sealed record LabourLine(string Task, decimal Hours, decimal? HourlyRate);
public sealed record OtherCostLine(string Name, decimal Amount);

public sealed record PricingLines(IReadOnlyList<MaterialLine> Materials, IReadOnlyList<LabourLine> Labour,
    IReadOnlyList<OtherCostLine> Other, decimal ComplexityMultiplier = 1m);

public sealed record PricingOutcome(
    decimal MaterialCost, decimal LabourCost, decimal OtherCost, decimal OverheadCost, decimal TotalCost,
    decimal SuggestedPriceBeforeRounding, decimal UnitPrice, decimal Profit, decimal MarginPercent, decimal MarkupPercent,
    decimal TaxAmount, decimal PriceIncludingTax);

/// <summary>Pure, deterministic, decimal-only. Every money figure is rounded to 2 places, half away from zero, line by line.</summary>
public static class PriceCalculator
{
    public const decimal MinComplexity = 0.5m, MaxComplexity = 5m;
    public const decimal MaxMarginTarget = 95m, MaxMarkupTarget = 1000m;

    /// <summary>Starting suggestions for a business that has not set up pricing. Hourly rate is deliberately 0: labour is never silently free, see <see cref="Validate"/>.</summary>
    public static readonly PricingRules Defaults = new(0m, 10m, PricingMethod.Margin, 40m, 0m, 0m);

    private static decimal R(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<string> Validate(PricingRules r, PricingLines l)
    {
        var errors = new List<string>();
        if (l.ComplexityMultiplier < MinComplexity || l.ComplexityMultiplier > MaxComplexity)
            errors.Add($"Complexity must be between {MinComplexity} and {MaxComplexity}.");
        if (r.DefaultHourlyRate < 0) errors.Add("Hourly rate cannot be negative.");
        if (r.OverheadPercent is < 0 or > 200) errors.Add("Overhead must be between 0% and 200%.");
        if (r.TaxPercent is < 0 or > 100) errors.Add("Tax must be between 0% and 100%.");
        if (r.RoundingStep < 0 || r.RoundingStep > 1_000_000) errors.Add("Rounding step must be between 0 and 1,000,000.");

        var max = r.Method == PricingMethod.Margin ? MaxMarginTarget : MaxMarkupTarget;
        if (r.TargetPercent < 0 || r.TargetPercent > max)
            errors.Add(r.Method == PricingMethod.Margin
                ? $"A margin target must be between 0% and {MaxMarginTarget}% (a 100% margin would need an infinite price)."
                : $"A markup target must be between 0% and {MaxMarkupTarget}%.");

        if (l.Materials.Any(m => m.Quantity < 0 || m.UnitCost < 0)) errors.Add("Material quantities and costs cannot be negative.");
        if (l.Labour.Any(x => x.Hours < 0 || x.HourlyRate < 0)) errors.Add("Labour hours and rates cannot be negative.");
        if (l.Other.Any(o => o.Amount < 0)) errors.Add("Other costs cannot be negative.");

        foreach (var x in l.Labour.Where(x => x.Hours > 0 && x.HourlyRate is null && r.DefaultHourlyRate <= 0))
            errors.Add($"Labour '{x.Task}' has no hourly rate. Enter a rate, or set a default hourly rate in the pricing profile.");

        if (errors.Count == 0 && Calculate(r, l).TotalCost <= 0)
            errors.Add("Add at least one cost with an amount above zero.");
        return errors;
    }

    /// <summary>Call <see cref="Validate"/> first; invalid input throws.</summary>
    public static PricingOutcome Calculate(PricingRules r, PricingLines l)
    {
        var material = l.Materials.Sum(m => R(m.Quantity * m.UnitCost));
        var labour = R(l.Labour.Sum(x => R(x.Hours * (x.HourlyRate ?? r.DefaultHourlyRate))) * l.ComplexityMultiplier);
        var other = l.Other.Sum(o => R(o.Amount));
        var overhead = R((material + labour) * r.OverheadPercent / 100m); // overhead applies to direct cost, not to other/outsourced costs
        var total = material + labour + other + overhead;

        var raw = r.Method == PricingMethod.Markup
            ? R(total * (1m + r.TargetPercent / 100m))
            : R(total / (1m - r.TargetPercent / 100m)); // target < 100 is guaranteed by Validate

        var price = r.RoundingStep > 0 ? R(Math.Ceiling(raw / r.RoundingStep) * r.RoundingStep) : raw; // always rounds UP, never eats margin
        var profit = price - total;
        var margin = price == 0 ? 0 : decimal.Round(profit / price * 100m, 2, MidpointRounding.AwayFromZero);
        var markup = total == 0 ? 0 : decimal.Round(profit / total * 100m, 2, MidpointRounding.AwayFromZero);
        var tax = R(price * r.TaxPercent / 100m);

        return new PricingOutcome(material, labour, other, overhead, total, raw, price, profit, margin, markup, tax, price + tax);
    }
}
