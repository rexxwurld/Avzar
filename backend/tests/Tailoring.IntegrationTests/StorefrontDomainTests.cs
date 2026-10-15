using System.Text.Json;
using Tailoring.Domain.Billing;
using Tailoring.Domain.Storefront;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Pure storefront rules: no database. The validator is the wall between user input and the public site.</summary>
public sealed class StorefrontDomainTests
{
    private static StorefrontDocument Doc(string template = "standard") => StorefrontTemplates.Get(template).Build("Ada Atelier");

    private static EffectiveEntitlements Plan(string code)
    {
        var seed = DefaultPlans.All.Single(p => p.Code == code);
        return new EffectiveEntitlements(seed.Code, seed.Name, SubscriptionStatus.Active, seed.Values, [], DateTime.UtcNow);
    }

    private static IReadOnlyList<ValidationError> Errors(StorefrontDocument d) => StorefrontValidator.Validate(d).Errors;
    private static JsonElement Props(object o) => JsonSerializer.SerializeToElement(o, StorefrontJson.Options);
    private static bool Has(IReadOnlyList<PolicyViolation> v, string prefix) => v.Any(x => x.Code == prefix || x.Code.StartsWith(prefix + ":", StringComparison.Ordinal) || x.Code.StartsWith(prefix, StringComparison.Ordinal));
    private static SectionDoc First(StorefrontDocument d, string type) => d.Pages.SelectMany(p => p.Sections).First(s => s.Type == type);

    private static StorefrontDocument Mutated(string template, Action<StorefrontDocument> change)
    {
        var d = StorefrontJson.Clone(Doc(template));
        change(d);
        return d;
    }

    // ------------------------------------------------------------------ templates

    public static IEnumerable<object[]> TemplateKeys() => StorefrontTemplates.All.Select(t => new object[] { t.Key });

