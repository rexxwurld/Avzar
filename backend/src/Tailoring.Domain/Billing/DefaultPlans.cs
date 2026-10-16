namespace Tailoring.Domain.Billing;

public sealed record PlanSeed(string Code, string Name, string Description, int SortOrder, decimal MonthlyPrice,
    IReadOnlyDictionary<string, string> Values);

/// <summary>
/// INITIAL values only (from the master spec). They are inserted once; after that the database is the source of
/// truth and admins edit it. Values marked "default" were not in the spec and are my documented choices.
/// Custom domains are false everywhere until that feature ships (do not advertise before it is built and tested).
/// </summary>
public static class DefaultPlans
{
    public const string FreeCode = "free";

    private static Dictionary<string, string> V(
        long users, long customers, long orders, long designs, long storageGb, long appts, long storefronts, long pages,
        bool builder, bool customPages, bool sched, bool shop, bool auto,
        string template, string theme, string branding, string section, string inventory, string reports,
        bool partialPayments, bool expenses) => new()
    {
        [EntitlementKeys.UsersMax] = users.ToString(),
        [EntitlementKeys.CustomersActiveMax] = customers.ToString(),
        [EntitlementKeys.OrdersMonthlyMax] = orders.ToString(),
        [EntitlementKeys.DesignsMax] = designs.ToString(),
        [EntitlementKeys.StorageGbMax] = storageGb.ToString(),
        [EntitlementKeys.AppointmentsMonthlyMax] = appts.ToString(),
        [EntitlementKeys.StorefrontCountMax] = storefronts.ToString(),
        [EntitlementKeys.StorefrontPagesMax] = pages.ToString(),        // default (not in spec)
        [EntitlementKeys.StorefrontBuilder] = builder ? "true" : "false",
        [EntitlementKeys.StorefrontCustomPages] = customPages ? "true" : "false",
        [EntitlementKeys.StorefrontCustomDomain] = "false",
        [EntitlementKeys.FinancePartialPayments] = partialPayments ? "true" : "false",
        [EntitlementKeys.FinanceExpenses] = expenses ? "true" : "false",
        [EntitlementKeys.SchedulingIndividual] = sched ? "true" : "false",
        [EntitlementKeys.SchedulingShop] = shop ? "true" : "false",
        [EntitlementKeys.SchedulingAutoPlanning] = auto ? "true" : "false",
        [EntitlementKeys.AiFashionDesign] = inventory == "full" ? "true" : "false",
        [EntitlementKeys.AiAssistantBuilder] = template is "advanced" or "premium" ? "true" : "false",
        [EntitlementKeys.StorefrontTemplateTier] = template,
        [EntitlementKeys.StorefrontThemeLevel] = theme,
        [EntitlementKeys.StorefrontBrandingLevel] = branding,
        [EntitlementKeys.StorefrontSectionLevel] = section,
        [EntitlementKeys.InventoryLevel] = inventory,
        [EntitlementKeys.ReportsLevel] = reports,
    };

    public static readonly IReadOnlyList<PlanSeed> All =
    [
        new(FreeCode, "Free", "Individuals and small tailors getting started.", 0, 0m,
            V(1, 30, 20, 50, 1, 10, 1, 5, false, false, false, false, false, "standard", "none", "none", "none", "none", "basic", false, false)),
        new("starter", "Starter", "Growing tailors who need invoicing, pricing and a branded catalog.", 1, 5000m,
            V(3, 200, 100, 500, 5, 50, 1, 5, false, false, false, false, false, "basic", "basic", "basic", "none", "basic", "standard", true, true)),
        new("professional", "Professional", "Studios running a team, a schedule and a customized storefront.", 2, 10000m,
            V(10, 1000, 500, 2500, 25, 250, 1, 12, true, true, true, true, true, "advanced", "advanced", "advanced", "advanced", "full", "advanced", true, true)),
        new("business", "Business", "Established brands with multiple storefronts and advanced controls.", 3, 20000m,
            V(30, 5000, 2000, 10000, 100, 1000, 5, 25, true, true, true, true, true, "premium", "full", "full", "full", "full", "full", true, true)),
    ];
}
