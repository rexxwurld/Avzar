using System.Text.Json;

namespace Tailoring.Domain.Storefront;

public sealed record BuiltInPreset(string Key, string Name, string SectionType, string Variant, object Props, SectionStyle Style);

/// <summary>Ready-made sections owners can drop in and edit. Text only: no images, so they are valid anywhere.</summary>
public static class StorefrontPresets
{
    public static readonly IReadOnlyList<BuiltInPreset> All =
    [
        new("hero-centered-cta", "Hero: centered with button", "hero", "centered",
            new { headline = "Made for you", subheadline = "Clothing cut to your measurements.", buttonLabel = "Get in touch", buttonTarget = "action:inquiry" }, new SectionStyle { Align = "center" }),
        new("hero-minimal", "Hero: minimal", "hero", "minimal",
            new { headline = "Tailoring with care" }, new SectionStyle()),
        new("about-story", "About: our story", "about", "split",
            new { heading = "Our story", body = "Tell visitors where you started, what you care about and how you work." }, new SectionStyle()),
        new("faq-basics", "FAQ: common questions", "faq", "accordion",
            new { heading = "Questions we hear a lot", items = new object[]
            {
                new { question = "How long does an order take?", answer = "Add your usual lead time here." },
                new { question = "Do you do alterations?", answer = "Add your alteration policy here." },
                new { question = "How do I pay?", answer = "Explain deposits and payment methods." },
            } }, new SectionStyle()),
        new("testimonials-trio", "Testimonials: three quotes", "testimonials", "cards",
            new { heading = "What customers say", items = new object[]
            {
                new { quote = "Add a customer's words here.", author = "Customer name", role = "" },
                new { quote = "Another happy customer.", author = "Customer name", role = "" },
                new { quote = "A third quote.", author = "Customer name", role = "" },
            } }, new SectionStyle { Align = "center" }),
        new("services-three", "Services: three offers", "services", "cards",
            new { heading = "What we do", items = new object[]
            {
                new { title = "Made-to-measure", description = "Cut and sewn for you.", priceText = "From on request" },
                new { title = "Alterations", description = "Fit and repair.", priceText = "" },
                new { title = "Occasion wear", description = "For weddings and events.", priceText = "" },
            } }, new SectionStyle()),
        new("pricing-simple", "Pricing: two options", "pricing", "cards",
            new { heading = "Pricing", tiers = new object[]
            {
                new { name = "Alteration", price = "From 0", features = new[] { "Quick turnaround" }, highlight = false },
                new { name = "Made-to-measure", price = "On request", features = new[] { "Consultation", "Fittings included" }, highlight = true },
            } }, new SectionStyle()),
        new("booking-simple", "Booking: request a fitting", "booking", "simple",
            new { heading = "Book a fitting", intro = "Choose a day and we will confirm it." }, new SectionStyle()),
    ];

    public static JsonElement PropsOf(BuiltInPreset p) => JsonSerializer.SerializeToElement(p.Props, StorefrontJson.Options);
}
