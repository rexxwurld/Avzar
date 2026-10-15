using System.Text.Json;

namespace Tailoring.Domain.Storefront;

/// <param name="Tier">Needs storefront.template_tier of at least this: standard | basic | advanced | premium.</param>
/// <param name="Supported">Section type to the layouts this template can render. A type not listed is unsupported (and is archived on a switch).</param>
public sealed record TemplateDef(string Key, string Name, string Description, string Tier, string DefaultPreset,
    IReadOnlyDictionary<string, string[]> Supported, Func<string, StorefrontDocument> Build);

/// <summary>
/// Eight structurally different starting points: different page sets, section order and layouts, not just colours.
/// Basic-tier templates use only core section types so they work on plans without the visual builder.
/// </summary>
public static class StorefrontTemplates
{
    public static readonly IReadOnlyList<string> Tiers = ["standard", "basic", "advanced", "premium"];

    // ---- tiny builders ---------------------------------------------------------------
    private static SectionDoc S(string type, string variant, object props, string? align = null, string? background = null) => new()
    {
        Id = Guid.NewGuid(), Type = type, Variant = variant,
        Props = JsonSerializer.SerializeToElement(props, StorefrontJson.Options),
        Style = new SectionStyle { Align = align ?? "left", Background = background },
    };

    private static PageDoc P(string kind, string slug, string title, params SectionDoc[] sections)
        => new() { Id = Guid.NewGuid(), Kind = kind, Slug = slug, Title = title, Sections = sections.ToList() };

    private static StorefrontDocument Doc(string key, string preset, params PageDoc[] pages)
    {
        var doc = new StorefrontDocument { TemplateKey = key, Theme = new ThemeSettings { Preset = preset }, Pages = pages.ToList() };
        doc.Navigation = doc.Pages.Where(p => p.Kind != "design")
            .Select(p => new NavItem { Label = p.Kind == "home" ? "Home" : p.Title, PageId = p.Id }).ToList();
        return doc;
    }

    private static readonly object None = new { };
    private static object Catalog(string heading = "Our designs", int limit = 48, bool prices = true) => new { heading, limit, showPrices = prices };
    private static object Inquiry(string heading = "Ask about a design", string intro = "Tell us what you have in mind and we will reply personally.") => new { heading, intro };
    private static object Contact(string heading = "Visit or contact us") => new { heading, showAddress = true, showPhone = true, showEmail = true, showHours = true };

    private static Dictionary<string, string[]> Sup(params (string Type, string[] Variants)[] entries) => entries.ToDictionary(e => e.Type, e => e.Variants);