    [Theory, MemberData(nameof(TemplateKeys))]
    public void Every_template_builds_a_document_that_passes_its_own_validation(string key)
    {
        var errors = Errors(Doc(key));
        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));
    }

    [Theory, MemberData(nameof(TemplateKeys))]
    public void Every_template_survives_a_json_round_trip_unchanged(string key)
    {
        var doc = Doc(key);
        var json = StorefrontJson.Serialize(doc);
        Assert.Equal(json, StorefrontJson.Serialize(StorefrontJson.Deserialize(json)));
    }

    [Theory, MemberData(nameof(TemplateKeys))]
    public void A_template_fits_the_plan_that_includes_it_with_no_plan_violations(string key)
    {
        var template = StorefrontTemplates.Get(key);
        var plan = template.Tier switch { "standard" => "free", "basic" => "starter", "advanced" => "professional", _ => "business" };
        var violations = StorefrontPolicy.Violations(template.Build("X Studio"), Plan(plan));
        Assert.True(violations.Count == 0, $"{key} on {plan}: " + string.Join("; ", violations.Select(v => v.Message)));
    }

    [Fact]
    public void Templates_are_structurally_different_not_just_recoloured()
    {
        var signatures = StorefrontTemplates.All.Select(t =>
        {
            var home = t.Build("X").Pages.First(p => p.Kind == "home");
            return string.Join(">", home.Sections.Select(s => $"{s.Type}:{s.Variant}"));
        }).ToList();
        Assert.Equal(signatures.Count, signatures.Distinct().Count());

        var pageSets = StorefrontTemplates.All.Select(t => string.Join(",", t.Build("X").Pages.Select(p => p.Kind).Order())).Distinct().Count();
        Assert.True(pageSets >= 2, "some templates should have a different page set");
        Assert.True(StorefrontTemplates.All.Select(t => t.DefaultPreset).Distinct().Count() >= 4);
    }

    [Fact]
    public void Building_twice_gives_fresh_ids()
    {
        var a = Doc("luxury"); var b = Doc("luxury");
        Assert.Empty(a.Pages.Select(p => p.Id).Intersect(b.Pages.Select(p => p.Id)));
    }

    [Fact]
    public void Every_theme_preset_is_readable_by_wcag()
    {
        foreach (var preset in StorefrontCatalog.ThemePresets.Values)
        {
            Assert.Equal(StorefrontCatalog.ThemeColorKeys.Order(), preset.Colors.Keys.Order());
            Assert.All(preset.Colors.Values, c => Assert.True(StorefrontValidator.IsHexColor(c)));
            Assert.Contains(preset.HeadingFont, StorefrontCatalog.Fonts);
            Assert.Contains(preset.BodyFont, StorefrontCatalog.Fonts);
            var warnings = Contrast.Check(preset.Colors);
            Assert.True(warnings.Count == 0, $"{preset.Key}: " + string.Join("; ", warnings.Select(w => $"{w.Foreground}/{w.Background} {w.Ratio}")));
        }
    }

    [Fact]
    public void Built_in_presets_are_valid_sections()
    {
        foreach (var p in StorefrontPresets.All)
        {
            var section = new SectionDoc { Id = Guid.NewGuid(), Type = p.SectionType, Variant = p.Variant, Props = StorefrontPresets.PropsOf(p), Style = p.Style };
            var check = StorefrontValidator.ValidateSectionAlone(section);
            Assert.True(check.IsValid, $"{p.Key}: " + string.Join("; ", check.Errors.Select(e => $"{e.Path} {e.Message}")));
            Assert.Empty(check.MediaIds);
        }
    }

    // ------------------------------------------------------------------ validator: everything unsafe is refused

    [Fact]
    public void Unknown_section_types_and_layouts_are_refused()
    {
        Assert.Contains(Errors(Mutated("standard", d => d.Pages[0].Sections[0].Type = "script")), e => e.Path.EndsWith(".type"));
        Assert.Contains(Errors(Mutated("standard", d => First(d, "hero").Variant = "fullbleed")), e => e.Path.EndsWith(".variant")); // exists, but not in THIS template
        Assert.Contains(Errors(Mutated("standard", d => First(d, "hero").Variant = "<img onerror=x>")), e => e.Path.EndsWith(".variant"));
    }

    [Fact]
    public void A_template_cannot_hold_a_section_it_does_not_support()
    {
        var d = Mutated("standard", x => x.Pages[0].Sections.Add(new SectionDoc { Id = Guid.NewGuid(), Type = "pricing", Variant = "table", Props = Props(new { }) }));
        Assert.Contains(Errors(d), e => e.Message.Contains("does not support"));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("rgb(0,0,0)")]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("#000; background:url(x)")]
    public void Colours_must_be_plain_hex(string value)
    {
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Theme.Colors["primary"] = value)));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages[0].Sections[0].Style.Background = value)));
    }

    [Theory]
    [InlineData("Comic Sans MS")]
    [InlineData("Arial; } body { display:none")]
    [InlineData("'; alert(1); '")]
    public void Fonts_must_come_from_the_allow_list(string font)
    {
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Theme.HeadingFont = font)));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Theme.BodyFont = font)));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Hello <b>world</b>")]
    [InlineData("a > b")]
    public void Text_may_not_contain_angle_brackets(string text)
    {
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { headline = text }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages[0].Title = text)));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Branding.FooterText = text)));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Navigation[0].Label = text)));
    }

    [Fact]
    public void Unknown_props_wrong_types_and_missing_required_props_are_refused()
    {
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { headline = "x", onclick = "alert(1)" }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { headline = 42 }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { subheadline = "no headline" }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "designList").Props = Props(new { limit = 9999 }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "designList").Props = Props(new { limit = 2.5 }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "designList").Props = Props(new { categoryId = "not-a-guid" }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "hero").Style.Align = "justify")));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("http://example.com")]
    [InlineData("//evil.example.com")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("action:delete-everything")]
    [InlineData("/relative")]
    public void Links_may_only_go_to_own_pages_known_forms_or_https(string target)
        => Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { headline = "x", buttonLabel = "Go", buttonTarget = target }))));

    [Fact]
    public void Safe_links_are_accepted()
    {
        Assert.Empty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { headline = "x", buttonLabel = "Go", buttonTarget = "https://example.com/page" }))));
        Assert.Empty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { headline = "x", buttonLabel = "Go", buttonTarget = "action:contact" }))));
        Assert.Empty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { headline = "x", buttonLabel = "Go", buttonTarget = d.Pages[1].Id.ToString() }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { headline = "x", buttonLabel = "Go", buttonTarget = Guid.NewGuid().ToString() })))); // a page that does not exist
    }

    [Fact]
    public void List_sizes_and_text_lengths_are_bounded()
    {
        var tooMany = Enumerable.Range(0, 25).Select(_ => new { mediaId = Guid.NewGuid(), alt = "x", caption = "" }).ToArray();
        Assert.NotEmpty(Errors(Mutated("luxury", d => First(d, "gallery").Props = Props(new { heading = "g", columns = 3, images = tooMany }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => First(d, "hero").Props = Props(new { headline = new string('a', 121) }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages[0].Sections = Enumerable.Range(0, 41).Select(_ => new SectionDoc
        { Id = Guid.NewGuid(), Type = "richText", Variant = "simple", Props = Props(new { body = "x" }) }).ToList())));
    }

    [Fact]
    public void Media_and_reference_ids_are_collected_for_the_tenant_check()
    {
        var media = Guid.NewGuid(); var collection = Guid.NewGuid(); var category = Guid.NewGuid(); var logo = Guid.NewGuid();
        var d = Doc("african-heritage");
        First(d, "hero").Props = Props(new { headline = "h", imageMediaId = media });
        First(d, "collection").Props = Props(new { heading = "c", collectionId = collection });
        d.Branding.LogoMediaId = logo;
        d.Pages.First(p => p.Kind == "catalog").Sections[0].Props = Props(new { heading = "all", categoryId = category });
        var check = StorefrontValidator.Validate(d);
        Assert.True(check.IsValid, string.Join("; ", check.Errors.Select(e => e.Message)));
        Assert.Contains(media, check.MediaIds); Assert.Contains(logo, check.MediaIds);
        Assert.Contains(collection, check.CollectionIds); Assert.Contains(category, check.CategoryIds);
    }

    [Fact]
    public void Page_structure_rules_hold()
    {
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages.RemoveAll(p => p.Kind == "home"))));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages.Add(new PageDoc { Id = Guid.NewGuid(), Kind = "home", Slug = "", Title = "Another home" }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages.First(p => p.Kind == "about").Slug = "About Us")));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages.First(p => p.Kind == "about").Slug = "api")));            // reserved
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages.First(p => p.Kind == "about").Slug = "contact")));        // duplicate
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages.First(p => p.Kind == "home").Slug = "start")));           // home is ""
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages.Add(new PageDoc { Id = d.Pages[0].Id, Kind = "custom", Slug = "dup", Title = "Same id" }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages.Add(new PageDoc { Id = Guid.NewGuid(), Kind = "about", Slug = "about-2", Title = "Second about" }))));
        Assert.Empty(Errors(Mutated("standard", d => d.Pages.Add(new PageDoc { Id = Guid.NewGuid(), Kind = "custom", Slug = "care-guide", Title = "Care guide" }))));
    }

    [Fact]
    public void Navigation_and_section_ids_must_be_consistent()
    {
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Navigation[0].PageId = Guid.NewGuid())));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Navigation.Add(new NavItem { Label = "Again", PageId = d.Navigation[0].PageId }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages[1].Sections[0].Id = d.Pages[0].Sections[0].Id)));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Pages[0].Sections[0].Id = Guid.Empty)));
    }

    [Fact]
    public void Theme_and_branding_values_are_bounded()
    {
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Theme.Radius = 99)));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Theme.Preset = "neon")));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Theme.Colors["unknown"] = "#000000")));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Theme.ButtonShape = "triangle")));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Branding.LogoWidth = 5)));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Branding.SocialLinks.Add(new SocialLink { Network = "instagram", Url = "http://insecure.example" }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.Branding.SocialLinks.Add(new SocialLink { Network = "myspace", Url = "https://example.com" }))));
        Assert.Empty(Errors(Mutated("standard", d => d.Branding.SocialLinks.Add(new SocialLink { Network = "instagram", Url = "https://instagram.com/ada" }))));
        Assert.NotEmpty(Errors(Mutated("standard", d => d.SchemaVersion = 99)));
    }

    [Fact]
    public void Strict_parsing_rejects_unknown_properties_anywhere_in_a_document()
    {
        var json = StorefrontJson.Serialize(Doc());
        Assert.Throws<JsonException>(() => StorefrontJson.Deserialize(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"customCss\":\"body{display:none}\"")));
        Assert.Throws<JsonException>(() => StorefrontJson.Deserialize(json.Replace("\"preset\":", "\"onload\":\"alert(1)\",\"preset\":")));
        Assert.Throws<JsonException>(() => StorefrontJson.Deserialize("{\"pages\":\"not an array\"}"));
    }

    // ------------------------------------------------------------------ plan policy

    [Fact]
    public void Free_gets_the_standard_template_untouched_and_nothing_more()
    {
        var free = Plan("free");
        Assert.Empty(StorefrontPolicy.Violations(Doc("standard"), free));
        Assert.True(Has(StorefrontPolicy.Violations(Doc("luxury"), free), "template_tier"));
        Assert.True(Has(StorefrontPolicy.Violations(Mutated("standard", d => d.Theme.Preset = "elegant"), free), "theme_preset"));
        Assert.True(Has(StorefrontPolicy.Violations(Mutated("standard", d => d.Theme.Colors["primary"] = "#112233"), free), "theme_color:primary"));
        Assert.True(Has(StorefrontPolicy.Violations(Mutated("standard", d => d.Branding.LogoMediaId = Guid.NewGuid()), free), "branding_logo"));
        Assert.Contains(StorefrontPolicy.Violations(Mutated("standard", d => d.Pages.Add(new PageDoc { Id = Guid.NewGuid(), Kind = "custom", Slug = "x", Title = "X" })), free), v => v.Code is "custom_pages" or "pages_max");
    }

    [Fact]
    public void Starter_gets_basic_templates_a_limited_palette_and_a_logo_but_no_advanced_sections()
    {
        var starter = Plan("starter");
        Assert.Empty(StorefrontPolicy.Violations(Doc("bespoke"), starter));
        Assert.Empty(StorefrontPolicy.Violations(Doc("ready-to-wear"), starter));
        Assert.True(Has(StorefrontPolicy.Violations(Doc("luxury"), starter), "template_tier"));

        Assert.Empty(StorefrontPolicy.Violations(Mutated("bespoke", d => { d.Theme.Preset = "modern-luxury"; d.Theme.Colors["primary"] = "#AA5500"; d.Theme.Colors["accent"] = "#005599"; d.Branding.LogoMediaId = Guid.NewGuid(); d.Branding.FooterText = "Thank you"; }), starter));
        Assert.True(Has(StorefrontPolicy.Violations(Mutated("bespoke", d => d.Theme.Colors["background"] = "#101010"), starter), "theme_color:background"));
        Assert.True(Has(StorefrontPolicy.Violations(Mutated("bespoke", d => d.Theme.HeadingFont = "Lora"), starter), "theme_font:heading"));
        Assert.True(Has(StorefrontPolicy.Violations(Mutated("bespoke", d => d.Theme.Radius = 20), starter), "theme_style:radius"));
        Assert.Contains(StorefrontPolicy.Violations(Mutated("bespoke", d => d.Branding.SocialLinks.Add(new SocialLink { Network = "instagram", Url = "https://instagram.com/x" })), starter), v => v.Code == "branding_social");
        Assert.Contains(StorefrontPolicy.Violations(Mutated("bespoke", d => d.Pages[0].Sections.Add(new SectionDoc { Id = Guid.NewGuid(), Type = "richText", Variant = "simple", Props = Props(new { body = "x" }), Style = new SectionStyle { Background = "#ffeecc" } })), starter), v => v.Code == "section_style");
    }

    [Fact]
    public void Professional_unlocks_advanced_sections_colours_fonts_and_branding_but_not_the_top_tier()
    {
        var pro = Plan("professional");
        Assert.Empty(StorefrontPolicy.Violations(Doc("luxury"), pro));
        Assert.Empty(StorefrontPolicy.Violations(Mutated("luxury", d =>
        {
            d.Theme.Colors["background"] = "#202020"; d.Theme.HeadingFont = "Cinzel"; d.Theme.BodyFont = "Lato";
            d.Branding.ShareImageMediaId = Guid.NewGuid(); d.Branding.LogoWidth = 200; d.Branding.SocialLinks.Add(new SocialLink { Network = "tiktok", Url = "https://tiktok.com/@x" });
            First(d, "hero").Style.Background = "#111111";
        }), pro));
        Assert.True(Has(StorefrontPolicy.Violations(Doc("streetwear"), pro), "template_tier"));
        Assert.True(Has(StorefrontPolicy.Violations(Mutated("luxury", d => d.Theme.ButtonShape = "pill"), pro), "theme_style:buttonShape"));
        Assert.Contains(StorefrontPolicy.Violations(Mutated("luxury", d => d.Branding.Tagline = "Couture"), pro), v => v.Code == "branding_tagline");
        Assert.Contains(StorefrontPolicy.Violations(Mutated("luxury", d => First(d, "hero").Style.ButtonShape = "pill"), pro), v => v.Code == "section_button_shape");
    }

    [Fact]
    public void Business_gets_everything_up_to_its_page_limit()
    {
        var biz = Plan("business");
        Assert.Empty(StorefrontPolicy.Violations(Mutated("streetwear", d =>
        { d.Theme.ButtonShape = "pill"; d.Theme.Radius = 24; d.Theme.HeadingStyle = "uppercase"; d.Branding.Tagline = "Drop 01"; First(d, "hero").Style.ButtonShape = "square"; }), biz));

        var many = Mutated("standard", d => { for (var i = 0; i < 30; i++) d.Pages.Add(new PageDoc { Id = Guid.NewGuid(), Kind = "custom", Slug = $"p{i}", Title = $"P{i}" }); });
        Assert.Contains(StorefrontPolicy.Violations(many, biz), v => v.Code == "pages_max");
    }

    [Fact]
    public void Someone_on_a_lower_plan_may_remove_advanced_content_but_not_add_it()
    {
        var starter = Plan("starter");
        var before = Doc("bespoke");
        before.Theme.Colors["background"] = "#202020"; // an old, now-unavailable customisation
        var cleared = StorefrontJson.Clone(before); cleared.Theme.Colors.Clear();
        var worse = StorefrontJson.Clone(before); worse.Theme.HeadingFont = "Lora";

        Assert.Empty(StorefrontPolicy.NewViolations(cleared, before, starter));
        Assert.Empty(StorefrontPolicy.NewViolations(before, before, starter)); // unchanged: not the user's doing
        var morePages = StorefrontJson.Clone(Doc("bespoke")); morePages.Pages.Add(new PageDoc { Id = Guid.NewGuid(), Kind = "custom", Slug = "x1", Title = "X" });
        Assert.NotEmpty(StorefrontPolicy.NewViolations(morePages, Doc("bespoke"), starter)); // a new custom page is refused
        Assert.NotEmpty(StorefrontPolicy.Violations(before, starter));
        Assert.Contains(StorefrontPolicy.NewViolations(worse, before, starter), v => v.Code == "theme_font:heading"); // a second advanced item is new even though one already existed
    }

    [Fact]
    public void Enabled_workflows_must_stay_reachable_from_the_layout()
    {
        var d = Doc("standard");
        Assert.Empty(StorefrontPolicy.MissingEntryPoints(d, ["inquiry", "contact"]));
        Assert.Equal(new[] { "booking" }, StorefrontPolicy.MissingEntryPoints(d, ["inquiry", "booking"]));
        Assert.Equal(new[] { "customOrder" }, StorefrontPolicy.MissingEntryPoints(d, ["customOrder"]));

        foreach (var s in d.Pages.SelectMany(p => p.Sections).Where(s => s.Type == "inquiryForm")) s.Visible = false;
        Assert.Equal(new[] { "inquiry" }, StorefrontPolicy.MissingEntryPoints(d, ["inquiry"]));      // hidden sections do not count
        Assert.Empty(StorefrontPolicy.MissingEntryPoints(d, ["contact"]));                    // contactInfo still covers "contact"

        d.Pages.First(p => p.Kind == "contact").Visible = false;
        Assert.Equal(new[] { "contact" }, StorefrontPolicy.MissingEntryPoints(d, ["contact"]));       // hidden pages do not count
    }

    // ------------------------------------------------------------------ contrast

    [Fact]
    public void Contrast_matches_the_wcag_reference_values()
    {
        Assert.Equal(21.0, Contrast.Ratio("#000000", "#FFFFFF"), 1);
        Assert.Equal(1.0, Contrast.Ratio("#777777", "#777777"), 3);
        Assert.True(Contrast.Ratio("#767676", "#FFFFFF") >= 4.5);  // the classic accessible grey
        Assert.True(Contrast.Ratio("#777777", "#FFFFFF") < 4.5);
    }

    [Fact]
    public void Low_contrast_is_flagged_and_the_suggested_fix_actually_passes()
    {
        var colors = new Dictionary<string, string> { ["text"] = "#BBBBBB", ["background"] = "#FFFFFF", ["buttonText"] = "#FFFFFF", ["button"] = "#FFE08A" };
        var warnings = Contrast.Check(colors);
        Assert.Equal(2, warnings.Count);
        foreach (var w in warnings)
        {
            Assert.True(w.Ratio < w.Required);
            Assert.True(Contrast.Ratio(w.SuggestedForegroundHex, w.BackgroundHex) >= w.Required, $"{w.Foreground}: {w.SuggestedForegroundHex}");
            Assert.True(StorefrontValidator.IsHexColor(w.SuggestedForegroundHex));
        }
        Assert.Equal("#222222", Contrast.SuggestForeground("#222222", "#FFFFFF")); // already fine: unchanged
    }

    [Fact]
    public void Resolved_theme_is_the_preset_plus_valid_overrides_only()
    {
        var theme = new ThemeSettings { Preset = "elegant", HeadingFont = "Lora", Radius = 3 };
        theme.Colors["primary"] = "#123456"; theme.Colors["nonsense"] = "#000000"; theme.Colors["accent"] = "not-a-colour";
        var r = ResolvedTheme.From(theme);
        Assert.Equal("#123456", r.Colors["primary"]);
        Assert.Equal(StorefrontCatalog.ThemePresets["elegant"].Colors["accent"], r.Colors["accent"]);
        Assert.False(r.Colors.ContainsKey("nonsense"));
        Assert.Equal(("Lora", "Lato", 3), (r.HeadingFont, r.BodyFont, r.Radius));
        Assert.Equal("comfortable", r.SectionSpacing);
    }

    // ------------------------------------------------------------------ template switching

    private static int SectionCount(StorefrontDocument d) => d.Pages.Sum(p => p.Sections.Count) + d.Archive.Count;

    [Fact]
    public void Switching_to_a_smaller_template_archives_what_it_cannot_show_and_loses_nothing()
    {
        var luxury = Doc("luxury");
        var target = StorefrontTemplates.Get("streetwear");
        var plan = TemplateSwitcher.Plan(luxury, target);

        Assert.True(plan.NeedsConfirmation);
        Assert.Contains(plan.Archived, c => c.Type == "testimonials");
        Assert.Contains(plan.Archived, c => c.Type == "designerProfile");
        Assert.Equal(SectionCount(luxury), plan.Kept.Count + plan.Converted.Count + plan.Archived.Count);

        var switched = TemplateSwitcher.Apply(luxury, target, useTemplateTheme: false);
        Assert.Equal("streetwear", switched.TemplateKey);
        Assert.Equal(SectionCount(luxury), SectionCount(switched));       // nothing deleted
        Assert.Equal(plan.Archived.Count, switched.Archive.Count);
        Assert.Equal(luxury.Theme.Preset, switched.Theme.Preset);          // the owner's look is untouched
        var errors = Errors(switched);
        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));
    }

    [Fact]
    public void Unsupported_layouts_are_converted_to_one_the_new_template_has()
    {
        var d = Doc("luxury");
        var plan = TemplateSwitcher.Plan(d, StorefrontTemplates.Get("african-heritage"));
        var hero = plan.Converted.First(c => c.Type == "hero"); // luxury hero is fullbleed; african-heritage has split-right/left/centered
        Assert.Equal("fullbleed", hero.FromVariant);
        Assert.Contains(hero.ToVariant, StorefrontTemplates.Get("african-heritage").Supported["hero"]);
    }

    [Fact]
    public void Switching_back_restores_archived_sections_to_the_home_page()
    {
        var luxury = Doc("luxury");
        var away = TemplateSwitcher.Apply(luxury, StorefrontTemplates.Get("streetwear"), false);
        var back = TemplateSwitcher.Apply(away, StorefrontTemplates.Get("luxury"), false);

        Assert.Empty(back.Archive);
        Assert.Equal(SectionCount(luxury), SectionCount(back));
        Assert.Contains(back.Pages.First(p => p.Kind == "home").Sections, s => s.Type == "testimonials");
        Assert.Empty(Errors(back));
    }

    [Fact]
    public void A_template_switch_can_optionally_take_the_new_templates_theme()
    {
        var d = Doc("luxury"); d.Theme.Colors["primary"] = "#ABCDEF"; d.Theme.HeadingFont = "Cinzel";
        var keep = TemplateSwitcher.Apply(d, StorefrontTemplates.Get("bridal"), useTemplateTheme: false);
        var take = TemplateSwitcher.Apply(d, StorefrontTemplates.Get("bridal"), useTemplateTheme: true);
        Assert.Equal("#ABCDEF", keep.Theme.Colors["primary"]);
        Assert.Empty(take.Theme.Colors);
        Assert.Equal(StorefrontTemplates.Get("bridal").DefaultPreset, take.Theme.Preset);
        Assert.Null(take.Theme.HeadingFont);
    }

    [Fact]
    public void The_confirmation_token_changes_when_anything_it_describes_changes()
    {
        var d = Doc("luxury");
        var target = StorefrontTemplates.Get("streetwear");
        var a = TemplateSwitcher.Plan(d, target).Token;
        Assert.Equal(a, TemplateSwitcher.Plan(d, target).Token);                                   // stable
        Assert.NotEqual(a, TemplateSwitcher.Plan(d, StorefrontTemplates.Get("bridal")).Token);     // different target
        var edited = StorefrontJson.Clone(d); edited.Pages[0].Sections.RemoveAt(0);
        Assert.NotEqual(a, TemplateSwitcher.Plan(edited, target).Token);                           // different content
    }

    [Fact]
    public void Switching_to_the_same_template_changes_nothing()
    {
        var d = Doc("bridal");
        var plan = TemplateSwitcher.Plan(d, StorefrontTemplates.Get("bridal"));
        Assert.False(plan.NeedsConfirmation);
        Assert.Equal(StorefrontJson.Serialize(d), StorefrontJson.Serialize(TemplateSwitcher.Apply(d, StorefrontTemplates.Get("bridal"), false)));
    }
}
