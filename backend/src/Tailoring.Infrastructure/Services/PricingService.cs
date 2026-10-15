using Microsoft.EntityFrameworkCore;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Pricing;
using Tailoring.Domain.Pricing;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

public sealed class PricingService(AppDbContext db, ITenantContext tenant, IAuditLogger audit) : IPricingService
{
    private Guid BusinessId => tenant.BusinessId ?? throw AppException.Unauthorized("no_tenant", "No business in scope.");

    public async Task<PricingProfileDto> GetProfileAsync(CancellationToken ct = default)
    {
        var currency = await CurrencyAsync(ct);
        var p = await db.PricingProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        return p is null ? ToDto(PriceCalculator.Defaults, currency, configured: false, version: null) : ToDto(p, currency);
    }

    public async Task<PricingProfileDto> UpsertProfileAsync(UpsertPricingProfileRequest req, CancellationToken ct = default)
    {
        var method = Parse.Value<PricingMethod>(req.Method, "method");
        var rules = new PricingRules(req.DefaultHourlyRate, req.OverheadPercent, method, req.TargetPercent, req.TaxPercent, req.RoundingStep);
        var probe = new PricingLines([new MaterialLine("probe", 1, 1)], [], []);
        if (PriceCalculator.Validate(rules, probe) is { Count: > 0 } errors)
            throw AppException.BadRequest("invalid_pricing_profile", string.Join(" ", errors));

        var currency = await CurrencyAsync(ct);
        var existing = await db.PricingProfiles.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            if (req.Version is not null)
                throw AppException.Conflict("stale_version", "The pricing profile no longer exists. Reload and try again.");
            existing = new PricingProfile { BusinessId = BusinessId };
            db.PricingProfiles.Add(existing);
        }
        else
        {
            if (req.Version is null)
                throw AppException.BadRequest("version_required", "Include the pricing profile's current version when updating.");
            db.Entry(existing).Property(x => x.Version).OriginalValue = req.Version.Value;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        existing.DefaultHourlyRate = rules.DefaultHourlyRate; existing.OverheadPercent = rules.OverheadPercent;
        existing.Method = method; existing.TargetPercent = rules.TargetPercent;
        existing.TaxPercent = rules.TaxPercent; existing.RoundingStep = rules.RoundingStep;

        audit.Record(BusinessId, tenant.UserId, "pricing.profile_saved", nameof(PricingProfile), existing.Id,
            new { method = method.ToString(), req.TargetPercent, req.OverheadPercent });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict("stale_version", "The pricing profile was changed by someone else. Reload and try again.");
        }
        catch (DbUpdateException)
        {
            // Two first-time saves raced for the unique per-business row.
            throw AppException.Conflict("stale_version", "The pricing profile was changed by someone else. Reload and try again.");
        }
        return ToDto(existing, currency);
    }

    public async Task<PricingCalculationDto> CalculateAsync(PricingInputDto input, CancellationToken ct = default)
    {
        var profile = await db.PricingProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        var rules = profile is null
            ? PriceCalculator.Defaults
            : new PricingRules(profile.DefaultHourlyRate, profile.OverheadPercent, profile.Method, profile.TargetPercent, profile.TaxPercent, profile.RoundingStep);

        // A per-calculation method/target override applies together: a bare percent would be ambiguous (markup or margin?).
        if (input.Method is not null || input.TargetPercent is not null)
        {
            var method = input.Method is null ? rules.Method : Parse.Value<PricingMethod>(input.Method, "method");
            rules = rules with { Method = method, TargetPercent = input.TargetPercent ?? rules.TargetPercent };
        }

        var lines = new PricingLines(
            input.Materials.Select(m => new MaterialLine(m.Name.Trim(), m.Quantity, m.UnitCost)).ToList(),
            input.Labour.Select(l => new LabourLine(l.Task.Trim(), l.Hours, l.HourlyRate)).ToList(),
            input.Other.Select(o => new OtherCostLine(o.Name.Trim(), o.Amount)).ToList(),
            input.ComplexityMultiplier);

        if (PriceCalculator.Validate(rules, lines) is { Count: > 0 } errors)
            throw AppException.BadRequest("invalid_pricing", string.Join(" ", errors));

        var o = PriceCalculator.Calculate(rules, lines);
        return new PricingCalculationDto(await CurrencyAsync(ct), profile is not null,
            new PricingRulesDto(rules.DefaultHourlyRate, rules.OverheadPercent, rules.Method.ToString(), rules.TargetPercent, rules.TaxPercent, rules.RoundingStep),
            new PricingResultDto(o.MaterialCost, o.LabourCost, o.OtherCost, o.OverheadCost, o.TotalCost, o.SuggestedPriceBeforeRounding,
                o.UnitPrice, o.Profit, o.MarginPercent, o.MarkupPercent, o.TaxAmount, o.PriceIncludingTax));
    }

    private Task<string> CurrencyAsync(CancellationToken ct) => db.Businesses.AsNoTracking().Select(b => b.CurrencyCode).FirstAsync(ct);

    private static PricingProfileDto ToDto(PricingProfile p, string currency) =>
        new(true, currency, p.DefaultHourlyRate, p.OverheadPercent, p.Method.ToString(), p.TargetPercent, p.TaxPercent, p.RoundingStep, p.Version);

    private static PricingProfileDto ToDto(PricingRules r, string currency, bool configured, uint? version) =>
        new(configured, currency, r.DefaultHourlyRate, r.OverheadPercent, r.Method.ToString(), r.TargetPercent, r.TaxPercent, r.RoundingStep, version);
}