    // ---- templates -------------------------------------------------------------------
    public static readonly IReadOnlyList<TemplateDef> All = new TemplateDef[]
    {
        new("standard", "Standard", "A clean, simple catalog site. Included in every plan.", "standard", "clean-professional",
            Sup(("hero", ["centered", "minimal"]), ("designList", ["grid", "list"]), ("about", ["simple"]), ("contactInfo", ["simple"]),
                ("inquiryForm", ["minimal"]), ("richText", ["simple"])),
            name => Doc("standard", "clean-professional",
                P("home", "", "Home",
                    S("hero", "centered", new { headline = name, subheadline = "Made-to-measure clothing, made with care.", buttonLabel = "Browse designs", buttonTarget = "action:inquiry" }, "center"),
                    S("designList", "grid", new { heading = "Featured designs", featuredOnly = true, limit = 8, showPrices = true }),
                    S("inquiryForm", "minimal", Inquiry())),
                P("catalog", "designs", "Designs", S("designList", "grid", Catalog())),
                P("design", "design", "Design", S("inquiryForm", "minimal", Inquiry("Interested in this design?"))),
                P("about", "about", "About", S("about", "simple", new { heading = $"About {name}", body = "Tell your customers who you are and how you work." })),
                P("contact", "contact", "Contact", S("contactInfo", "simple", Contact()), S("inquiryForm", "minimal", Inquiry("Send us a message"))))),

        new("bespoke", "Bespoke tailoring", "Process-led and personal: ideal for suits and made-to-measure by appointment.", "basic", "elegant",
            Sup(("hero", ["minimal", "centered", "split-right"]), ("about", ["split", "simple"]), ("designList", ["list", "editorial"]),
                ("contactInfo", ["split", "simple"]), ("inquiryForm", ["split", "minimal"]), ("richText", ["narrow", "simple"])),
            name => Doc("bespoke", "elegant",
                P("home", "", "Home",
                    S("hero", "minimal", new { headline = "Tailoring, built around you", subheadline = name, buttonLabel = "Book a consultation", buttonTarget = "action:inquiry" }),
                    S("richText", "narrow", new { heading = "How it works", body = "1. We talk about what you need.\n2. We take your measurements.\n3. We cut, sew and fit until it is right." }),
                    S("about", "split", new { heading = "The tailor", body = "Share your story, your training and what makes your work different." }),
                    S("designList", "editorial", new { heading = "Recent work", limit = 4, showPrices = false }),
                    S("inquiryForm", "split", Inquiry("Request a consultation"))),
                P("catalog", "designs", "Portfolio", S("designList", "editorial", Catalog("Portfolio", 48, false))),
                P("design", "design", "Design", S("inquiryForm", "split", Inquiry("Commission something like this"))),
                P("about", "about", "About", S("about", "split", new { heading = $"About {name}", body = "Tell your customers who you are and how you work." }), S("richText", "narrow", new { heading = "Our promise", body = "Every garment is fitted until you are happy." })),
                P("contact", "contact", "Contact", S("contactInfo", "split", Contact("Atelier and hours")), S("inquiryForm", "split", Inquiry("Send us a message"))))),

        new("ready-to-wear", "Ready-to-wear", "A shop-style layout with a dense product grid and clear prices.", "basic", "minimalist",
            Sup(("hero", ["centered", "fullbleed"]), ("designList", ["grid", "dense-grid", "editorial"]), ("about", ["simple"]),
                ("contactInfo", ["simple"]), ("inquiryForm", ["minimal"]), ("richText", ["simple"])),
            name => Doc("ready-to-wear", "minimalist",
                P("home", "", "Home",
                    S("hero", "fullbleed", new { headline = "New arrivals", subheadline = name, buttonLabel = "Shop now", buttonTarget = "action:inquiry" }, "center"),
                    S("designList", "dense-grid", new { heading = "Shop the collection", limit = 12, showPrices = true }),
                    S("richText", "simple", new { heading = "Delivery and returns", body = "Tell customers how long delivery takes and how returns work." }),
                    S("contactInfo", "simple", Contact())),
                P("catalog", "shop", "Shop", S("designList", "dense-grid", Catalog("Shop", 48, true))),
                P("design", "design", "Product", S("inquiryForm", "minimal", Inquiry("Ask about this item"))),
                P("about", "about", "About", S("about", "simple", new { heading = $"About {name}", body = "Tell customers about your brand." })),
                P("contact", "contact", "Contact", S("contactInfo", "simple", Contact()), S("inquiryForm", "minimal", Inquiry("Send us a message"))))),

        new("luxury", "Luxury fashion", "Dark, editorial and spacious: for high-end labels and couture.", "advanced", "modern-luxury",
            Sup(("hero", ["fullbleed", "minimal", "centered"]), ("featuredDesigns", ["editorial", "spotlight"]), ("designList", ["editorial", "grid"]),
                ("designerProfile", ["split", "large"]), ("testimonials", ["quotes", "slider"]), ("gallery", ["filmstrip", "grid"]),
                ("services", ["list"]), ("about", ["split"]), ("contactInfo", ["split"]), ("inquiryForm", ["minimal", "split"]),
                ("booking", ["split"]), ("richText", ["narrow"]), ("faq", ["accordion"])),
            name => Doc("luxury", "modern-luxury",
                P("home", "", "Home",
                    S("hero", "fullbleed", new { headline = name, subheadline = "Couture, made for you.", buttonLabel = "Private appointment", buttonTarget = "action:booking" }, "center"),
                    S("featuredDesigns", "editorial", new { heading = "The collection", limit = 6, showPrices = false }),
                    S("designerProfile", "split", new { name = "The Designer", role = "Creative director", bio = "Introduce the designer and the vision behind the label." }),
                    S("testimonials", "quotes", new { heading = "Kind words", items = new[] { new { quote = "Add a client testimonial here.", author = "A client", role = "" } } }),
                    S("inquiryForm", "minimal", Inquiry("Enquire"))),
                P("catalog", "the-collection", "Collection", S("designList", "editorial", Catalog("The collection", 48, false))),
                P("design", "design", "Piece", S("booking", "split", new { heading = "Book a private viewing", intro = "Visit the atelier to see this piece in person." })),
                P("about", "about", "The House", S("designerProfile", "large", new { name = "The Designer", role = "Creative director", bio = "Tell the story of the house." }), S("gallery", "filmstrip", new { heading = "Inside the atelier", columns = 4, images = Array.Empty<object>() })),
                P("services", "services", "Services", S("services", "list", new { heading = "Services", items = new[] { new { title = "Made-to-measure", description = "A garment cut for you.", priceText = "On request" } } }), S("faq", "accordion", new { heading = "Questions", items = new[] { new { question = "How long does a commission take?", answer = "Add your lead time here." } } })),
                P("contact", "contact", "Contact", S("contactInfo", "split", Contact("The atelier")), S("booking", "split", new { heading = "Request an appointment", intro = "Choose a date and we will confirm." })))),

        new("african-heritage", "African fashion", "Warm, pattern-friendly and story-led: for Ankara, Aso-oke and heritage designers.", "advanced", "african-heritage",
            Sup(("hero", ["split-right", "split-left", "centered"]), ("collection", ["mosaic", "cards"]), ("gallery", ["masonry", "grid"]),
                ("services", ["cards", "columns"]), ("testimonials", ["cards"]), ("designList", ["grid", "editorial"]),
                ("designerProfile", ["centered", "split"]), ("booking", ["simple", "split"]), ("about", ["split", "simple"]),
                ("contactInfo", ["simple", "split"]), ("inquiryForm", ["minimal", "split"]), ("faq", ["two-column", "accordion"]),
                ("richText", ["simple"]), ("customOrderForm", ["simple", "split"]), ("featuredDesigns", ["row"])),
            name => Doc("african-heritage", "african-heritage",
                P("home", "", "Home",
                    S("hero", "split-right", new { headline = name, subheadline = "Rooted in culture, cut for today.", buttonLabel = "Order a custom outfit", buttonTarget = "action:custom-order" }),
                    S("collection", "mosaic", new { heading = "Featured collection", limit = 8, showPrices = true }),
                    S("gallery", "masonry", new { heading = "From our studio", columns = 3, images = Array.Empty<object>() }),
                    S("services", "cards", new { heading = "What we make", items = new[] { new { title = "Traditional wear", description = "Agbada, kaftan, iro and buba, aso-ebi.", priceText = "" } } }),
                    S("testimonials", "cards", new { heading = "Happy customers", items = new[] { new { quote = "Add a customer testimonial here.", author = "A customer", role = "" } } }),
                    S("booking", "simple", new { heading = "Book a fitting", intro = "Pick a day and we will confirm it." })),
                P("catalog", "designs", "Designs", S("designList", "grid", Catalog())),
                P("design", "design", "Design", S("customOrderForm", "simple", new { heading = "Order this design made for you", intro = "Tell us your size, colours and event date." })),
                P("about", "about", "Our story", S("designerProfile", "centered", new { name = "The Designer", role = "Founder", bio = "Share your story and the culture behind your work." })),
                P("services", "services", "Services", S("services", "columns", new { heading = "Services", items = new[] { new { title = "Custom orders", description = "Made for your measurements.", priceText = "" } } }), S("faq", "two-column", new { heading = "Common questions", items = new[] { new { question = "Do you sew aso-ebi for groups?", answer = "Add your policy here." } } })),
                P("contact", "contact", "Contact", S("contactInfo", "simple", Contact()), S("inquiryForm", "minimal", Inquiry("Send us a message"))))),

        new("bridal", "Bridal fashion", "Soft, photo-led and consultation-first: for gowns, bridal parties and occasions.", "advanced", "elegant",
            Sup(("hero", ["split-left", "centered", "fullbleed"]), ("gallery", ["carousel", "grid"]), ("collection", ["lookbook", "cards"]),
                ("testimonials", ["slider", "cards"]), ("booking", ["split", "simple"]), ("faq", ["accordion"]), ("services", ["list", "cards"]),
                ("pricing", ["cards", "table"]), ("designerProfile", ["centered", "split"]), ("about", ["split", "simple"]),
                ("designList", ["editorial", "grid"]), ("contactInfo", ["split"]), ("inquiryForm", ["split"]), ("customOrderForm", ["split"]),
                ("richText", ["narrow"]), ("featuredDesigns", ["spotlight", "row"])),
            name => Doc("bridal", "elegant",
                P("home", "", "Home",
                    S("hero", "split-left", new { headline = "Your day, your dress", subheadline = name, buttonLabel = "Book a bridal consultation", buttonTarget = "action:booking" }),
                    S("gallery", "carousel", new { heading = "Real brides", columns = 3, images = Array.Empty<object>() }),
                    S("collection", "lookbook", new { heading = "The lookbook", limit = 8, showPrices = false }),
                    S("testimonials", "slider", new { heading = "Love notes", items = new[] { new { quote = "Add a bride's testimonial here.", author = "A bride", role = "" } } }),
                    S("booking", "split", new { heading = "Book a consultation", intro = "Choose a date for your first fitting." }),
                    S("faq", "accordion", new { heading = "Before you visit", items = new[] { new { question = "How early should I order?", answer = "Add your advice here." } } })),
                P("catalog", "gowns", "Gowns", S("designList", "editorial", Catalog("Gowns", 48, false))),
                P("design", "design", "Gown", S("customOrderForm", "split", new { heading = "Make this gown yours", intro = "Share your event date and preferences." })),
                P("about", "about", "About", S("designerProfile", "centered", new { name = "The Designer", role = "Bridal designer", bio = "Tell brides about your approach." })),
                P("services", "services", "Services", S("services", "list", new { heading = "Bridal services", items = new[] { new { title = "Bridal gown", description = "Designed and fitted for you.", priceText = "" } } }), S("pricing", "cards", new { heading = "Packages", tiers = new[] { new { name = "Essential", price = "On request", features = new[] { "Consultation", "Two fittings" }, highlight = false } } })),
                P("contact", "contact", "Contact", S("contactInfo", "split", Contact()), S("inquiryForm", "split", Inquiry("Send us a message"))))),

        new("streetwear", "Streetwear", "Loud, fast and product-first: for drops, merch and casual labels.", "premium", "bold-contemporary",
            Sup(("hero", ["fullbleed", "minimal"]), ("designList", ["dense-grid", "grid"]), ("gallery", ["filmstrip", "masonry", "grid"]),
                ("collection", ["strip", "mosaic"]), ("featuredDesigns", ["row", "spotlight"]), ("inquiryForm", ["minimal"]),
                ("contactInfo", ["simple"]), ("richText", ["simple"]), ("about", ["simple"]), ("faq", ["two-column"]), ("customOrderForm", ["simple"])),
            name => Doc("streetwear", "bold-contemporary",
                P("home", "", "Home",
                    S("hero", "fullbleed", new { headline = name, subheadline = "New drop. Limited run.", buttonLabel = "Shop the drop", buttonTarget = "action:inquiry" }, "center"),
                    S("collection", "strip", new { heading = "The drop", limit = 6, showPrices = true }),
                    S("designList", "dense-grid", new { heading = "All pieces", limit = 24, showPrices = true }),
                    S("gallery", "filmstrip", new { heading = "On the street", columns = 5, images = Array.Empty<object>() }),
                    S("inquiryForm", "minimal", Inquiry("Orders and collabs", "Tell us what you want."))),
                P("catalog", "shop", "Shop", S("designList", "dense-grid", Catalog("Shop", 48, true))),
                P("design", "design", "Piece", S("customOrderForm", "simple", new { heading = "Custom run", intro = "Want this in your colours or a bulk order? Tell us." })),
                P("about", "about", "About", S("about", "simple", new { heading = $"About {name}", body = "Tell your story." }), S("faq", "two-column", new { heading = "FAQ", items = new[] { new { question = "How do drops work?", answer = "Add your answer here." } } })),
                P("contact", "contact", "Contact", S("contactInfo", "simple", Contact()), S("inquiryForm", "minimal", Inquiry("Message us"))))),

        new("atelier-studio", "Atelier studio", "Designer-led and service-rich: for studios selling their craft, process and pricing.", "premium", "minimalist",
            Sup(("designerProfile", ["large", "split", "centered"]), ("gallery", ["grid", "masonry"]), ("services", ["list", "columns"]),
                ("pricing", ["table", "cards"]), ("testimonials", ["quotes", "cards"]), ("customOrderForm", ["split", "simple"]),
                ("booking", ["split", "simple"]), ("hero", ["minimal", "split-left", "centered"]), ("about", ["split"]), ("contactInfo", ["split"]),
                ("inquiryForm", ["split"]), ("faq", ["accordion", "two-column"]), ("designList", ["editorial", "list"]),
                ("collection", ["cards", "lookbook"]), ("featuredDesigns", ["editorial"]), ("richText", ["narrow", "simple"])),
            name => Doc("atelier-studio", "minimalist",
                P("home", "", "Home",
                    S("designerProfile", "large", new { name, role = "Designer and maker", bio = "Introduce yourself and your studio." }),
                    S("gallery", "grid", new { heading = "Selected work", columns = 3, images = Array.Empty<object>() }),
                    S("services", "columns", new { heading = "What I do", items = new[] { new { title = "Custom design", description = "From sketch to finished garment.", priceText = "From on request" } } }),
                    S("pricing", "table", new { heading = "Pricing", tiers = new[] { new { name = "Alteration", price = "From 0", features = new[] { "Quick turnaround" }, highlight = false } } }),
                    S("testimonials", "quotes", new { heading = "Clients say", items = new[] { new { quote = "Add a client testimonial here.", author = "A client", role = "" } } }),
                    S("customOrderForm", "split", new { heading = "Start a commission", intro = "Describe your idea and your deadline." }),
                    S("booking", "split", new { heading = "Book a studio visit", intro = "Pick a day that suits you." })),
                P("catalog", "work", "Work", S("designList", "editorial", Catalog("Work", 48, true))),
                P("design", "design", "Piece", S("customOrderForm", "split", new { heading = "Commission something like this", intro = "Share your size, colours and deadline." })),
                P("about", "about", "About", S("designerProfile", "split", new { name, role = "Designer and maker", bio = "Tell your story." }), S("faq", "accordion", new { heading = "Common questions", items = new[] { new { question = "How long does a commission take?", answer = "Add your lead time here." } } })),
                P("services", "services", "Services", S("services", "list", new { heading = "Services", items = new[] { new { title = "Custom design", description = "Made for you.", priceText = "" } } }), S("pricing", "cards", new { heading = "Pricing", tiers = new[] { new { name = "Standard", price = "On request", features = new[] { "Consultation" }, highlight = true } } })),
                P("contact", "contact", "Contact", S("contactInfo", "split", Contact()), S("inquiryForm", "split", Inquiry("Send a message"))))),
    };

    public static TemplateDef? Find(string key) => All.FirstOrDefault(t => t.Key == key);
    public static TemplateDef Get(string key) => Find(key) ?? throw new ArgumentException($"Unknown template '{key}'.", nameof(key));
    public static int TierIndex(string tier) => Tiers.ToList().IndexOf(tier);
}
