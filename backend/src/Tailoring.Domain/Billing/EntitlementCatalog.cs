namespace Tailoring.Domain.Billing;

public enum EntitlementKind { Flag, Limit, Tier }

/// <param name="Noun">Used in messages: "active customers", "new orders this month".</param>
/// <param name="Periodic">Limit that resets each period and is tracked in UsageCounters.</param>
public sealed record EntitlementDefinition(
    string Key, EntitlementKind Kind, string Description, string Noun,
    IReadOnlyList<string>? TierOrder = null, bool Periodic = false);

public static class EntitlementKeys
{
    // Limits (stock): compared against a live count
    public const string UsersMax = "users.max";
    public const string CustomersActiveMax = "customers.active.max";
    public const string DesignsMax = "designs.max";
    public const string StorageGbMax = "storage.gb.max";
    public const string StorefrontCountMax = "storefront.count.max";
    public const string StorefrontPagesMax = "storefront.pages.max";

    // Limits (periodic): tracked in UsageCounters
    public const string OrdersMonthlyMax = "orders.monthly.max";
    public const string AppointmentsMonthlyMax = "appointments.monthly.max";

    // Flags
    public const string StorefrontBuilder = "storefront.builder";
    public const string StorefrontCustomPages = "storefront.custom_pages";
    public const string StorefrontCustomDomain = "storefront.custom_domain";
    public const string FinancePartialPayments = "finance.partial_payments";
    public const string FinanceExpenses = "finance.expenses";
    public const string SchedulingIndividual = "scheduling.individual";
    public const string SchedulingShop = "scheduling.shop";
    public const string SchedulingAutoPlanning = "scheduling.auto_planning";
    public const string AiFashionDesign = "ai.fashion_design";
    public const string AiAssistantBuilder = "ai.assistant_builder";

    // Tiers
    public const string StorefrontTemplateTier = "storefront.template_tier";
    public const string StorefrontThemeLevel = "storefront.theme_level";
    public const string StorefrontBrandingLevel = "storefront.branding_level";
    public const string StorefrontSectionLevel = "storefront.section_level";
    public const string InventoryLevel = "inventory.level";
    public const string ReportsLevel = "reports.level";
}

/// <summary>
/// The single list of things a plan can grant. Adding a key here is how a later phase introduces a new gate.
/// Missing or unparseable values fall back to the most restrictive default (flag off, limit 0, lowest tier).
/// </summary>
public static class EntitlementCatalog
{
    private static readonly string[] Levels4 = ["none", "basic", "advanced", "full"];

    public static readonly IReadOnlyList<EntitlementDefinition> All =
    [
        new(EntitlementKeys.UsersMax, EntitlementKind.Limit, "Staff accounts", "staff accounts"),
        new(EntitlementKeys.CustomersActiveMax, EntitlementKind.Limit, "Active customers", "active customers"),
        new(EntitlementKeys.DesignsMax, EntitlementKind.Limit, "Saved designs", "saved designs"),
        new(EntitlementKeys.StorageGbMax, EntitlementKind.Limit, "Storage (GB)", "GB of storage"),
        new(EntitlementKeys.StorefrontCountMax, EntitlementKind.Limit, "Storefronts / catalogs", "storefronts"),
        new(EntitlementKeys.StorefrontPagesMax, EntitlementKind.Limit, "Pages per storefront", "storefront pages"),
        new(EntitlementKeys.OrdersMonthlyMax, EntitlementKind.Limit, "New orders per month", "new orders this month", Periodic: true),
        new(EntitlementKeys.AppointmentsMonthlyMax, EntitlementKind.Limit, "Appointments per month", "appointments this month", Periodic: true),

        new(EntitlementKeys.StorefrontBuilder, EntitlementKind.Flag, "Visual storefront builder", "the visual storefront builder"),
        new(EntitlementKeys.StorefrontCustomPages, EntitlementKind.Flag, "Custom storefront pages", "custom storefront pages"),
        new(EntitlementKeys.StorefrontCustomDomain, EntitlementKind.Flag, "Custom domain", "custom domains"),
        new(EntitlementKeys.FinancePartialPayments, EntitlementKind.Flag, "Deposits, partial payments and credit notes", "deposits, partial payments and credit notes"),
        new(EntitlementKeys.FinanceExpenses, EntitlementKind.Flag, "Expense tracking", "expense tracking"),
        new(EntitlementKeys.SchedulingIndividual, EntitlementKind.Flag, "Individual worker schedules", "worker schedules"),
        new(EntitlementKeys.SchedulingShop, EntitlementKind.Flag, "Shop-wide schedule", "the shop-wide schedule"),
        new(EntitlementKeys.SchedulingAutoPlanning, EntitlementKind.Flag, "Automatic daily planning", "automatic daily planning"),
        new(EntitlementKeys.AiFashionDesign, EntitlementKind.Flag, "AI fashion design image generation", "AI fashion design image generation"),
        new(EntitlementKeys.AiAssistantBuilder, EntitlementKind.Flag, "AI storefront assistant builder", "AI storefront assistant builder"),

        new(EntitlementKeys.StorefrontTemplateTier, EntitlementKind.Tier, "Template tier", "this template tier", ["standard", "basic", "advanced", "premium"]),
        new(EntitlementKeys.StorefrontThemeLevel, EntitlementKind.Tier, "Theme customization", "theme customization", Levels4),
        new(EntitlementKeys.StorefrontBrandingLevel, EntitlementKind.Tier, "Branding controls", "branding controls", Levels4),
        new(EntitlementKeys.StorefrontSectionLevel, EntitlementKind.Tier, "Section customization", "section customization", Levels4),
        new(EntitlementKeys.InventoryLevel, EntitlementKind.Tier, "Inventory", "inventory features", ["none", "basic", "full"]),
        new(EntitlementKeys.ReportsLevel, EntitlementKind.Tier, "Report depth", "this report level", ["basic", "standard", "advanced", "full"]),
    ];

    private static readonly Dictionary<string, EntitlementDefinition> ByKey = All.ToDictionary(d => d.Key);

    public static EntitlementDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    public static EntitlementDefinition Require(string key, EntitlementKind? kind = null)
    {
        var def = Find(key) ?? throw new ArgumentException($"Unknown entitlement key '{key}'.", nameof(key));
        if (kind is { } k && def.Kind != k)
            throw new ArgumentException($"Entitlement '{key}' is a {def.Kind}, not a {k}.", nameof(key));
        return def;
    }

    public const string Unlimited = "unlimited";

    /// <summary>The restrictive value used when a plan has no (valid) value for a key.</summary>
    public static string DefaultFor(EntitlementDefinition d) => d.Kind switch
    {
        EntitlementKind.Flag => "false",
        EntitlementKind.Limit => "0",
        _ => d.TierOrder![0],
    };

    /// <summary>Returns an error message, or null when <paramref name="raw"/> is valid for the key.</summary>
    public static string? Validate(string key, string? raw)
    {
        var def = Find(key);
        if (def is null) return $"Unknown entitlement '{key}'.";
        raw = raw?.Trim();
        switch (def.Kind)
        {
            case EntitlementKind.Flag:
                return raw is "true" or "false" ? null : $"'{key}' must be true or false.";
            case EntitlementKind.Limit:
                if (raw == Unlimited) return null;
                return long.TryParse(raw, out var n) && n >= 0 ? null : $"'{key}' must be a whole number >= 0 or 'unlimited'.";
            default:
                return def.TierOrder!.Contains(raw ?? "") ? null : $"'{key}' must be one of: {string.Join(", ", def.TierOrder!)}.";
        }
    }
}
